using ApexAudio.Contracts;

await AdapterServer.Run(args, new YourAdapter());

// This is a compileable interface skeleton, not a working game capture module.
sealed class YourAdapter : IGameAdapter
{
    public AdapterInfo Info => new("custom.adapter", "未实现采集", Wire.Version, new(false, false, false, false, false));
    public IReadOnlyList<AudioGroup> Groups => Array.Empty<AudioGroup>();
    public AdapterStatus Status => new(false, false, "需要实现真实游戏采集");
    public Task Connect(ConnectRequest request, CancellationToken token) => throw new NotSupportedException("Implement verified game discovery and capture initialization.");
    public Task StartCapture(CaptureRequest request, Func<AudioPacket, CancellationToken, Task> output, CancellationToken token) => throw new NotSupportedException("Emit independent PCM from the actual capture module.");
    public Task StopCapture() => Task.CompletedTask;
    public Task Disconnect() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
