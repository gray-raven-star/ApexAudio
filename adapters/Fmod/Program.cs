using System.Diagnostics;
using System.Runtime.InteropServices;
using ApexAudio.Contracts;
using ApexAudio.Windows;
using ApexAudio.Fmod;

await AdapterServer.Run(args, new FmodAdapter(GameDefinition.Load()));

sealed class FmodAdapter : IGameAdapter, IRealtimeMixAdapter
{
    const int Capacity = 8192, BlockSize = 8224;
    readonly GameDefinition game;
    Target? target; nint stop, select, tracksMap, tracks, sourceMap, source;
    CancellationTokenSource? capture; Task? worker; long dropped; string detail = "等待游戏连接";
    public AdapterInfo Info => new("fmod.profile", game.Name, Wire.Version, new(true, true, true, false, false, true));
    public IReadOnlyList<AudioGroup> Groups { get; }
    public AdapterStatus Status => new(target != null && !target.Process.HasExited, worker is { IsCompleted: false }, detail, dropped);
    public FeedbackSource FeedbackSource => new("apex-f32-planar-ring-v1", $"Local\\ApexFmodMix-{target?.Process.Id ?? throw new IOException("尚未连接。")}", target.Process.Id);
    public FmodAdapter(GameDefinition definition)
    {
        game = definition;
        Groups = game.Buses.Select(b => new AudioGroup(b.Id, b.Name, b.ParentId, b.Category, true, b.ContainsChildren, "FMOD 总线路径")).ToArray();
    }
    public async Task Connect(ConnectRequest request, CancellationToken token)
    {
        await Disconnect();
        try
        {
            var process = Process.GetProcessById(request.ProcessId); target = new(process, game.Executable, game.ExecutableSha256);
            foreach (var (name, hash) in game.Modules)
            {
                var module = process.Modules.Cast<ProcessModule>().SingleOrDefault(m => m.ModuleName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (module == null || Native.Hash(module.FileName) != hash) throw new IOException("游戏音频模块与适配包版本不匹配。");
            }
            var wrapper = process.Modules.Cast<ProcessModule>().Single(m => m.ModuleName.Equals(game.WrapperModule, StringComparison.OrdinalIgnoreCase));
            if (game.WrapperRva >= wrapper.ModuleMemorySize - 8) throw new IOException("音频实例定位超出模块范围。");
            string dll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, game.CaptureFile));
            if (!dll.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)) throw new IOException("FMOD 采集模块路径无效。");
            if (Native.Hash(dll) != game.CaptureSha256) throw new IOException("FMOD 采集模块校验失败。");
            nint address = target.Load(dll); stop = target.Export(dll, address, "FmodStop"); select = target.Export(dll, address, "FmodSelect");
            uint result = target.CallData(target.Export(dll, address, "FmodConfigure"), game.Configuration());
            if (result != 0) throw new IOException("FMOD 配置未应用：" + result);
            result = target.Call(target.Export(dll, address, "FmodStart"));
            if (result != 0) throw new IOException(result == 3 ? "游戏音频尚未初始化。" : "FMOD 采集未连接：" + result);
            tracksMap = Native.OpenFileMapping(6, false, $"Local\\ApexFmodTracks-{process.Id}"); tracks = Native.MapViewOfFile(tracksMap, 6, 0, 0, 64 + Capacity * BlockSize);
            sourceMap = Native.OpenFileMapping(6, false, $"Local\\ApexFmodMix-{process.Id}"); source = Native.MapViewOfFile(sourceMap, 6, 0, 0, 3150488);
            if (tracks == 0 || source == 0 || Marshal.ReadInt32(tracks) != 0x31544641 || Marshal.ReadInt32(tracks, 28) != BlockSize) throw new IOException("FMOD 音轨缓冲区格式不匹配。");
            detail = "FMOD 分组已连接";
        }
        catch { await Disconnect(); throw; }
    }
    public void SetFeedbackGroups(CaptureRequest request)
    {
        if (target == null) throw new IOException("尚未连接。");
        var ids = request.GroupIds.ToHashSet(); if (ids.Any(id => !Groups.Any(g => g.Id == id))) throw new IOException("无效的声音分组。");
        byte[] mask = new byte[64 * 4]; for (int i = 0; i < Groups.Count; i++) BitConverter.GetBytes(ids.Contains(Groups[i].Id) ? 1 : 0).CopyTo(mask, i * 4);
        if (target.CallData(select, mask) != 0) throw new IOException("反馈分组未应用。");
    }
    public async Task StartCapture(CaptureRequest request, Func<AudioPacket, CancellationToken, Task> output, CancellationToken token)
    {
        await StopCapture(); if (tracks == 0) throw new IOException("尚未连接。");
        capture = CancellationTokenSource.CreateLinkedTokenSource(token); var ct = capture.Token;
        var requested = request.GroupIds.ToHashSet(); int position = Marshal.ReadInt32(tracks, 12);
        worker = Task.Run(async () =>
        {
            using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var lease = KeepAlive(leaseCancellation.Token); long sequence = 0;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (target == null || target.Process.HasExited) { detail = "游戏已退出"; break; }
                    if (Marshal.ReadInt32(source, 16) == 0) { detail = "FMOD 采集已停止"; break; }
                    int published = Marshal.ReadInt32(tracks, 12);
                    if (published - position > Capacity) { dropped += published - position - Capacity; position = published - Capacity; }
                    while (position < published)
                    {
                        nint block = tracks + 64 + position % Capacity * BlockSize; int seq = Marshal.ReadInt32(block); if ((seq & 1) != 0) break;
                        int route = Marshal.ReadInt32(block, 4), frames = Marshal.ReadInt32(block, 12); long tick = Marshal.ReadInt64(block, 24);
                        if (route < 0 || route >= Groups.Count || frames is < 1 or > 2048) { dropped++; position++; continue; }
                        byte[] pcm = new byte[frames * 4]; Marshal.Copy(block + 32, pcm, 0, pcm.Length); Thread.MemoryBarrier();
                        if (Marshal.ReadInt32(block) != seq) break;
                        position++; if (!requested.Contains(Groups[route].Id)) continue;
                        await output(new((uint)route, 48000, 2, tick, sequence++, pcm), ct);
                    }
                    await Task.Delay(4, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception e) { detail = "FMOD 采集停止：" + e.Message; }
            finally { leaseCancellation.Cancel(); await lease; }
        }, ct);
        detail = "正在采集 FMOD 独立分组";
    }
    async Task KeepAlive(CancellationToken token)
    {
        try { while (!token.IsCancellationRequested) { Marshal.WriteInt64(source, 72, Stopwatch.GetTimestamp()); await Task.Delay(100, token); } }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public async Task StopCapture() { capture?.Cancel(); if (worker != null) await worker; worker = null; capture?.Dispose(); capture = null; }
    public async Task Disconnect()
    {
        await StopCapture();
        try { if (target != null && !target.Process.HasExited && stop != 0) target.Call(stop); }
        finally
        {
            if (tracks != 0) Native.UnmapViewOfFile(tracks); if (tracksMap != 0) Native.CloseHandle(tracksMap);
            if (source != 0) Native.UnmapViewOfFile(source); if (sourceMap != 0) Native.CloseHandle(sourceMap);
            tracks = tracksMap = source = sourceMap = stop = select = 0; target?.Dispose(); target?.Process.Dispose(); target = null;
        }
    }
    public async ValueTask DisposeAsync() => await Disconnect();
}
