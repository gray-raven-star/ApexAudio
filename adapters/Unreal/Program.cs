using System.Diagnostics;
using System.Runtime.InteropServices;
using ApexAudio.Contracts;
using ApexAudio.Windows;
using ApexAudio.Unreal;

await AdapterServer.Run(args, new UnrealAdapter(GameDefinition.Load()));

sealed class UnrealAdapter : IGameAdapter, IRealtimeMixAdapter
{
    const int Capacity = 8192, BlockSize = 8224;
    readonly GameDefinition game;
    Target? target; nint stop, select, tracksMap, tracks, sourceMap, source;
    CancellationTokenSource? capture; Task? worker; long dropped; string detail = "等待游戏连接";
    public AdapterInfo Info => new("unreal.profile", game.Name, Wire.Version, new(true, true, true, false, false, true));
    public IReadOnlyList<AudioGroup> Groups { get; private set; }
    public AdapterStatus Status => new(target != null && !target.Process.HasExited, worker is { IsCompleted: false }, detail, dropped);
    public FeedbackSource FeedbackSource => new("apex-f32-planar-ring-v1", $"Local\\ApexUnrealMix-{target?.Process.Id ?? throw new IOException("尚未连接。")}", target.Process.Id);
    public UnrealAdapter(GameDefinition definition)
    {
        game = definition;
        Groups = game.Buses.Select(b => new AudioGroup(b.Id, b.Name, b.ParentId, b.Category, true, b.ContainsChildren, "虚幻 Submix 名称")).ToArray();
    }
    public async Task Connect(ConnectRequest request, CancellationToken token)
    {
        await Disconnect();
        try
        {
            var process = Process.GetProcessById(request.ProcessId); target = new(process, game.Executable, game.ExecutableSha256);
            string dll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, game.CaptureFile));
            if (!dll.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)) throw new IOException("虚幻 Submix 采集模块路径无效。");
            if (Native.Hash(dll) != game.CaptureSha256) throw new IOException("虚幻 Submix 采集模块校验失败。");
            nint address = target.Load(dll); stop = target.Export(dll, address, "UnrealCaptureStop"); select = target.Export(dll, address, "UnrealCaptureSelect");
            uint result = target.CallData(target.Export(dll, address, "UnrealCaptureStart"), game.Configuration());
            if (result != 0) throw new IOException(result == 3 ? "游戏音频尚未初始化。" : "虚幻 Submix 采集未连接：" + result);
            tracksMap = Native.OpenFileMapping(6, false, $"Local\\ApexUnrealTracks-{process.Id}"); tracks = Native.MapViewOfFile(tracksMap, 6, 0, 0, 64 + Capacity * BlockSize);
            sourceMap = Native.OpenFileMapping(6, false, $"Local\\ApexUnrealMix-{process.Id}"); source = Native.MapViewOfFile(sourceMap, 6, 0, 0, 3150488);
            if (tracks == 0 || source == 0 || Marshal.ReadInt32(tracks) != 0x31544641 || Marshal.ReadInt32(tracks, 28) != BlockSize) throw new IOException("虚幻 Submix 音轨缓冲区格式不匹配。");
            nint bindingsMap=Native.OpenFileMapping(4,false,$"Local\\ApexUnrealBindings-{process.Id}"), bindings=Native.MapViewOfFile(bindingsMap,4,0,0,64*8);
            try {
                if(bindings==0)throw new IOException("Submix 分组连接状态无法读取。");
                int[] parents=Enumerable.Range(0,game.Buses.Count).Select(i=>Marshal.ReadInt32(bindings,256+i*4)-1).ToArray();
                if(parents.Any(p=>p>=game.Buses.Count))throw new IOException("Submix 父组信息无效。");
                Groups = game.Buses.Select((b,i) => new AudioGroup(b.Id,b.Name,parents[i]>=0?game.Buses[parents[i]].Id:null,b.Category,Marshal.ReadInt32(bindings,i*4)!=0,parents.Contains(i),"虚幻 Submix 名称")).ToArray();
            }finally {if(bindings!=0)Native.UnmapViewOfFile(bindings);if(bindingsMap!=0)Native.CloseHandle(bindingsMap);}
            detail = "虚幻 Submix 分组已连接";
        }
        catch { await Disconnect(); throw; }
    }
    public void SetFeedbackGroups(CaptureRequest request)
    {
        if (target == null) throw new IOException("尚未连接。");
        var ids = request.GroupIds.ToHashSet(); if (ids.Any(id => !Groups.Any(g => g.Id == id&&g.HasPcm))) throw new IOException("无效的声音分组。");
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
                    if (Marshal.ReadInt32(source, 16) == 0) { detail = "虚幻 Submix 采集已停止"; break; }
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
            catch (Exception e) { detail = "虚幻 Submix 采集停止：" + e.Message; }
            finally { leaseCancellation.Cancel(); await lease; }
        }, ct);
        detail = "正在采集 虚幻 Submix 独立分组";
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
