using System.Text.Json;
using System.Diagnostics;
using ApexAudio.Contracts;
using ApexAudio.Fmod.Definition;

namespace ApexAudio.Fmod;

sealed class GameDefinition : FmodParameters
{
    public string Executable { get; set; } = "";
    public string ExecutableSha256 { get; set; } = "";
    public Dictionary<string, string> Modules { get; set; } = new();
    public string CaptureFile { get; set; } = "";
    public string CaptureSha256 { get; set; } = "";
    public LaunchContext? Context { get; private set; }
    public BackendIdentity? Identity { get; private set; }
    GameMatch[] PreparedBuilds=>Context?.Candidates ?? (Context?.Build is {} build?new[]{build}:Array.Empty<GameMatch>());
    long? validatedStart;
    public static GameDefinition Load(string[] args)
    {
        int at = Array.IndexOf(args, "--context");
        if (at < 0) return JsonSerializer.Deserialize<GameDefinition>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "game.json")), Wire.Json)!;
        if (at + 1 == args.Length) throw new IOException("缺少启动上下文。");
        string path = Path.GetFullPath(args[at + 1]);
        var context = JsonSerializer.Deserialize<LaunchContext>(File.ReadAllText(path), Wire.Json) ?? throw new IOException("启动上下文为空。");
        var descriptor = BackendFiles.Read(AppContext.BaseDirectory, context.Backend);
        if (descriptor.ContextVersion != context.Version) throw new IOException("后端与启动上下文版本不匹配。");
        BackendFiles.ValidateParameterHeader(descriptor,context.Backend,context.Parameters,context.ParametersFile ?? "parameters");
        if(descriptor.Support is {} support && !FmodParameterContract.MatchesSupport(support.Parameters[context.Backend.Variant]))
            throw new OperationException("backend_unsupported","后端声明与此 FMOD 实现的参数支持范围不一致。","choose_backend_version");
        var game = FromContext(context);
        game.Identity = new(context.Backend.Id, context.Backend.Version, context.Backend.Variant, BackendFiles.Hash(path));
        game.CaptureFile = descriptor.Variants[context.Backend.Variant]; game.CaptureSha256 = descriptor.Files[game.CaptureFile];
        game.Validate(); return game;
    }
    internal static GameDefinition FromContext(LaunchContext context)
    {
        if (context.Version != 2 || context.Backend.Id != "apex.fmod" || context.Backend.Version != "0.3.0" || context.Backend.Variant != "studio-v1") throw new IOException("不受支持的共享后端身份或上下文格式。");
        FmodParameterContract.Require(context.Parameters,context.ParametersFile ?? "parameters");
        var game = context.Parameters.Deserialize<GameDefinition>(Wire.Json) ?? throw new IOException("采集参数为空。");
        if (game.SchemaVersion != 2) throw new IOException("不支持的 FMOD 参数格式。");
        game.Context = context;
        if (context.Target != null && game.PreparedBuilds.Length==0) throw new IOException("目标缺少选中的游戏构建。");
        if (game.PreparedBuilds.FirstOrDefault() is { } build)
        {
            if (game.PreparedBuilds.Any(b=>b.ProfileId!=context.ProfileId)) throw new IOException("游戏构建与 Profile 不一致。");
            game.Executable = build.Executable; game.ExecutableSha256 = build.Sha256 ?? "";
            game.Modules = build.Modules == null ? new() : new(build.Modules, StringComparer.OrdinalIgnoreCase);
        }
        game.Validate(); return game;
    }
    public void Validate()
    {
        var invalid=FmodParameterContract.CheckValues(this,Context?.ParametersFile ?? "game.json").FirstOrDefault();
        if(invalid!=null)throw new OperationException("backend_unsupported",invalid.File+" "+invalid.Path+"："+invalid.Message,"edit_parameters",new Dictionary<string,string>{{"file",invalid.File},{"field",invalid.Path}});
        var builds=Context?.Version==2?PreparedBuilds:new[]{new GameMatch(Executable,ExecutableSha256,Modules:Modules)};
        foreach(var build in builds)
        {
            var modules=build.Modules==null?new Dictionary<string,string>():new(build.Modules,StringComparer.OrdinalIgnoreCase);
            if(!System.Text.RegularExpressions.Regex.IsMatch(build.Sha256 ?? "", "^[0-9a-fA-F]{64}$") || string.IsNullOrWhiteSpace(build.Executable) || Path.GetFileName(build.Executable)!=build.Executable ||
                !modules.ContainsKey("fmod.dll") || !modules.ContainsKey("fmodstudio.dll") || !modules.ContainsKey(WrapperModule) || modules.Values.Any(h=>!System.Text.RegularExpressions.Regex.IsMatch(h,"^[0-9a-fA-F]{64}$")))
                throw new IOException("缺少关键模块和游戏构建的摘要。");
        }
    }
    public TargetValidation ValidateTarget(ConnectRequest request)
    {
        try
        {
            if (Context is { Version: 2 } context)
            {
                if (context.Target == null || PreparedBuilds.Length==0) throw new OperationException("capture_not_ready","此后端仅用于离线查询；连接游戏需要重新准备目标上下文。","prepare_session");
                context.Target.Validate(request);
                Validate();
                var evidence=TargetEvidence.Collect(context.Target,PreparedBuilds.SelectMany(b=>b.Modules?.Keys.AsEnumerable() ?? Array.Empty<string>()));
                var result=BuildMatcher.Match(PreparedBuilds,evidence);
                if(result.Failure is {} error)return new(false,error.Message,error,Array.Empty<GameMatch>());
                return new(true,"游戏构建和关键模块匹配。",Matches:result.Candidates);
            }
            Validate(); using var process = Process.GetProcessById(request.ProcessId);
            string path = process.MainModule?.FileName ?? throw new IOException("无法读取目标进程路径。");
            if (!Path.GetFileName(path).Equals(Executable, StringComparison.OrdinalIgnoreCase) ||
                request.GamePath != null && !Path.GetFullPath(request.GamePath).Equals(path, StringComparison.OrdinalIgnoreCase) ||
                !BackendFiles.Hash(path).Equals(ExecutableSha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("目标游戏构建不匹配。");
            foreach (var (name, hash) in Modules)
            {
                var module = process.Modules.Cast<ProcessModule>().SingleOrDefault(m => m.ModuleName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (module == null || !BackendFiles.Hash(module.FileName).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("关键音频模块不匹配：" + name);
            }
            long started = process.StartTime.ToUniversalTime().Ticks;
            if (validatedStart != null && validatedStart != started) throw new IOException("目标进程已更换，请重新连接。");
            validatedStart = started; return new(true, "游戏构建和关键模块匹配。");
        }
        catch (Exception e) { return new(false, e.Message,(e as OperationException)?.Error); }
    }
    public byte[] Configuration()
    {
        if (Buses.Count is < 1 or > 64 || Buses.Select(b => b.Id).Distinct().Count() != Buses.Count) throw new IOException("适配包总线配置无效。");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        foreach (uint value in new uint[] { 0x314d4641, 2, Locator == "studioHandle" ? 0 : WrapperRva, RuntimeVersion, (uint)Buses.Count, Locator == "studioHandle" ? StudioHandle : StateOffset, StudioOffset }) writer.Write(value);
        var module = System.Text.Encoding.Unicode.GetBytes(WrapperModule);
        if (module.Length > 254) throw new IOException("适配包模块名称过长。");
        writer.Write(module); writer.Write(new byte[256 - module.Length]);
        for (int i = 0; i < 64; i++) writer.Write(i < Buses.Count ? Convert.FromHexString(Buses[i].Id) : new byte[16]);
        return stream.ToArray();
    }
}
