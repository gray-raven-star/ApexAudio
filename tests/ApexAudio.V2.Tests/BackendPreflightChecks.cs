using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using ApexAudio.Contracts;
using ApexAudio.Core;

static class BackendPreflightChecks
{
    // Deliberately bypass host preparation to verify the backend's independent pre-injection boundary.
    public static async Task Run(InstalledPack pack,string backend,string scratch,Action<bool,string> check)
    {
        var profile=pack.Manifest.Profiles[0];var target=TargetIdentity.Read(Environment.ProcessId);
        var build=pack.Manifest.Games[0] with{Executable=Path.GetFileName(target.Path),Sha256=PackageStore.Hash(target.Path)};
        using var parameters=JsonDocument.Parse(File.ReadAllText(PackageStore.SafePath(pack.Directory,profile.Parameters)));
        var context=new LaunchContext(2,pack.Manifest.Id,pack.Manifest.Version,pack.Reference.ManifestSha256,profile.Id,profile.Backend,
            parameters.RootElement.Clone(),build,target,new[]{build});
        string file=Path.Combine(scratch,"preflight-context.json"),key="ApexAudio-Preflight-"+Guid.NewGuid().ToString("N");
        File.WriteAllText(file,JsonSerializer.Serialize(context,Wire.Json));
        using var control=new NamedPipeServerStream(key+"-control",PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        using var audio=new NamedPipeServerStream(key+"-audio",PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        var descriptor=BackendFiles.Read(backend,profile.Backend);
        var start=new ProcessStartInfo(PackageStore.SafePath(backend,descriptor.EntryPoint)){WorkingDirectory=backend,UseShellExecute=false,CreateNoWindow=true};
        foreach(string argument in new[]{"--context",file,"--control",key+"-control","--audio",key+"-audio"})start.ArgumentList.Add(argument);
        using var timeout=new CancellationTokenSource(10000);
        var connected=Task.WhenAll(control.WaitForConnectionAsync(timeout.Token),audio.WaitForConnectionAsync(timeout.Token));
        using var process=Process.Start(start)!;
        try
        {
            await connected;long id=0;
            async Task<Response> Call(string method,object? value=null)
            {
                await Framing.WriteJson(control,new Request(Wire.Version,++id,method,Wire.Element(value)),timeout.Token);
                return await Framing.ReadJson<Response>(control,timeout.Token);
            }
            var validation=Wire.Read<TargetValidation>((await Call("ValidateTarget",new ConnectRequest(target.ProcessId,target.Path))).Result);
            check(!validation.Allowed && validation.Failure?.Code=="evidence_insufficient","backend independently rechecks frozen candidates and rejects unavailable modules over IPC");
            var changed=Wire.Read<TargetValidation>((await Call("ValidateTarget",new ConnectRequest(0,null))).Result);
            check(!changed.Allowed && changed.Failure?.Code=="target_changed","backend rejects a different target over IPC");
            var rejected=await Call("Connect",new ConnectRequest(target.ProcessId,target.Path));
            check(!rejected.Ok && rejected.Failure?.Code=="evidence_insufficient" && !Wire.Read<AdapterStatus>((await Call("GetStatus")).Result).Connected,
                "Connect cannot bypass independent checks or load capture code after rejection");
            await Call("Shutdown");await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}File.Delete(file);
        }
    }
}
