using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;

namespace ApexAudio.Contracts;

public static class Framing
{
    public static async Task WriteJson<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json);
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("控制消息过大。");
        byte[] size = BitConverter.GetBytes(bytes.Length);
        await stream.WriteAsync(size, token);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }
    public static async Task<T> ReadJson<T>(Stream stream, CancellationToken token)
    {
        byte[] size = new byte[4]; await stream.ReadExactlyAsync(size, token);
        int length = BitConverter.ToInt32(size);
        if (length < 2 || length > 4 * 1024 * 1024) throw new InvalidDataException("控制消息长度无效。");
        byte[] data = new byte[length]; await stream.ReadExactlyAsync(data, token);
        return JsonSerializer.Deserialize<T>(data, Wire.Json) ?? throw new InvalidDataException("控制消息无效。");
    }
    // 40-byte little-endian header; PCM16 interleaved, timestamps are Windows QPC ticks.
    public static async Task WriteAudio(Stream stream, AudioPacket packet, CancellationToken token)
    {
        byte[] header = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 0x31504141);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), packet.GroupIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), packet.SampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), packet.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), packet.Frames);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), 1);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(24), packet.Tick);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(32), packet.Sequence);
        await stream.WriteAsync(header, token); await stream.WriteAsync(packet.Pcm, token);
    }
    public static async Task<AudioPacket> ReadAudio(Stream stream, CancellationToken token)
    {
        byte[] h = new byte[40]; await stream.ReadExactlyAsync(h, token);
        uint group = BitConverter.ToUInt32(h, 4), rate = BitConverter.ToUInt32(h, 8), channels = BitConverter.ToUInt32(h, 12);
        int frames = BitConverter.ToInt32(h, 16);
        if (BitConverter.ToUInt32(h) != 0x31504141 || BitConverter.ToUInt32(h, 20) != 1 || rate != 48000 || channels != 2 || frames < 1 || frames > 2048)
            throw new InvalidDataException("音频协议不匹配；V1 使用 48 kHz 双声道 PCM16。");
        byte[] pcm = new byte[frames * 4]; await stream.ReadExactlyAsync(pcm, token);
        return new(group, rate, channels, BitConverter.ToInt64(h, 24), BitConverter.ToInt64(h, 32), pcm);
    }
}

public interface IGameAdapter : IAsyncDisposable
{
    AdapterInfo Info { get; }
    IReadOnlyList<AudioGroup> Groups { get; }
    AdapterStatus Status { get; }
    Task Connect(ConnectRequest request, CancellationToken token);
    Task StartCapture(CaptureRequest request, Func<AudioPacket, CancellationToken, Task> output, CancellationToken token);
    Task StopCapture();
    Task Disconnect();
}

// Optional: the adapter mixes the selected groups at capture time.
// Independent PCM remains available for recordings and previews.
public interface IRealtimeMixAdapter
{
    FeedbackSource FeedbackSource { get; }
    void SetFeedbackGroups(CaptureRequest request);
}

public static class AdapterServer
{
    public static Task Run(string[] args, IGameAdapter adapter)=>Run(args,null,adapter);
    public static Task Run(string[] args, Func<IGameAdapter> create)=>Run(args,create,null);
    static async Task Run(string[] args,Func<IGameAdapter>? create,IGameAdapter? adapter)
    {
        string Arg(string name) { int at = Array.IndexOf(args, name); return at >= 0 && at + 1 < args.Length ? args[at + 1] : throw new ArgumentException(name); }
        using var lifetime = new CancellationTokenSource();
        using var control = new NamedPipeClientStream(".", Arg("--control"), PipeDirection.InOut, PipeOptions.Asynchronous);
        using var audio = new NamedPipeClientStream(".", Arg("--audio"), PipeDirection.Out, PipeOptions.Asynchronous);
        try
        {
            await control.ConnectAsync(10000, lifetime.Token); await audio.ConnectAsync(10000, lifetime.Token);
            OperationError? initialization=null;
            if(adapter==null)
            {
                try{adapter=create!();}
                catch(Exception e){initialization=(e as OperationException)?.Error ?? new("backend_start_failed",e.Message,"check_backend");}
            }
            while (!lifetime.IsCancellationRequested)
            {
                Request request = await Framing.ReadJson<Request>(control, lifetime.Token);
                Response response;
                try
                {
                    if (request.Version != Wire.Version) throw new InvalidDataException("IPC 协议版本不匹配。");
                    if(adapter==null && request.Method!="Shutdown")throw new OperationException(initialization!.Code,initialization.Message,initialization.NextAction,initialization.Details);
                    object? result;
                    if(adapter==null)result=new AdapterStatus(false,false,"后端未初始化，已结束。",Failure:initialization);
                    else switch (request.Method)
                    {
                        case "GetInfo": result = adapter.Info; break;
                        case "GetBackendInfo": result = (adapter as IPreparedAdapter)?.BackendIdentity ?? throw new InvalidDataException("当前适配器不提供共享后端身份。"); break;
                        case "ValidateTarget": result = (adapter as IPreparedAdapter)?.ValidateTarget(Wire.Read<ConnectRequest>(request.Params)) ?? throw new InvalidDataException("当前适配器不提供连接前检查。"); break;
                        case "GetCapabilities": result = adapter.Info.Capabilities; break;
                        case "GetAudioGroups": result = adapter.Groups; break;
                        case "GetStatus": result = adapter.Status; break;
                        case "GetFeedbackSource": result = (adapter as IRealtimeMixAdapter)?.FeedbackSource ?? throw new InvalidDataException("适配包未提供实时混音。"); break;
                        case "SetFeedbackGroups":
                            if (adapter is not IRealtimeMixAdapter realtime) throw new InvalidDataException("适配包未提供实时混音。");
                            realtime.SetFeedbackGroups(Wire.Read<CaptureRequest>(request.Params)); result = adapter.Status; break;
                        case "Connect": await adapter.Connect(Wire.Read<ConnectRequest>(request.Params), lifetime.Token); result = adapter.Status; break;
                        case "StartCapture": await adapter.StartCapture(Wire.Read<CaptureRequest>(request.Params), (p, ct) => Framing.WriteAudio(audio, p, ct), lifetime.Token); result = adapter.Status; break;
                        case "StopCapture": await adapter.StopCapture(); result = adapter.Status; break;
                        case "Disconnect": await adapter.Disconnect(); result = adapter.Status; break;
                        case "Shutdown": await adapter.Disconnect(); result = adapter.Status; break;
                        default: throw new InvalidDataException("不支持的请求：" + request.Method);
                    }
                    response = new(Wire.Version, request.Id, true, Wire.Element(result));
                }
                catch (Exception e)
                {
                    var failure=(e as OperationException)?.Error;
                    response = new(Wire.Version, request.Id, false, Wire.Element(null), e.Message, failure?.Code ?? "adapter_error",failure);
                }
                await Framing.WriteJson(control, response, lifetime.Token);
                if (request.Method == "Shutdown") break;
            }
        }
        catch (EndOfStreamException) { }
        catch (IOException) { }
        finally { lifetime.Cancel(); if(adapter!=null)await adapter.DisposeAsync(); }
    }
}
