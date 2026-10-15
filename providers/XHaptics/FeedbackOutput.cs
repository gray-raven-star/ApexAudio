using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ApexAudio.Contracts;
using ApexAudio.Windows;

namespace ApexAudio.XHaptics;

public sealed class FeedbackOutput : IAsyncDisposable
{
    const int SourceSize = 3150488, StreamSize = 1050136, BlockSize = 16408;
    readonly object gate = new(); readonly Dictionary<long, float[]> pending = new();
    readonly MemoryMappedFile map; readonly MemoryMappedViewAccessor view;
    readonly CancellationTokenSource lifetime = new(); readonly Task worker;
    readonly long origin = Stopwatch.GetTimestamp();
    long nextBlock, delayFrames = 960;
    Target? target; nint statusMap, statusView, stopAddress; long writes; bool previousInput = true;
    public int Port { get; set; } = 18789;
    public EngineApi Engine { get; }
    public float Gain { get; set; } = 1;
    public bool Enabled => statusView != 0;
    public bool EngineAlive => target != null && !target.Process.HasExited;
    public string Detail { get; private set; } = "反馈已关闭";
    public bool Active { get { lock (gate) return statusView != 0 && Marshal.ReadInt32(statusView, 16) != 0; } }
    public bool Delivered { get { lock (gate) return statusView != 0 && Marshal.ReadInt32(statusView, 16) != 0 && Marshal.ReadInt32(statusView, 144) > 0 && Stopwatch.GetTimestamp() - Marshal.ReadInt64(statusView, 96) < Stopwatch.Frequency; } }
    public bool HasSignal { get { lock (gate) return statusView != 0 && Marshal.PtrToStructure<float>(statusView + 140) > .00003f; } }
    public FeedbackOutput()
    {
        Engine = new(() => Port);
        map = MemoryMappedFile.CreateNew($"Local\\ApexGroupsInput-{Environment.ProcessId}", SourceSize);
        view = map.CreateViewAccessor(); view.Write(0, 0x37504341u); view.Write(4, 1u); view.Write(8, Environment.ProcessId);
        view.Write(16, 1); view.Write(56, Stopwatch.Frequency); view.Write(64, origin); view.Write(72, origin); view.Write(96, 48000);
        worker = Task.Run(Pump);
    }
    public void Accept(AudioPacket packet)
    {
        lock (gate)
        {
            long first = (long)Math.Round((packet.Tick - origin) * 48000.0 / Stopwatch.Frequency) + delayFrames;
            long currentFrame = Math.Max(nextBlock * 480, (long)((Stopwatch.GetTimestamp() - origin) * 48000.0 / Stopwatch.Frequency));
            if (first < nextBlock * 480 || first > currentFrame + 2880)
            {
                // Source timestamps can precede arrival (IPC latency or a drifting
                // game render clock). Move the whole mix, rather than losing the
                // packet or shifting different groups onto different time bases.
                // Recover in both directions. A single slow packet must not
                // leave every subsequent sound delayed by the same amount.
                long shift = (currentFrame / 480 + 2) - (long)Math.Floor(first / 480.0);
                var queued = pending.ToArray(); pending.Clear();
                foreach (var entry in queued) if (entry.Key + shift >= nextBlock) pending[entry.Key + shift] = entry.Value;
                delayFrames += shift * 480; first += shift * 480;
            }
            for (int i = 0; i < packet.Frames; i++)
            {
                long frame = first + i; if (frame < 0) continue; long block = frame / 480; int slot = (int)(frame % 480);
                if (!pending.TryGetValue(block, out var buffer)) { if (pending.Count > 200) pending.Clear(); buffer = new float[960]; pending[block] = buffer; }
                buffer[slot] += BitConverter.ToInt16(packet.Pcm, i * 4) / 32768f;
                buffer[480 + slot] += BitConverter.ToInt16(packet.Pcm, i * 4 + 2) / 32768f;
            }
        }
    }
    public void Clear() { lock (gate) { pending.Clear(); delayFrames = 960; } }
    async Task Pump()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                long now = Stopwatch.GetTimestamp(); view.Write(72, now);
                long due = (long)((now - origin) * 100.0 / Stopwatch.Frequency);
                lock (gate)
                {
                    // After a long scheduling stall, resume current feedback.
                    // Replaying a large backlog would make actions feel delayed.
                    if (due - nextBlock > 8)
                    {
                        nextBlock = due - 3;
                        foreach (long key in pending.Keys.Where(k => k < nextBlock).ToArray()) pending.Remove(key);
                    }
                    // Windows may wake a 10 ms timer only every 15–16 ms.
                    // Publish every intervening block, not just the latest one.
                    while (nextBlock < due)
                    {
                        float[] data = pending.Remove(nextBlock++, out var p) ? p : new float[960];
                        long address = 104 + (writes % 64) * BlockSize; int seq = (int)((writes + 1) * 2);
                        view.Write(address, seq - 1); view.Write(address + 4, 480); view.Write(address + 8, 2);
                        // The bridge's freshness check concerns delivery, not the
                        // game's earlier capture timestamp. Recording keeps that timestamp.
                        view.Write(address + 16, now);
                        view.WriteArray(address + 24, data, 0, 480); view.WriteArray(address + 24 + 2048 * 4, data, 480, 480);
                        Thread.MemoryBarrier(); view.Write(address, seq); view.Write(80, (int)++writes);
                    }
                    if (statusView != 0) { Marshal.WriteInt64(statusView, 104, now); Marshal.WriteInt32(statusView, 136, BitConverter.SingleToInt32Bits(Gain)); }
                }
            }
        }
        catch (OperationCanceledException) { }
    }
    public async Task Start(FeedbackSource? source = null)
    {
        if (Enabled) return;
        if (source != null && (source.Format != "apex-f32-planar-ring-v1" || source.ProducerProcessId <= 0 ||
            !source.MappingName.StartsWith("Local\\", StringComparison.Ordinal) || source.MappingName.Length > 127 || source.MappingName.Contains('\0')))
            throw new IOException("适配包的实时混音格式不支持。");
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "native", "xh-support.json")));
        string dll = Path.Combine(AppContext.BaseDirectory, "native", "ApexGroupsBridge-v5.dll");
        if (Native.Hash(dll) != config.RootElement.GetProperty("bridgeSha256").GetString()) throw new IOException("反馈桥接文件校验失败。");
        var supports = EngineApi.Supported;
        var processes = supports.SelectMany(s => Process.GetProcessesByName(s.ProcessName)).ToArray();
        if (processes.Length != 1) { foreach (var p in processes) p.Dispose(); throw new IOException("请先打开 X-Haptics；需要且只能运行一份。"); }
        var process = processes[0];
        var support = supports.SingleOrDefault(s => s.ProcessName.Equals(process.ProcessName,StringComparison.OrdinalIgnoreCase) && s.Sha256.Equals(Native.Hash(process.MainModule!.FileName),StringComparison.OrdinalIgnoreCase))
            ?? throw new IOException("X-Haptics 版本尚未适配。");
        Engine.Bind(support,process.Id);
        nint existing = Native.OpenFileMapping(4, false, $"Local\\ApexGroupsXhV4-{process.Id}");
        if (existing != 0)
        {
            nint ptr = Native.MapViewOfFile(existing, 4, 0, 0, 152);
            bool occupied = ptr != 0 && Marshal.ReadInt32(ptr, 16) != 0;
            if (ptr != 0) Native.UnmapViewOfFile(ptr); Native.CloseHandle(existing);
            if (occupied) { process.Dispose(); throw new IOException("另一份工具正在转送声音，请先关闭它的手柄反馈。"); }
        }
        previousInput = await Engine.InputEnabled();
        try
        {
            target = new(process, Path.GetFileName(process.MainModule!.FileName), support.Sha256);
            await Engine.SetInput(false);
            nint address = target.Load(dll); stopAddress = target.Export(dll, address, "BridgeStop");
            uint code;
            if (source == null) code = target.Call(target.Export(dll, address, "BridgeStart"), ((ulong)(uint)Environment.ProcessId << 32) | (uint)Environment.ProcessId);
            else
            {
                byte[] configuration = new byte[4 + 128 * 2];
                BitConverter.GetBytes(source.ProducerProcessId).CopyTo(configuration, 0);
                Encoding.Unicode.GetBytes(source.MappingName).CopyTo(configuration, 4);
                code = target.CallData(target.Export(dll, address, "BridgeStartSource"), configuration);
            }
            if (code != 0 && code != 100) throw new IOException("反馈桥接未连接：" + code);
            statusMap = Native.OpenFileMapping(6, false, $"Local\\ApexGroupsXhV4-{process.Id}"); statusView = Native.MapViewOfFile(statusMap, 6, 0, 0, 152);
            if (statusView == 0) throw new IOException("反馈状态不可用。");
            await Engine.SetInput(true); Detail = "已连接 · 等待声音";
        }
        catch { await Stop(); throw; }
    }
    public async Task Stop()
    {
        nint ptr; lock (gate) { ptr = statusView; statusView = 0; pending.Clear(); delayFrames = 960; }
        try { if (target != null && !target.Process.HasExited && stopAddress != 0) { await Engine.SetInput(false); await Task.Run(() => target.Call(stopAddress)); } }
        finally
        {
            if (ptr != 0) Native.UnmapViewOfFile(ptr); if (statusMap != 0) Native.CloseHandle(statusMap); statusMap = 0;
            if (target != null) { bool restore = !target.Process.HasExited; target.Dispose(); target.Process.Dispose(); target = null; if (restore) try { await Engine.SetInput(previousInput); } catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException) { } }
            stopAddress = 0; Detail = "反馈已关闭";
        }
    }
    public async ValueTask DisposeAsync() { lifetime.Cancel(); await worker; await Stop(); view.Dispose(); map.Dispose(); Engine.Dispose(); lifetime.Dispose(); }
}
