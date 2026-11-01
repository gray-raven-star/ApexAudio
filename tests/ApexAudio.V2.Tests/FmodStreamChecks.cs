using System.Diagnostics;
using System.Runtime.InteropServices;
using ApexAudio.Contracts;
using ApexAudio.Fmod;

static class FmodStreamChecks
{
    // Synthetic producer buffers exercise transport and heartbeat, not game injection or FMOD DSP.
    public static async Task Run(Action<bool,string> check)
    {
        nint tracks=Marshal.AllocHGlobal(64+8224*8),source=Marshal.AllocHGlobal(104);
        try
        {
            Marshal.Copy(new byte[64+8224*8],0,tracks,64+8224*8);Marshal.Copy(new byte[104],0,source,104);Marshal.WriteInt32(source,16,1);
            bool alive=true;int published=0;
            AudioGroup[] groups=[new("a","A",null,"",true),new("b","B",null,"",true)];
            await using var stream=new FmodStream(groups,tracks,source,()=>alive);
            var written=new TaskCompletionSource<AudioPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
            await stream.StartCapture(new(["b"]),(packet,ct)=>{written.TrySetResult(packet);return Task.CompletedTask;},CancellationToken.None);
            try {await stream.StartCapture(new(["unknown"]),(_,_)=>Task.CompletedTask,CancellationToken.None);throw new Exception("Unknown group accepted");}
            catch(OperationException e)when(e.Error.Code=="group_invalid") {check(stream.Status.Capturing,"unknown group is rejected without stopping active PCM");}
            Publish(0);Publish(1);var packet=await written.Task.WaitAsync(TimeSpan.FromSeconds(3));
            check(packet.GroupIndex==1 && packet.Peak>0 && stream.Status.CaptureGroupIds!.SequenceEqual(new[]{"b"}),"PCM preserves full group-table indices and requested scope");
            await stream.StopCapture();long lease=Marshal.ReadInt64(source,72);await Task.Delay(250);
            check(stream.Status.Connected && !stream.Status.Capturing && stream.Status.Failure==null && Marshal.ReadInt64(source,72)>lease && Marshal.ReadInt32(source,16)==1,
                "stopping PCM keeps native connection active and heartbeats advancing");
            await stream.StartCapture(new(Array.Empty<string>()),(_,_)=>throw new Exception("Unexpected PCM"),CancellationToken.None);
            check(stream.Status.Connected && !stream.Status.Capturing && stream.Status.CaptureGroupIds!.Length==0,"empty PCM selection has connected noncapturing status and empty scope");
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var complete=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool outputCancelled=false;
            await stream.StartCapture(new(["a"]),async(_,ct)=>{entered.TrySetResult();try{await complete.Task.WaitAsync(ct);}catch(OperationCanceledException){outputCancelled=true;throw;}},CancellationToken.None);
            Publish(0);await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));var stopping=stream.StopCapture();await Task.Delay(50);
            bool stopWaited=!stopping.IsCompleted && !outputCancelled;complete.SetResult();await stopping;
            check(stopWaited,"PCM stop waits for an in-flight frame instead of truncating wire framing");
            var resumed=new TaskCompletionSource<AudioPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
            await stream.StartCapture(new(["a"]),(p,_)=>{resumed.TrySetResult(p);return Task.CompletedTask;},CancellationToken.None);
            Publish(0);var resumedPacket=await resumed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            check(resumedPacket.GroupIndex==0 && resumedPacket.Sequence>packet.Sequence,"PCM restarts on the same connection with monotonically increasing sequence");
            alive=false;
            using var timeout=new CancellationTokenSource(3000);
            while(stream.Status.Failure==null)await Task.Delay(10,timeout.Token);
            check(!stream.Status.Connected && stream.Status.Failure.Code=="target_changed","actual target loss is distinct from normal capture stop");
            await stream.DisposeAsync();alive=true;
            await using var closingStream=new FmodStream(groups,tracks,source,()=>alive);
            var closingWrite=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var closeFrame=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await closingStream.StartCapture(new(["a"]),async(_,ct)=>{closingWrite.TrySetResult();await closeFrame.Task.WaitAsync(ct);},CancellationToken.None);
            Publish(0);await closingWrite.Task.WaitAsync(TimeSpan.FromSeconds(3));var closing=closingStream.DisposeAsync().AsTask();await Task.Delay(50);
            bool disconnectWaited=!closing.IsCompleted;closeFrame.SetResult();await closing;
            check(disconnectWaited,"disconnect also finishes an in-flight frame before releasing mappings");
            void Publish(int route)
            {
                nint block=tracks+64+published*8224;Marshal.WriteInt32(block,1);Marshal.WriteInt32(block,4,route);Marshal.WriteInt32(block,12,1);
                Marshal.WriteInt64(block,24,Stopwatch.GetTimestamp());Marshal.WriteInt16(block,32,1000);Marshal.WriteInt16(block,34,-1000);
                Thread.MemoryBarrier();Marshal.WriteInt32(block,2);Marshal.WriteInt32(tracks,12,++published);
            }
        }
        finally {Marshal.FreeHGlobal(tracks);Marshal.FreeHGlobal(source);}
    }
}
