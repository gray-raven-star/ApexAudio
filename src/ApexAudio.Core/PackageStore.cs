using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ApexAudio.Contracts;

namespace ApexAudio.Core;

public sealed record InstalledPack(string Directory, PackManifest Manifest);

public sealed class PackageStore
{
    public string Root { get; }
    public PackageStore(string dataRoot) { Root = Path.Combine(dataRoot, "packs"); Directory.CreateDirectory(Root); }
    public List<InstalledPack> List() => Directory.EnumerateDirectories(Root).Where(d => !Path.GetFileName(d).StartsWith('.'))
        .Select(d => new InstalledPack(d, ReadManifest(Path.Combine(d, "manifest.json")))).ToList();
    public static PackManifest ReadManifest(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        foreach (string field in new[] { "schemaVersion", "id", "name", "version", "protocolVersion", "entryPoint", "games", "presets", "files" })
            if (!doc.RootElement.TryGetProperty(field, out _)) throw new InvalidDataException("适配包清单缺少字段：" + field);
        return Wire.Read<PackManifest>(doc.RootElement);
    }
    public static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains(':') || relative.Contains('\\') || Path.IsPathRooted(relative)) throw new InvalidDataException("适配包路径无效。");
        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("适配包路径超出安装目录。");
        return full;
    }
    public static string Hash(string file) { using var s = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(s)); }
    public static void Validate(string directory, PackManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || manifest.ProtocolVersion != Wire.Version) throw new InvalidDataException("此适配包的格式或协议版本不受支持。");
        if (!Regex.IsMatch(manifest.Id, "^[a-z0-9][a-z0-9.-]{2,79}$") || !Version.TryParse(manifest.Version, out _) || string.IsNullOrWhiteSpace(manifest.Name)) throw new InvalidDataException("适配包身份信息无效。");
        if (!manifest.EntryPoint.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !manifest.Files.ContainsKey(manifest.EntryPoint)) throw new InvalidDataException("适配包缺少启动程序。");
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
        using var zip = ZipFile.OpenRead(archive);
        var entry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("文件不是完整 .gpack 适配包。");
        if (entry.Length > 2_000_000) throw new InvalidDataException("清单过大。");
        using var stream = entry.Open(); return JsonSerializer.Deserialize<PackManifest>(stream, Wire.Json) ?? throw new InvalidDataException("清单为空。");
    }
    public InstalledPack Install(string archive)
    {
        string stage = Path.Combine(Root, ".install-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
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
            string destination = Path.Combine(Root, manifest.Id), previous = Path.Combine(Root, ".previous-" + Guid.NewGuid().ToString("N"));
            bool exists = Directory.Exists(destination);
            if (exists) Directory.Move(destination, previous);
            try { Directory.Move(stage, destination); }
            catch { if (exists) Directory.Move(previous, destination); throw; }
            if (exists) Directory.Delete(previous, true);
            return new(destination, manifest);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    public void Uninstall(string id)
    {
        var pack = List().Single(p => p.Manifest.Id == id);
        Directory.Delete(pack.Directory, true);
    }
}
