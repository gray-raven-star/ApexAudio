using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace ApexAudio.Contracts;

public sealed record BackendDependency(string Id, string Version, string DescriptorSha256, string Variant);
public sealed record CaptureProfile(string Id, BackendDependency Backend, string Parameters);
public sealed record BackendDescriptor(string Id, string Version, string EntryPoint, int ContextVersion,
    Dictionary<string, string> Files, Dictionary<string, string> Variants);
public sealed record TargetValidation(bool Allowed, string Detail);
public sealed record BackendIdentity(string Id, string Version, string Variant, string ContextSha256);
public sealed record LaunchContext(int Version, string PackId, string PackVersion, string PackSha256,
    string ProfileId, BackendDependency Backend, JsonElement Parameters);
public interface IPreparedAdapter
{
    BackendIdentity BackendIdentity { get; }
    TargetValidation ValidateTarget(ConnectRequest request);
}

public static class BackendFiles
{
    public static string Hash(string path) { using var f = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(f)); }
    public static string PathIn(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains(':') || relative.Contains('\\') || Path.IsPathRooted(relative)) throw new InvalidDataException("无效的后端文件路径。");
        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("后端文件越界。");
        if (relative.Split('/').Any(p => p is "." or ".." || p.EndsWith('.') || p.EndsWith(' '))) throw new InvalidDataException("后端文件路径包含无效部分。");
        for (string? p = full; p != null && p.Length >= Path.GetFullPath(root).Length; p = Path.GetDirectoryName(p))
            if (File.Exists(p) || Directory.Exists(p))
                if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("后端路径不能使用链接。");
        return full;
    }
    public static BackendDescriptor Read(string root, BackendDependency requirement)
    {
        string file = PathIn(root, "backend.json");
        if (!Hash(file).Equals(requirement.DescriptorSha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("共享后端描述校验失败。");
        var d = JsonSerializer.Deserialize<BackendDescriptor>(File.ReadAllText(file), Wire.Json) ?? throw new IOException("后端描述为空。");
        if (d.Id != requirement.Id || d.Version != requirement.Version || d.ContextVersion != 1 || !d.Variants.ContainsKey(requirement.Variant)) throw new IOException("共享后端版本或采集变体不匹配。");
        if (!d.Files.ContainsKey(d.EntryPoint) || !d.EntryPoint.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || d.Variants.Values.Any(v => !d.Files.ContainsKey(v))) throw new IOException("后端入口或变体文件未登记。");
        foreach (var (name, hash) in d.Files)
            if (!Hash(PathIn(root, name)).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("共享后端文件校验失败：" + name);
        return d;
    }
}
