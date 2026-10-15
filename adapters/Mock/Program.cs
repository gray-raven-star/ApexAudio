using ApexAudio.Contracts;
using System.Diagnostics;

await AdapterServer.Run(args, new MockAdapter());

sealed class MockAdapter : IGameAdapter, IRealtimeMixAdapter
{
    readonly RealtimeMix? mix = Environment.GetEnvironmentVariable("APEX_AUDIO_MOCK_REALTIME") == "1" ? new() : null;
    public AdapterInfo Info => new("demo.mock", "独立音轨演示", Wire.Version, new(true, true, true, false, false, mix != null));
    public FeedbackSource FeedbackSource => mix?.Source ?? throw new IOException("未开启实时混音测试。");
    public void SetFeedbackGroups(CaptureRequest request) => mix?.Select(request.GroupIds);
    public IReadOnlyList<AudioGroup> Groups { get; } = new AudioGroup[]
    {
        new("effects", "音效", null, "音效", false),
        new("impact", "低频冲击", "effects", "音效", true),
        new("steps", "脚步节奏", "effects", "音效", true),
        new("music", "旋律示例", null, "音乐", true),
        new("voice", "语音节奏示例", null, "语音", true)
    };
    CancellationTokenSource? capture; Task? worker; bool connected; string detail = "尚未连接";
    public AdapterStatus Status => new(connected, worker is { IsCompleted: false }, detail);
    public Task Connect(ConnectRequest request, CancellationToken token) { connected = true; detail = "演示已连接 · 不读取游戏"; return Task.CompletedTask; }
    public async Task StartCapture(CaptureRequest request, Func<AudioPacket, CancellationToken, Task> output, CancellationToken token)
    {
        if (!connected) throw new InvalidOperationException("请先连接。");
        await StopCapture(); capture = CancellationTokenSource.CreateLinkedTokenSource(token);
        var ct = capture.Token; long origin = Stopwatch.GetTimestamp();
        worker = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10)); long frame = 0, sequence = 0;
            try
            {
                while (await timer.WaitForNextTickAsync(ct))
                {
                    var packets = new List<(string Id, AudioPacket Packet)>();
                    for (int g = 0; g < Groups.Count; g++)
                    {
                        if (!Groups[g].HasPcm || !request.GroupIds.Contains(Groups[g].Id)) continue;
                        byte[] pcm = new byte[480 * 4];
                        for (int i = 0; i < 480; i++)
                        {
                            double t = (frame + i) / 48000.0;
                            double amplitude = g switch { 1 => Math.Exp(-(t % 2) * 12) * .65, 2 => Math.Exp(-(t % .55) * 45) * .25, 3 => .1, _ => t % 3 < 1.4 ? .16 * (.5 + .5 * Math.Sin(t * 13)) : 0 };
                            double frequency = g switch { 1 => 95, 2 => 340, 3 => 660 + 110 * Math.Floor(t % 4), _ => 180 + 70 * Math.Sin(t * 7) };
                            short v = (short)(Math.Sin(t * Math.Tau * frequency) * amplitude * 32767);
                            BitConverter.TryWriteBytes(pcm.AsSpan(i * 4), v); BitConverter.TryWriteBytes(pcm.AsSpan(i * 4 + 2), v);
                        }
                        packets.Add((Groups[g].Id, new((uint)g, 48000, 2, origin + frame * Stopwatch.Frequency / 48000, sequence++, pcm)));
                    }
                    mix?.Publish(packets);
                    foreach (var (_, packet) in packets) await output(packet, ct);
                    frame += 480;
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { detail = "音频连接已断开"; }
        }, ct);
        detail = "正在输出四条独立测试音轨";
    }
    public async Task StopCapture() { capture?.Cancel(); if (worker != null) await worker; worker = null; capture?.Dispose(); capture = null; }
    public async Task Disconnect() { await StopCapture(); connected = false; detail = "已断开"; }
    public async ValueTask DisposeAsync() { await Disconnect(); mix?.Dispose(); }
}
