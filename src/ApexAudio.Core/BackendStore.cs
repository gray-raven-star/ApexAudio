using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using ApexAudio.Contracts;

namespace ApexAudio.Core;

public sealed class BackendStore(string dataRoot)
{
    public string Root { get; } = Path.Combine(dataRoot, "backends");
    public BackendDescription Describe(BackendDependency dependency)
    {
        var descriptor=BackendFiles.Read(Resolve(dependency),dependency);
        return new(descriptor.Id,descriptor.Version,dependency.Variant,descriptor.ContextVersion,descriptor.Support);
    }
    public string Resolve(BackendDependency dependency)
    {
        ValidateIdentity(dependency.Id, dependency.Version);
        string folder = Path.Combine(Root, dependency.Id, dependency.Version);
        if (!Directory.Exists(folder)) throw new OperationException("backend_missing", $"缺少共享后端 {dependency.Id} {dependency.Version}，请先导入对应 .gbackend 文件。", "import_backend",
            new Dictionary<string,string>{{"backendId",dependency.Id},{"version",dependency.Version},{"variant",dependency.Variant}});
        BackendFiles.Read(folder, dependency); return folder;
    }
    static void ValidateIdentity(string id, string version)
    {
        if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9.-]{2,79}$") || !Version.TryParse(version, out _)) throw new InvalidDataException("后端身份无效。");
    }
    // Offline, explicit import only. No URLs or scripts from a configuration pack are executed.
    public string Install(string archive)
    {
        Directory.CreateDirectory(Root);
        string stage = Path.Combine(Root, ".install-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            using (var zip = ZipFile.OpenRead(archive))
            {
                if (zip.Entries.Count > 1000 || zip.Entries.Sum(e => e.Length) > 256L * 1024 * 1024) throw new InvalidDataException("共享后端超过大小限制。");
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith('/')) continue;
                    string target = BackendFiles.PathIn(stage, entry.FullName);
                    if (!paths.Add(target)) throw new InvalidDataException("共享后端存在重复路径。");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var input = entry.Open(); using var output = new FileStream(target, FileMode.CreateNew); input.CopyTo(output);
                }
            }
            string descriptor = Path.Combine(stage, "backend.json");
            var d = JsonSerializer.Deserialize<BackendDescriptor>(File.ReadAllText(descriptor), Wire.Json) ?? throw new InvalidDataException("共享后端描述为空。");
            ValidateIdentity(d.Id, d.Version);
            if (d.Variants==null || d.Variants.Count == 0) throw new InvalidDataException("后端没有采集变体。");
            var requirement = new BackendDependency(d.Id, d.Version, BackendFiles.Hash(descriptor), d.Variants.Keys.First());
            BackendFiles.Read(stage, requirement);
            foreach (string file in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
                if (file != descriptor && !d.Files.ContainsKey(Path.GetRelativePath(stage, file).Replace('\\', '/'))) throw new InvalidDataException("后端包含未登记文件。");
            string destination = Path.Combine(Root, d.Id, d.Version);
            if (Directory.Exists(destination)) { BackendFiles.Read(destination, requirement); return destination; }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); Directory.Move(stage, destination); return destination;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
}

public static class V2Packages
{
    public const string HostVersion = "0.3.0";
    public static void Validate(string directory, PackManifest m)
    {
        if (m.Demo || m.Profiles.Count == 0 || m.Profiles.Select(p => p.Id).Distinct().Count() != m.Profiles.Count ||
            !Version.TryParse(m.MinHostVersion, out var minimum) || minimum > Version.Parse(HostVersion)) throw new InvalidDataException("V2 后端配置或主程序版本要求无效。");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        if (manifest.RootElement.TryGetProperty("entryPoint", out _)) throw new InvalidDataException("V2 配置包不能指定可执行入口。");
        foreach (string file in m.Files.Keys)
            if (!new[] { ".json", ".md", ".txt" }.Contains(Path.GetExtension(file).ToLowerInvariant()) && Path.GetFileName(file) != "LICENSE") throw new InvalidDataException("V2 试验包仅允许配置、文档和许可文件。");
        foreach (var p in m.Profiles)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || !m.Files.ContainsKey(p.Parameters) || !System.Text.RegularExpressions.Regex.IsMatch(p.Backend.DescriptorSha256, "^[0-9a-fA-F]{64}$")) throw new InvalidDataException("V2 Profile 缺少已登记参数或后端摘要。");
            BackendFiles.PathIn(directory, p.Parameters);
            if (!m.Games.Any(g => g.ProfileId == p.Id)) throw new InvalidDataException("V2 Profile 没有对应的游戏构建记录。");
        }
        if (m.Games.Any(g => g.Sha256 == null || !System.Text.RegularExpressions.Regex.IsMatch(g.Sha256, "^[0-9a-fA-F]{64}$") || !m.Profiles.Any(p => p.Id == g.ProfileId))) throw new InvalidDataException("V2 试验包要求完整的 EXE 构建摘要及 Profile 引用。");
        foreach (var build in m.Games)
            if (build.Modules != null && build.Modules.Any(m => string.IsNullOrWhiteSpace(m.Key) || m.Key.Contains('/') || m.Key.Contains('\\') || m.Key.Contains(':') || !Regex.IsMatch(m.Value, "^[0-9a-fA-F]{64}$")))
                throw new InvalidDataException("游戏构建的关键模块声明无效。");
    }
    public static CaptureProfile SelectOffline(InstalledPack pack)
    {
        if (pack.Manifest.Profiles.Count == 1) return pack.Manifest.Profiles[0];
        throw new OperationException("profile_conflict","包含多个采集方案，请先指定运行的游戏构建。","choose_target");
    }
}
