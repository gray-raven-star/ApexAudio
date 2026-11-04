using System.Diagnostics;
using System.Runtime.InteropServices;
using ApexAudio.Contracts;
using ApexAudio.Core;
using ApexAudio.Fmod;

static class CaptureConflictChecks
{
    public static async Task Run(InstalledPack pack,string backend,string scratch,Action<bool,string> check)
    {
        OperationError Error(string stage,uint result){try{NativeCaptureError.RequireSuccess(stage,result);}catch(OperationException e){return e.Error;}throw new Exception("Expected native rejection");}
        var notReady=Error("start",3);
        check(notReady.Code=="capture_not_ready" && notReady.NextAction=="wait_game_audio" && notReady.Details?["nativeResult"]=="3" && notReady.Details?["stage"]=="start",
            "unavailable native audio instance retains structured retry action and original result");
        check(Error("configure",100).Code=="capture_busy" && Error("start",108).NextAction=="close_other_session","native busy results are distinguishable from configuration or initialization failures");
        check(Error("start",1007).Details?["nativeResult"]=="1007","other native failures preserve their exact result for author feedback");
        const string name="ApexFmodCapture-current.dll";string hash=new('A',64);
        check(CaptureModuleGuard.Check(name,hash,new[]{new ModuleEvidence(name,null,hash)})==null,"same capture revision may be reused after disconnect");
        check(CaptureModuleGuard.Check(name,hash,new[]{new ModuleEvidence(name,null,new('B',64))})?.Code=="restart_required","same filename with different capture content requires game restart");
        check(CaptureModuleGuard.Check(name,hash,new[]{new ModuleEvidence("ApexFmodCapture-old.dll",null,hash)})?.NextAction=="restart_game","different capture filename cannot load alongside the selected revision");
        check(CaptureModuleGuard.Check(name,hash,new[]{new ModuleEvidence(name,null,null)})?.Code=="evidence_insufficient","unreadable loaded capture is not treated as a compatible revision");
        var descriptor=BackendFiles.Read(backend,pack.Manifest.Profiles[0].Backend);
        string native=BackendFiles.PathIn(backend,descriptor.Variants[pack.Manifest.Profiles[0].Backend.Variant]);
        string directory=Path.Combine(scratch,"capture-conflict");Directory.CreateDirectory(directory);
        var handles=new List<nint>();
        try
        {
            // DllMain only disables thread notifications. Do not call any capture exports.
            // Other copies supply synthetic module evidence, not real FMOD interfaces or DSP coverage.
            foreach(string module in pack.Manifest.Games[0].Modules!.Keys.Append("ApexFmodCapture-old.dll").Distinct(StringComparer.OrdinalIgnoreCase))
            {string file=Path.Combine(directory,module);File.Copy(native,file);handles.Add(NativeLibrary.Load(file));}
            using var process=Process.GetCurrentProcess();
            CaptureModuleGuard.Require(process,Path.Combine(directory,"ApexFmodCapture-old.dll"),BackendFiles.Hash(native));
            bool rejected=false;try{CaptureModuleGuard.Require(process,native,BackendFiles.Hash(native));}
            catch(OperationException e)when(e.Error.Code=="restart_required"){rejected=true;}
            check(rejected,"actual loaded capture modules are detected through the Windows process module list");
            await BackendPreflightChecks.Run(pack,backend,scratch,check,captureConflict:true);
        }
        finally{foreach(nint handle in handles.AsEnumerable().Reverse())NativeLibrary.Free(handle);}
    }
}
