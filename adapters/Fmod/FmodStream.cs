using System.Diagnostics;
using System.Runtime.InteropServices;
using ApexAudio.Contracts;

namespace ApexAudio.Fmod;

// Mapping ownership stays with the connection. Dispose before unmapping either buffer.
sealed class FmodStream : IAsyncDisposable
{
    const int Capacity = 8192, BlockSize = 8224;
    readonly IReadOnlyList<AudioGroup> groups;
    readonly nint tracks, source;
    readonly Func<bool> targetAlive;
    readonly CancellationTokenSource connection = new();
    readonly Task heartbeat;
    CancellationTokenSource? capture;
    Task? worker;
    long dropped,sequence;
    string detail = "FMOD 分组已连接";
    string[] selected = Array.Empty<string>();
    OperationError? failure;
    int disposed;
    public AdapterStatus Status
    {
        get
        {
            bool connected=targetAlive() && Marshal.ReadInt32(source,16)!=0 && !heartbeat.IsCompleted;
            return new(connected,connected && worker is { IsCompleted:false },detail,Interlocked.Read(ref dropped),failure,selected);
        }
    }
    public FmodStream(IReadOnlyList<AudioGroup> groups,nint tracks,nint source,Func<bool> targetAlive)
    {
        this.groups=groups;this.tracks=tracks;this.source=source;this.targetAlive=targetAlive;
        heartbeat=KeepAlive(connection.Token);
    }
    public HashSet<string> ValidateSelection(CaptureRequest request)
    {
        if(request.GroupIds==null || request.GroupIds.Length>groups.Count || request.GroupIds.Any(id=>!groups.Any(g=>g.Id==id && g.HasPcm)))
            throw new OperationException("group_invalid","请求包含未知或不可采集的声音分组。","refresh_groups");
        return request.GroupIds.ToHashSet();
    }
    public async Task StartCapture(CaptureRequest request,Func<AudioPacket,CancellationToken,Task> output,CancellationToken token)
    {
        var requested=ValidateSelection(request); // A rejected request leaves the previous stream running.
        if(!Status.Connected)throw new OperationException("capture_not_ready","FMOD 游戏连接已结束，请重新连接。","reconnect");
        await StopCapture();failure=null;selected=groups.Where(g=>requested.Contains(g.Id)).Select(g=>g.Id).ToArray();
        if(selected.Length==0)return;
        capture=CancellationTokenSource.CreateLinkedTokenSource(token,connection.Token);var ct=capture.Token;
        int position=Marshal.ReadInt32(tracks,12);
        worker=Task.Run(async ()=>
        {
            try
            {
                while(!ct.IsCancellationRequested)
                {
                    if(!Status.Connected)throw new OperationException("target_changed","游戏已退出或原生采集连接已结束。","reconnect");
                    int published=Marshal.ReadInt32(tracks,12);
                    if(published-position>Capacity){Interlocked.Add(ref dropped,published-position-Capacity);position=published-Capacity;}
                    while(position<published && !ct.IsCancellationRequested)
                    {
                        nint block=tracks+64+position%Capacity*BlockSize;int seq=Marshal.ReadInt32(block);if((seq&1)!=0)break;
                        int route=Marshal.ReadInt32(block,4),frames=Marshal.ReadInt32(block,12);long tick=Marshal.ReadInt64(block,24);
                        if(route<0 || route>=groups.Count || frames is <1 or >2048){Interlocked.Increment(ref dropped);position++;continue;}
                        byte[] pcm=new byte[frames*4];Marshal.Copy(block+32,pcm,0,pcm.Length);Thread.MemoryBarrier();
                        if(Marshal.ReadInt32(block)!=seq)break;
                        position++;if(!requested.Contains(groups[route].Id))continue;
                        // Complete each in-flight frame so stopping or reconnecting cannot split the wire frame.
                        await output(new((uint)route,48000,2,tick,sequence++,pcm),connection.Token);
                    }
                    await Task.Delay(4,ct);
                }
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){ }
            catch(Exception e)
            {
                failure=(e as OperationException)?.Error ?? new("capture_failed","FMOD 音轨传输失败："+e.Message,"reconnect");detail=failure.Message;
            }
        });
        detail="正在采集 FMOD 独立分组";
    }
    async Task KeepAlive(CancellationToken token)
    {
        try
        {
            while(!token.IsCancellationRequested)
            {
                if(!targetAlive())return;
                Marshal.WriteInt64(source,72,Stopwatch.GetTimestamp());await Task.Delay(100,token);
            }
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){ }
        catch(Exception e){failure=new("capture_failed","FMOD 连接心跳已停止："+e.Message,"reconnect");detail=failure.Message;}
    }
    public async Task StopCapture()
    {
        capture?.Cancel();if(worker!=null)await worker;worker=null;capture?.Dispose();capture=null;selected=Array.Empty<string>();
        if(failure==null)detail="FMOD 已连接 · 独立音轨传输已停止";
    }
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        try{await StopCapture();}
        finally{connection.Cancel();try{await heartbeat;}finally{connection.Dispose();}}
    }
}
