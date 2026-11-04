using System.ComponentModel;
using System.Diagnostics;
using ApexAudio.Contracts;

namespace ApexAudio.Fmod;

// Loaded capture modules remain in the game after disconnect; never load a second revision.
static class CaptureModuleGuard
{
    internal static bool IsCapture(string name)=>name.StartsWith("ApexFmodCapture",StringComparison.OrdinalIgnoreCase) && name.EndsWith(".dll",StringComparison.OrdinalIgnoreCase);
    internal static OperationError? Check(string expectedName,string expectedHash,IEnumerable<ModuleEvidence> loaded)
    {
        foreach(var module in loaded.Where(m=>IsCapture(m.Name)))
        {
            if(module.Failure!=null || module.Sha256==null)
                return module.Failure ?? new("evidence_insufficient","无法核对已加载的 FMOD 采集模块："+module.Name,"retry_target_check");
            if(!module.Name.Equals(expectedName,StringComparison.OrdinalIgnoreCase) || !module.Sha256.Equals(expectedHash,StringComparison.OrdinalIgnoreCase))
                return new("restart_required","游戏已加载其他版本的 FMOD 采集 DLL，请重启游戏后再连接所选版本。","restart_game",
                    new Dictionary<string,string>{{"module",module.Name},{"expected",expectedName},{"actualSha256",module.Sha256},{"expectedSha256",expectedHash}});
        }
        return null;
    }
    internal static void Require(Process process,string expectedPath,string expectedHash)
    {
        try
        {
            process.Refresh();
            var loaded=process.Modules.Cast<ProcessModule>().Where(m=>IsCapture(m.ModuleName))
                .Select(m=>new ModuleEvidence(m.ModuleName,m.FileName,BackendFiles.Hash(m.FileName))).ToArray();
            if(Check(Path.GetFileName(expectedPath),expectedHash,loaded) is {} error)
                throw new OperationException(error.Code,error.Message,error.NextAction,error.Details);
        }
        catch(Exception e)when(e is IOException and not OperationException || e is UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {throw new OperationException("evidence_insufficient","无法检查游戏中已加载的采集 DLL："+e.Message,"retry_target_check",innerException:e);}
    }
}
