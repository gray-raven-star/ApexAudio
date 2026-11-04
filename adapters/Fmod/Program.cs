using System.Diagnostics;
using System.Runtime.InteropServices;
using ApexAudio.Contracts;
using ApexAudio.Windows;
using ApexAudio.Fmod;
using ApexAudio.Fmod.Definition;
using System.Text.Json;

if(args.Length==1 && args[0]=="--describe")Console.WriteLine(JsonSerializer.Serialize(FmodParameterContract.Support,Wire.Json));
else await AdapterServer.Run(args, ()=>new FmodAdapter(GameDefinition.Load(args)));

sealed class FmodAdapter : IGameAdapter, IRealtimeMixAdapter, IPreparedAdapter
{
    const int Capacity = 8192, BlockSize = 8224;
    readonly GameDefinition game;
    Target? target; nint stop, select, tracksMap, tracks, sourceMap, source;
    FmodStream? stream; string detail = "等待游戏连接";
    public AdapterInfo Info => new("fmod.profile", game.Name, Wire.Version, new(true, true, true, false, false, true));
    public IReadOnlyList<AudioGroup> Groups { get; }
    public BackendIdentity BackendIdentity => game.Identity ?? throw new IOException("当前使用 V1 包内配置。");
    public TargetValidation ValidateTarget(ConnectRequest request)
    {
        var result=game.ValidateTarget(request);if(!result.Allowed)return result;
        try
        {
            using var process=Process.GetProcessById(request.ProcessId);
            CaptureModuleGuard.Require(process,game.CaptureFile,game.CaptureSha256);return result;
        }
        catch(OperationException e){return new(false,e.Message,e.Error,result.Matches);}
        catch(ArgumentException){return new(false,"目标游戏已退出。",new("target_changed","目标游戏已退出。","choose_target"));}
    }
    public AdapterStatus Status => stream?.Status ?? new(false,false,detail);
    public FeedbackSource FeedbackSource => new("apex-f32-planar-ring-v1", $"Local\\ApexFmodMix-{target?.Process.Id ?? throw new IOException("尚未连接。")}", target.Process.Id);
    public FmodAdapter(GameDefinition definition)
    {
        game = definition;
        var ids = game.Buses.ToDictionary(b => b.Id, b => b.StableId ?? b.Id,StringComparer.OrdinalIgnoreCase);
        Groups = game.Buses.Select(b => new AudioGroup(ids[b.Id], b.Name, b.ParentId == null ? null : ids.GetValueOrDefault(b.ParentId), b.Category, true, b.ContainsChildren, "FMOD 总线路径")).ToArray();
    }
    public async Task Connect(ConnectRequest request, CancellationToken token)
    {
        await Disconnect();
        try
        {
            TargetValidation? validation=null;
            if (game.Context != null)
            {
                validation=ValidateTarget(request);
                if(!validation.Allowed){var error=validation.Failure;throw new OperationException(error?.Code ?? "target_rejected",validation.Detail,error?.NextAction ?? "check_target",error?.Details);}
                BackendFiles.Read(AppContext.BaseDirectory,game.Context.Backend);
            }
            var matchedBuild=validation?.Matches?.FirstOrDefault();
            var process = Process.GetProcessById(request.ProcessId); target = new(process, matchedBuild?.Executable ?? game.Executable, matchedBuild?.Sha256 ?? game.ExecutableSha256);
            foreach (var (name, hash) in matchedBuild?.Modules ?? game.Modules)
            {
                var module = process.Modules.Cast<ProcessModule>().SingleOrDefault(m => m.ModuleName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (module == null || !Native.Hash(module.FileName).Equals(hash,StringComparison.OrdinalIgnoreCase)) throw new OperationException("build_mismatch","游戏音频模块与准备结果不匹配："+name,"prepare_session");
            }
            var wrapper = process.Modules.Cast<ProcessModule>().Single(m => m.ModuleName.Equals(game.WrapperModule, StringComparison.OrdinalIgnoreCase));
            if (game.Locator == "wrapper" && game.WrapperRva >= wrapper.ModuleMemorySize - 8) throw new IOException("音频实例定位超出模块范围。");
            string dll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, game.CaptureFile));
            if (!dll.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)) throw new IOException("FMOD 采集模块路径无效。");
            if (!Native.Hash(dll).Equals(game.CaptureSha256,StringComparison.OrdinalIgnoreCase)) throw new IOException("FMOD 采集模块校验失败。");
            CaptureModuleGuard.Require(process,dll,game.CaptureSha256);
            nint address = target.Load(dll); nint stopAddress = target.Export(dll, address, "FmodStop"); select = target.Export(dll, address, "FmodSelect");
            uint result = target.CallData(target.Export(dll, address, "FmodConfigure"), game.Configuration());
            NativeCaptureError.RequireSuccess("configure",result);
            result = target.Call(target.Export(dll, address, "FmodStart"));
            NativeCaptureError.RequireSuccess("start",result);
            stop=stopAddress; // Own native cleanup only after this connection has started capture.
            tracksMap = Native.OpenFileMapping(6, false, $"Local\\ApexFmodTracks-{process.Id}"); tracks = Native.MapViewOfFile(tracksMap, 6, 0, 0, 64 + Capacity * BlockSize);
            sourceMap = Native.OpenFileMapping(6, false, $"Local\\ApexFmodMix-{process.Id}"); source = Native.MapViewOfFile(sourceMap, 6, 0, 0, 3150488);
            if (tracks == 0 || source == 0 || Marshal.ReadInt32(tracks) != 0x31544641 || Marshal.ReadInt32(tracks, 28) != BlockSize) throw new IOException("FMOD 音轨缓冲区格式不匹配。");
            stream=new(Groups,tracks,source,()=>target!=null && !target.Process.HasExited);
            detail = "FMOD 分组已连接";
        }
        catch(Exception primary)
        {
            try{await Disconnect();}catch(Exception cleanup){throw OperationException.WithCleanup(primary,cleanup);}throw;
        }
    }
    public void SetFeedbackGroups(CaptureRequest request)
    {
        if (target == null) throw new IOException("尚未连接。");
        var ids = stream?.ValidateSelection(request) ?? throw new OperationException("capture_not_ready","尚未连接。","connect");
        byte[] mask = new byte[64 * 4]; for (int i = 0; i < Groups.Count; i++) BitConverter.GetBytes(ids.Contains(Groups[i].Id) ? 1 : 0).CopyTo(mask, i * 4);
        if (target.CallData(select, mask) != 0) throw new IOException("反馈分组未应用。");
    }
    public Task StartCapture(CaptureRequest request, Func<AudioPacket, CancellationToken, Task> output, CancellationToken token)
        => (stream ?? throw new OperationException("capture_not_ready","尚未连接。","connect")).StartCapture(request,output,token);
    public Task StopCapture() => stream?.StopCapture() ?? Task.CompletedTask;
    public async Task Disconnect()
    {
        try { if(stream!=null)await stream.DisposeAsync(); }
        finally
        {
            stream=null;
            try { if (target != null && !target.Process.HasExited && stop != 0) target.Call(stop); }
            finally
            {
                if (tracks != 0) Native.UnmapViewOfFile(tracks); if (tracksMap != 0) Native.CloseHandle(tracksMap);
                if (source != 0) Native.UnmapViewOfFile(source); if (sourceMap != 0) Native.CloseHandle(sourceMap);
                tracks = tracksMap = source = sourceMap = stop = select = 0; target?.Dispose(); target?.Process.Dispose(); target = null;detail="FMOD 未连接";
            }
        }
    }
    public async ValueTask DisposeAsync() => await Disconnect();
}
