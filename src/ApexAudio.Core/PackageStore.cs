using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ApexAudio.Contracts;

namespace ApexAudio.Core;

public sealed record InstalledPack(string Directory, PackManifest Manifest, string? DataRoot = null)
{
    readonly PackageVersionRef? reference=File.Exists(Path.Combine(Directory,"manifest.json"))
        ? new(Manifest.Id,Manifest.Version,PackageStore.Hash(Path.Combine(Directory,"manifest.json"))) : null;
    public PackageVersionRef Reference => reference ?? throw new InvalidDataException("此包记录没有可读取的安装清单。");
}

public sealed partial class PackageStore
{
    public string Root { get; }
    public PackageStore(string dataRoot)
    {
        DataRoot = Path.GetFullPath(dataRoot); Root = Path.Combine(DataRoot, "packs");
        VersionsRoot = Path.Combine(DataRoot, "pack-versions");
        Directory.CreateDirectory(Root); Directory.CreateDirectory(VersionsRoot);
    }
    public static PackManifest ReadManifest(string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if(doc.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("适配包清单必须是对象。");
            foreach (string field in new[] { "schemaVersion", "id", "name", "version", "protocolVersion", "games", "presets", "files" })
                if (!doc.RootElement.TryGetProperty(field, out _)) throw new InvalidDataException("适配包清单缺少字段：" + field);
            var schema=doc.RootElement.GetProperty("schemaVersion");
            if(schema.ValueKind!=JsonValueKind.Number || !schema.TryGetInt32(out int version))throw new InvalidDataException("schemaVersion 必须是整数。");
            if (version == 1 && !doc.RootElement.TryGetProperty("entryPoint", out _)) throw new InvalidDataException("V1 适配包缺少 entryPoint。");
            var manifest=Wire.Read<PackManifest>(doc.RootElement);ValidateShape(manifest);return manifest;
        }
        catch(JsonException e){throw new InvalidDataException("适配包清单字段类型或 JSON 格式无效。",e);}
    }
    static void ValidateShape(PackManifest m)
    {
        if(m.Id==null || m.Name==null || m.Version==null || m.EntryPoint==null || m.Author==null || m.Source==null || m.Description==null ||
            m.Games==null || m.Presets==null || m.Files==null || m.Profiles==null ||
            m.Presets.Any(p=>p==null) || m.Files.Values.Any(h=>h==null) ||
            m.Games.Any(g=>g==null || g.Executable==null || g.Modules?.Values.Any(h=>h==null)==true) ||
            m.Profiles.Any(p=>p==null || p.Id==null || p.Parameters==null || p.Backend==null || p.Backend.Id==null ||
                p.Backend.Version==null || p.Backend.DescriptorSha256==null || p.Backend.Variant==null))
            throw new InvalidDataException("适配包清单包含不可为空的字段或条目。");
    }
    public static string SafePath(string root, string relative)
    {
        return BackendFiles.PathIn(root,relative);
    }
    public static string Hash(string file) { using var s = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(s)); }
    public static void Validate(string directory, PackManifest manifest)
    {
        ValidateShape(manifest);
        if (manifest.SchemaVersion is not (1 or 2) || manifest.ProtocolVersion != Wire.Version) throw new InvalidDataException("此适配包的格式或协议版本不受支持。");
        if (!Regex.IsMatch(manifest.Id, "^[a-z0-9][a-z0-9.-]{2,79}$") || !Version.TryParse(manifest.Version, out _) || string.IsNullOrWhiteSpace(manifest.Name)) throw new InvalidDataException("适配包身份信息无效。");
        if (manifest.SchemaVersion == 1 && (!manifest.EntryPoint.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !manifest.Files.ContainsKey(manifest.EntryPoint))) throw new InvalidDataException("适配包缺少启动程序。");
        if (manifest.SchemaVersion == 2) V2Packages.Validate(directory, manifest);
        var declared = new HashSet<string>(manifest.Files.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(directory, file).Replace('\\', '/');
            if (rel != "manifest.json" && !declared.Contains(rel)) throw new InvalidDataException("适配包含有未登记文件：" + rel);
        }
        foreach (var (path, hash) in manifest.Files)
        {
            string full = SafePath(directory, path);
            if (!File.Exists(full) || !Hash(full).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("适配包文件校验失败：" + path);
        }
        foreach (string preset in manifest.Presets)
        {
            if (!declared.Contains(preset)) throw new InvalidDataException("作者预设未登记。");
            PresetStore.Validate(JsonSerializer.Deserialize<SoundPreset>(File.ReadAllText(SafePath(directory, preset)), Wire.Json)!, manifest.Id);
        }
        if (!manifest.Demo && manifest.Games.Count == 0) throw new InvalidDataException("游戏适配包缺少识别规则。");
    }
    public PackManifest Inspect(string archive)
    {
        string stage = Path.Combine(VersionsRoot, ".inspect-" + Guid.NewGuid().ToString("N"));
        try { return ExtractAndValidate(archive, stage); }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    static PackManifest ExtractAndValidate(string archive, string stage)
    {
        Directory.CreateDirectory(stage);
            using (var zip = ZipFile.OpenRead(archive))
            {
                if (zip.Entries.Count > 10000 || zip.Entries.Sum(e => e.Length) > 1024L * 1024 * 1024) throw new InvalidDataException("适配包超过 V1 大小限制。");
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    string full = SafePath(stage, entry.FullName);
                    if (!paths.Add(full)) throw new InvalidDataException("适配包含有重复路径。");
                    if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(full); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    using var input = entry.Open(); using var output = new FileStream(full, FileMode.CreateNew); input.CopyTo(output);
                }
            }
        var manifest = ReadManifest(Path.Combine(stage, "manifest.json")); Validate(stage, manifest);
        return manifest;
    }
    public InstalledPack Install(string archive)
    {
        string stage = Path.Combine(VersionsRoot, ".install-" + Guid.NewGuid().ToString("N"));
        try
        {
            var manifest = ExtractAndValidate(archive, stage);
            using var catalog = LockCatalog();
            var existing = List().Where(p => p.Manifest.Id == manifest.Id && p.Manifest.Version == manifest.Version).ToArray();
            if (existing.Length != 0)
            {
                string hash = Hash(Path.Combine(stage,"manifest.json"));
                if (existing.Length != 1 || !existing[0].Reference.ManifestSha256.Equals(hash,StringComparison.OrdinalIgnoreCase))
                    throw new OperationException("version_content_conflict", "相同适配包版本已有不同内容，请使用新版本号。", "choose_package_version");
                Validate(existing[0].Directory, existing[0].Manifest); return existing[0];
            }
            string destination = SafePath(VersionsRoot, manifest.Id + "/" + manifest.Version);
            if(Directory.Exists(destination))throw new OperationException("package_invalid","已有相同版本的损坏安装记录，请打开数据目录处理后重新导入。","open_data_folder",new Dictionary<string,string>{{"directory",destination}});
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); Directory.Move(stage, destination);
            return new(destination, manifest, DataRoot);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    public void Uninstall(string id)
    {
        var versions = List().Where(p => p.Manifest.Id == id).ToArray();
        if (versions.Length != 1) throw new OperationException("package_invalid", "请明确选择要移除的适配包版本。", "choose_package_version");
        RemoveVersion(versions[0].Reference);
    }
}
