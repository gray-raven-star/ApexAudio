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
    Process? process; Task? receive; long id; int disposing;
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
    public async Task Launch(InstalledPack pack)
    {
        PackageStore.Validate(pack.Directory, pack.Manifest);
        var start = new ProcessStartInfo(PackageStore.SafePath(pack.Directory, pack.Manifest.EntryPoint)) { WorkingDirectory = pack.Directory, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--control"); start.ArgumentList.Add(controlName);
        start.ArgumentList.Add("--audio"); start.ArgumentList.Add(audioName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(10000);
        var c = control.WaitForConnectionAsync(timeout.Token); var a = audio.WaitForConnectionAsync(timeout.Token);
        process = Process.Start(start) ?? throw new IOException("适配程序未能启动。");
        try { await Task.WhenAll(c, a); Info = await Call<AdapterInfo>("GetInfo"); if (Info.ProtocolVersion != Wire.Version) throw new IOException("适配程序协议版本不匹配。"); receive = Task.Run(Receive); }
        catch { await DisposeAsync(); throw; }
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
                if (!response.Ok) throw new AdapterOperationException(response.Error ?? "适配操作失败。");
                return Wire.Read<T>(response.Result);
            }
            catch (OperationCanceledException) { lifetime.Cancel(); throw new IOException("适配程序响应超时，连接已停止。"); }
        }
        finally { commands.Release(); }
    }
    public async Task Connect(int pid, string? gamePath)
    {
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
        lifetime.Cancel(); control.Dispose(); audio.Dispose();
        if (receive != null) { try { await receive; } catch (Exception) { } }
        if (process != null)
        {
            if (!process.HasExited) { using var timeout = new CancellationTokenSource(1500); try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); } }
            process.Dispose(); process = null;
        }
    }
}
public sealed class AdapterOperationException(string message) : IOException(message);
