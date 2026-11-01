using System.Diagnostics;
using System.IO.Pipes;
using ApexAudio.Contracts;

namespace ApexAudio.Core;

public sealed class AdapterClient : IAsyncDisposable
{
    readonly NamedPipeServerStream control, audio;
    readonly SemaphoreSlim commands = new(1);
    readonly CancellationTokenSource lifetime = new();
    readonly string controlName, audioName;
    Process? process; Task? receive; long id; int disposing,launched;
    string? contextFile; BackendIdentity? backendIdentity;
    IDisposable? packageUse;
    public PreparedSession? Session { get; private set; }
    public IReadOnlyList<AudioGroup> Groups { get; private set; } = Array.Empty<AudioGroup>();
    public AdapterInfo? Info { get; private set; }
    public event Action<AudioPacket>? Audio;
    public event Action<string>? Failed;
    public bool IsAlive => process is { HasExited: false } && !lifetime.IsCancellationRequested && Volatile.Read(ref disposing) == 0;
    public int ProcessId => process?.Id ?? 0;
    public AdapterClient()
    {
        string key = "ApexAudio-" + Guid.NewGuid().ToString("N");
        controlName = key + "-control"; audioName = key + "-audio";
        control = new(controlName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        audio = new(audioName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }
    public Task Launch(InstalledPack pack, string? gamePath = null, int? processId = null)
        => Launch(()=>PreparedSession.Prepare(pack,gamePath,processId));
    public Task Launch(PreparedSession session)=>Launch(()=>session);
    async Task Launch(Func<PreparedSession> prepare)
    {
        if(Interlocked.Exchange(ref launched,1)!=0 || Volatile.Read(ref disposing)!=0)
            throw new InvalidOperationException("一个采集客户端只能启动一个会话，请建立新的客户端。");
        try
        {
            var session=prepare();packageUse=session.TakeUse();Session=session;
            var pack=session.Pack;string programRoot=session.ProgramRoot,executable=session.Executable;
            if(!PackageStore.Hash(Path.Combine(pack.Directory,"manifest.json")).Equals(session.Package.ManifestSha256,StringComparison.OrdinalIgnoreCase))
                throw new OperationException("package_invalid","准备后的适配包清单已改变。","choose_package_version");
            PackageStore.Validate(pack.Directory,PackageStore.ReadManifest(Path.Combine(pack.Directory,"manifest.json")));
            if(session.Context is {} context)
            {
                BackendFiles.Read(programRoot,context.Backend);
                context.Target?.Validate(new(context.Target.ProcessId,context.Target.Path));
                string sessions=Path.Combine(pack.DataRoot!,"sessions");Directory.CreateDirectory(sessions);
                contextFile=Path.Combine(sessions,session.Id.ToString("N")+".json");
                File.WriteAllText(contextFile,System.Text.Json.JsonSerializer.Serialize(context,Wire.Json));
                backendIdentity=new(context.Backend.Id,context.Backend.Version,context.Backend.Variant,PackageStore.Hash(contextFile));
            }
            var start = new ProcessStartInfo(PackageStore.SafePath(programRoot, executable)) { WorkingDirectory = programRoot, UseShellExecute = false, CreateNoWindow = true };
            if (contextFile != null) { start.ArgumentList.Add("--context"); start.ArgumentList.Add(contextFile); }
            start.ArgumentList.Add("--control"); start.ArgumentList.Add(controlName);
            start.ArgumentList.Add("--audio"); start.ArgumentList.Add(audioName);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(10000);
            var c = control.WaitForConnectionAsync(timeout.Token); var a = audio.WaitForConnectionAsync(timeout.Token);
            process = Process.Start(start) ?? throw new IOException("适配程序未能启动。");
            await Task.WhenAll(c, a); Info = await Call<AdapterInfo>("GetInfo"); if (Info.ProtocolVersion != Wire.Version) throw new IOException("适配程序协议版本不匹配。"); if (backendIdentity != null && await Call<BackendIdentity>("GetBackendInfo") != backendIdentity) throw new IOException("实际后端身份与启动配置不匹配。"); receive = Task.Run(Receive);
        }
        catch(Exception primary)
        {
            try{await DisposeAsync();}catch(Exception cleanup){throw OperationException.WithCleanup(primary,cleanup);}throw;
        }
    }
    public async Task<T> Call<T>(string method, object? parameters = null, int timeoutMs = 8000)
    {
        await commands.WaitAsync(lifetime.Token);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(timeoutMs);
            var request = new Request(Wire.Version, ++id, method, Wire.Element(parameters));
            try
            {
                await Framing.WriteJson(control, request, timeout.Token);
                var response = await Framing.ReadJson<Response>(control, timeout.Token);
                if (response.Id != request.Id || response.Version != Wire.Version) throw new InvalidDataException("适配程序响应不匹配。");
                if (!response.Ok) throw new AdapterOperationException(response.Error ?? "适配操作失败。",response.Code,response.Failure);
                return Wire.Read<T>(response.Result);
            }
            catch (OperationCanceledException) { lifetime.Cancel(); throw new IOException("适配程序响应超时，连接已停止。"); }
        }
        finally { commands.Release(); }
    }
    public async Task Connect(int pid, string? gamePath)
    {
        Session?.Match?.Evidence.Target.Validate(new(pid,gamePath));
        if (backendIdentity != null)
        {
            var validation = await Call<TargetValidation>("ValidateTarget", new ConnectRequest(pid, gamePath));
            if (!validation.Allowed) throw new AdapterOperationException(validation.Detail,"target_rejected",validation.Failure);
        }
        await Call<AdapterStatus>("Connect", new ConnectRequest(pid, gamePath), 15000);
        Groups = await Call<List<AudioGroup>>("GetAudioGroups");
        if (Groups.Count > 2000 || Groups.Select(g => g.Id).Distinct().Count() != Groups.Count) throw new InvalidDataException("声音分组列表无效。");
        var map = Groups.ToDictionary(g => g.Id);
        foreach (var g in Groups)
        {
            var seen = new HashSet<string> { g.Id }; string? parent = g.ParentId;
            while (parent != null) { if (!map.TryGetValue(parent, out var p) || !seen.Add(parent)) throw new InvalidDataException("分组层级无效。"); parent = p.ParentId; }
        }
        if (Info?.Capabilities.IndependentPcm == true) await Call<AdapterStatus>("StartCapture", new CaptureRequest(Groups.Where(g => g.HasPcm).Select(g => g.Id).ToArray()));
    }
    async Task Receive()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var packet = await Framing.ReadAudio(audio, lifetime.Token);
                if (packet.GroupIndex >= Groups.Count || !Groups[(int)packet.GroupIndex].HasPcm) throw new InvalidDataException("适配器输出了未登记音轨。");
                Audio?.Invoke(packet);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (Volatile.Read(ref disposing) != 0 || lifetime.IsCancellationRequested) return;
            lifetime.Cancel(); Failed?.Invoke(e is EndOfStreamException ? "采集进程已结束，连接已断开。" : "采集连接已断开：" + e.Message);
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposing, 1) != 0) return;
        if (control.IsConnected && !lifetime.IsCancellationRequested)
        { try { await Call<AdapterStatus>("Shutdown", timeoutMs: 1500); } catch (Exception) { } }
        try
        {
            lifetime.Cancel(); control.Dispose(); audio.Dispose();
            if (receive != null) { try { await receive; } catch (Exception) { } }
            if (process != null)
            {
                if (!process.HasExited) { using var timeout = new CancellationTokenSource(1500); try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); } }
                process.Dispose(); process = null;
            }
        }
        finally
        {
            if(process==null || process.HasExited)
            {
                process?.Dispose();process=null;
                try {if(contextFile!=null)File.Delete(contextFile);}
                catch(IOException e){throw new OperationException("cleanup_incomplete","后端已结束，但启动上下文未能删除："+e.Message,"retry_cleanup");}
                finally {contextFile=null;packageUse?.Dispose();packageUse=null;}
            }
            else
            {
                // Keep the version pinned until the still-running process really exits.
                var remaining=process;var use=packageUse;string? file=contextFile;
                process=null;packageUse=null;contextFile=null;
                _=ReleaseAfterExit(remaining,use,file);
            }
        }
    }
    static async Task ReleaseAfterExit(Process process,IDisposable? use,string? context)
    {
        try {await process.WaitForExitAsync();}
        finally
        {
            if(process.HasExited)
            {
                try {if(context!=null)File.Delete(context);}catch(IOException) { }
                finally {use?.Dispose();process.Dispose();}
            }
        }
    }
}
public sealed class AdapterOperationException(string message,string? code=null,OperationError? failure=null)
    : OperationException(failure?.Code ?? code ?? "adapter_error",failure?.Message ?? message,failure?.NextAction ?? "review_adapter_error",failure?.Details);
