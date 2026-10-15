using System.Diagnostics;
using System.Runtime.InteropServices;
using ApexAudio.Contracts;
using ApexAudio.Wwise;
using ApexAudio.Windows;

await AdapterServer.Run(args, new WwiseAdapter(GameDefinition.Load()));

sealed class WwiseAdapter : IGameAdapter, IRealtimeMixAdapter
{
    const int Capacity = 8192, BlockSize = 8224, MapSize = 64 + Capacity * BlockSize;
    readonly GameDefinition game; readonly GameBus[] routes; readonly Dictionary<uint, uint> groupIndices;
    Target? target; nint stopAddress, recordMap, recordView, sourceMap, sourceView, gridMap, gridView;
    CancellationTokenSource? capture; Task? worker; long dropped; string detail = "等待游戏连接";
    public AdapterInfo Info => new("wwise.profile", game.Title, Wire.Version, new(true, true, true, false, false, true));
    public FeedbackSource FeedbackSource => new("apex-f32-planar-ring-v1", $"Local\\ApexGroupsPcm-{target?.Process.Id ?? throw new IOException("尚未连接。")}", target.Process.Id);
    public void SetFeedbackGroups(CaptureRequest request)
    {
        if (gridView == 0) throw new IOException("尚未连接。");
        var selected = request.GroupIds.ToHashSet();
        if (selected.Any(id => !Groups.Any(g => g.Id == id && g.HasPcm))) throw new IOException("实时混音包含无效分组。");
        for (int i = 0; i < routes.Length; i++) Marshal.WriteInt32(gridView, 40 + i * 32 + 8, selected.Contains(routes[i].Id.ToString()) ? 1 : 0);
    }
    public IReadOnlyList<AudioGroup> Groups { get; }
    public AdapterStatus Status => new(target != null && !target.Process.HasExited, worker is { IsCompleted: false }, detail, dropped);
    public WwiseAdapter(GameDefinition definition)
    {
        game = definition; routes = game.Buses.Where(b => b.Slot >= 0 && !b.Auxiliary).OrderBy(b => b.Id).ToArray();
        var ordinary = game.Buses.Where(b => !b.Auxiliary).ToArray(); var ids = ordinary.Select(b => b.Id).ToHashSet();
        Groups = ordinary.Select(b => new AudioGroup(b.Id.ToString(), string.IsNullOrWhiteSpace(b.Name) ? "分组 " + b.Id : b.Name,
            ids.Contains(b.Parent) ? b.Parent.ToString() : null, b.Category, b.Slot >= 0, ordinary.Any(c => c.Parent == b.Id), b.NameSource)).ToArray();
        groupIndices = ordinary.Select((b, i) => (b.Id, i)).ToDictionary(v => v.Id, v => (uint)v.i);
    }
    public async Task Connect(ConnectRequest request, CancellationToken token)
    {
        await Disconnect();
        try
        {
            var process = Process.GetProcessById(request.ProcessId);
            string name = Path.GetFileName(process.MainModule?.FileName) ?? throw new IOException("游戏程序路径不可读。");
            target = new(process, name, game.GameSha256);
            var module = process.Modules.Cast<ProcessModule>().SingleOrDefault(m => m.ModuleName.Equals(game.EngineModule, StringComparison.OrdinalIgnoreCase));
            if (module == null || Native.Hash(module.FileName) != game.EngineSha256) throw new IOException("游戏音频引擎与适配包版本不匹配。");
            string dll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, game.CaptureFile));
            if (!dll.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase) || Native.Hash(dll) != game.CaptureSha256) throw new IOException("采集模块校验失败。");
            nint address = target.Load(dll); stopAddress = target.Export(dll, address, "CaptureStop");
            uint code = target.CallData(target.Export(dll, address, "CaptureConfigure"), game.BuildConfiguration());
            if (code != 0) throw new IOException("采集配置未应用：" + code);
            code = target.Call(target.Export(dll, address, "CaptureStart"));
            if (code != 0 && code != 100) throw new IOException(code == 108 ? "另一份工具正在采集该游戏，请先断开旧工具。" : "采集接口未连接：" + code);
            if (target.Call(target.Export(dll, address, "CaptureRecordPrepare")) != 0) throw new IOException("独立音轨缓冲区无法建立。");
            recordMap = Native.OpenFileMapping(6, false, $"Local\\ApexGroupsRecord2-{process.Id}"); recordView = Native.MapViewOfFile(recordMap, 6, 0, 0, MapSize);
            sourceMap = Native.OpenFileMapping(6, false, $"Local\\ApexGroupsPcm-{process.Id}"); sourceView = Native.MapViewOfFile(sourceMap, 6, 0, 0, 3150488);
            gridMap = Native.OpenFileMapping(6, false, $"Local\\ApexGroups3-{process.Id}"); gridView = Native.MapViewOfFile(gridMap, 6, 0, 0, (nuint)(40 + routes.Length * 32));
            if (recordView == 0 || sourceView == 0 || Marshal.ReadInt32(recordView) != 0x32524741 || Marshal.ReadInt32(recordView, 24) != Capacity || Marshal.ReadInt32(recordView, 28) != BlockSize) throw new IOException("独立音轨缓冲区格式不匹配。");
            if (gridView == 0 || Marshal.ReadInt32(gridView) != 0x47534241 || Marshal.ReadInt32(gridView, 12) != routes.Length) throw new IOException("实时混音缓冲区格式不匹配。");
            detail = "采集接口已连接";
        }
        catch { await Disconnect(); throw; }
    }
    public async Task StartCapture(CaptureRequest request, Func<AudioPacket, CancellationToken, Task> output, CancellationToken token)
    {
        await StopCapture(); if (recordView == 0) throw new IOException("尚未连接。");
        capture = CancellationTokenSource.CreateLinkedTokenSource(token); var ct = capture.Token;
        Marshal.WriteInt32(recordView, 8, 0); Marshal.WriteInt32(recordView, 12, 0); Marshal.WriteInt32(recordView, 16, 0);
        Marshal.WriteInt32(recordView, 20, int.MaxValue); long origin = Stopwatch.GetTimestamp(); Marshal.WriteInt64(recordView, 32, origin);
        Thread.MemoryBarrier(); Marshal.WriteInt32(recordView, 8, 1);
        worker = Task.Run(async () =>
        {
            using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var lease = MaintainLease(leaseCancellation.Token);
            int position = 0; long sequence = 0;
            var progress = new RenderProgress(origin, Stopwatch.Frequency);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (target == null || target.Process.HasExited) { detail = "游戏已退出"; break; }
                    long now = Stopwatch.GetTimestamp();
                    int frame = Marshal.ReadInt32(recordView, 16);
                    origin += progress.Observe(frame, now);
                    detail = progress.Waiting ? "游戏声音已暂停 · 保持连接，等待恢复" : "正在采集独立分组";
                    if (Marshal.ReadInt32(sourceView, 16) == 0) { detail = "游戏采集接口已停止"; break; }
                    if (Marshal.ReadInt32(recordView, 8) == 3) { detail = "独立音轨采集已停止，正在等待重新连接"; break; }
                    int published = Marshal.ReadInt32(recordView, 12);
                    if (published - position > Capacity) { dropped += published - position - Capacity; position = published - Capacity; }
                    while (position < published)
                    {
                        nint block = recordView + 64 + position % Capacity * BlockSize; int seq = Marshal.ReadInt32(block); if ((seq & 1) != 0) break;
                        int route = Marshal.ReadInt32(block, 4), at = Marshal.ReadInt32(block, 8), count = Marshal.ReadInt32(block, 12);
                        if (route < 0 || route >= routes.Length || count is < 1 or > 2048 || at < 0) { dropped++; position++; continue; }
                        byte[] pcm = new byte[count * 4]; Marshal.Copy(block + 32, pcm, 0, pcm.Length); Thread.MemoryBarrier();
                        if (Marshal.ReadInt32(block) != seq) break;
                        position++; var bus = routes[route]; if (!request.GroupIds.Contains(bus.Id.ToString())) continue;
                        await output(new(groupIndices[bus.Id], 48000, 2, origin + (long)at * Stopwatch.Frequency / 48000, sequence++, pcm), ct);
                    }
                    await Task.Delay(4, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { detail = "采集停止：" + e.Message; }
            finally
            {
                leaseCancellation.Cancel(); await lease;
                if (recordView != 0) Marshal.WriteInt32(recordView, 8, 0);
            }
        }, ct);
        detail = "正在采集独立分组";
    }
    // Audio pipe backpressure must not expire the native capture lease.
    async Task MaintainLease(CancellationToken token)
    {
        try
        {
            do
            {
                long now = Stopwatch.GetTimestamp();
                Marshal.WriteInt64(recordView, 32, now); Marshal.WriteInt64(sourceView, 72, now);
                await Task.Delay(100, token);
            } while (!token.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public async Task StopCapture() { capture?.Cancel(); if (worker != null) await worker; worker = null; capture?.Dispose(); capture = null; }
    public async Task Disconnect()
    {
        await StopCapture();
        try { if (target != null && !target.Process.HasExited && stopAddress != 0) target.Call(stopAddress); }
        finally
        {
            if (recordView != 0) Native.UnmapViewOfFile(recordView); if (recordMap != 0) Native.CloseHandle(recordMap);
            if (sourceView != 0) Native.UnmapViewOfFile(sourceView); if (sourceMap != 0) Native.CloseHandle(sourceMap);
            if (gridView != 0) Native.UnmapViewOfFile(gridView); if (gridMap != 0) Native.CloseHandle(gridMap);
            recordView = recordMap = sourceView = sourceMap = gridView = gridMap = stopAddress = 0;
            target?.Dispose(); target?.Process.Dispose(); target = null; detail = "已断开";
        }
    }
    public async ValueTask DisposeAsync() => await Disconnect();
}
