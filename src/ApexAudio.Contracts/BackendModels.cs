using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace ApexAudio.Contracts;

public sealed record BackendDependency(string Id, string Version, string DescriptorSha256, string Variant);
public sealed record CaptureProfile(string Id, BackendDependency Backend, string Parameters);
public sealed record BackendDescriptor(string Id, string Version, string EntryPoint, int ContextVersion,
    Dictionary<string, string> Files, Dictionary<string, string> Variants,BackendSupport? Support=null);
public sealed record BackendSupport(string Platform,int ProtocolVersion,Dictionary<string,ParameterSupport> Parameters);
public sealed record ParameterSupport(string Format,int Version,int MaxBytes,JsonElement Constraints);
public sealed record BackendDescription(string Id,string Version,string Variant,int ContextVersion,BackendSupport? Support);
public sealed record ConfigurationDiagnostic(string File,string Path,string Code,string Message);
public interface IBackendConfigurationValidator
{
    IReadOnlyList<ConfigurationDiagnostic> Validate(BackendDescriptor descriptor,CaptureProfile profile,JsonElement parameters,IReadOnlyList<GameMatch> builds,string file);
}
public sealed record TargetValidation(bool Allowed, string Detail,OperationError? Failure=null,GameMatch[]? Matches=null);
public sealed record BackendIdentity(string Id, string Version, string Variant, string ContextSha256);
public sealed record LaunchContext(int Version, string PackId, string PackVersion, string PackSha256,
    string ProfileId, BackendDependency Backend, JsonElement Parameters,
    GameMatch? Build = null, TargetIdentity? Target = null,GameMatch[]? Candidates=null,string? ParametersFile=null);
public sealed record TargetIdentity(int ProcessId, long StartedUtcTicks, string Path)
{
    public static TargetIdentity Read(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return new(pid, process.StartTime.ToUniversalTime().Ticks,
                process.MainModule?.FileName ?? throw new OperationException("evidence_insufficient","无法读取目标进程路径。","check_permissions"));
        }
        catch(Exception e)when(e is ArgumentException or InvalidOperationException)
        {throw new OperationException("target_changed","目标进程已退出或更换，请重新选择。","choose_target",innerException:e);}
        catch(System.ComponentModel.Win32Exception e)
        {throw new OperationException("evidence_insufficient","无法读取目标身份："+e.Message,"check_permissions",innerException:e);}
    }
    public void Validate(ConnectRequest request)
    {
        if (request.ProcessId != ProcessId || request.GamePath != null &&
            !System.IO.Path.GetFullPath(request.GamePath).Equals(Path, StringComparison.OrdinalIgnoreCase))
            throw new OperationException("target_changed","连接请求与本次会话的目标不一致。","choose_target");
        var actual = Read(ProcessId);
        if (actual.StartedUtcTicks != StartedUtcTicks || !actual.Path.Equals(Path, StringComparison.OrdinalIgnoreCase))
            throw new OperationException("target_changed","目标进程已更换，请重新连接。","choose_target");
    }
}
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
        if (d.Files==null || d.Variants==null || d.EntryPoint==null || d.Id != requirement.Id || d.Version != requirement.Version || d.ContextVersion is not (1 or 2) || !d.Variants.ContainsKey(requirement.Variant)) throw new IOException("共享后端版本或采集变体不匹配。");
        if(d.Support is {} support)
        {
            if(support.Platform!="windows-x64" || support.ProtocolVersion!=Wire.Version || support.Parameters==null ||
                d.Variants.Keys.Any(v=>!support.Parameters.ContainsKey(v)) || support.Parameters.Keys.Any(v=>!d.Variants.ContainsKey(v)) ||
                support.Parameters.Values.Any(p=>p==null || string.IsNullOrWhiteSpace(p.Format) || p.Version<1 || p.MaxBytes is <1 or >4194304 || p.Constraints.ValueKind!=JsonValueKind.Object))
                throw new OperationException("backend_unsupported","共享后端声明的平台、协议或参数支持范围不受支持。","choose_backend_version");
        }
        if (!d.Files.ContainsKey(d.EntryPoint) || !d.EntryPoint.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || d.Variants.Values.Any(v => !d.Files.ContainsKey(v))) throw new IOException("后端入口或变体文件未登记。");
        var directories = new Stack<string>();
        directories.Push(root);
        while (directories.TryPop(out var directory))
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("共享后端路径不能使用链接：" + relative);
                if ((attributes & FileAttributes.Directory) != 0)
                    directories.Push(path);
                else if (relative != "backend.json" && !d.Files.ContainsKey(relative))
                    throw new InvalidDataException("共享后端包含未登记文件：" + relative);
            }
        }
        foreach (var (name, hash) in d.Files)
            if (!Hash(PathIn(root, name)).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("共享后端文件校验失败：" + name);
        return d;
    }
    public static void ValidateParameterHeader(BackendDescriptor descriptor,BackendDependency dependency,JsonElement parameters,string file)
    {
        if(descriptor.Support==null)return; // Historical backends retain their original validation path.
        var support=descriptor.Support.Parameters[dependency.Variant];
        var version=parameters.ValueKind==JsonValueKind.Object?parameters.EnumerateObject().Where(p=>p.Name.Equals("schemaVersion",StringComparison.OrdinalIgnoreCase)).ToArray():Array.Empty<JsonProperty>();
        if(version.Length!=1 || version[0].Value.ValueKind!=JsonValueKind.Number || !version[0].Value.TryGetInt32(out int actual) || actual!=support.Version)
            throw new OperationException("backend_unsupported","参数格式与固定后端声明不一致。","edit_parameters",new Dictionary<string,string>{{"file",file},{"field","$.schemaVersion"},{"format",support.Format},{"expected",support.Version.ToString()}});
    }
}
