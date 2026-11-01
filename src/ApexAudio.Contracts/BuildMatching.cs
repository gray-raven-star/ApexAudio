using System.ComponentModel;
using System.Diagnostics;

namespace ApexAudio.Contracts;

public sealed record ModuleEvidence(string Name,string? Path,string? Sha256,OperationError? Failure=null);
public sealed record TargetEvidence(TargetIdentity Target,string? ExeSha256,ModuleEvidence[] Modules,OperationError? Failure=null)
{
    public static TargetEvidence Collect(TargetIdentity target,IEnumerable<string> moduleNames)
    {
        target.Validate(new(target.ProcessId,target.Path));
        string? hash=null;OperationError? failure=null;
        try{hash=BackendFiles.Hash(target.Path);}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException)
        {failure=new("evidence_insufficient","无法读取目标 EXE："+e.Message,"check_permissions",new Dictionary<string,string>{{"file",target.Path}});}
        using var process=OpenTarget(target.ProcessId);
        ProcessModule[] loaded=Array.Empty<ProcessModule>();string? moduleError=null;
        try{loaded=process.Modules.Cast<ProcessModule>().ToArray();}
        catch(Exception e)when(e is Win32Exception or InvalidOperationException){moduleError=e.Message;}
        var modules=new List<ModuleEvidence>();
        foreach(string name in moduleNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string? path=null,digest=null;OperationError? issue=null;
            try
            {
                if(moduleError!=null)throw new IOException("无法枚举已加载模块："+moduleError);
                var matches=loaded.Where(m=>m.ModuleName.Equals(name,StringComparison.OrdinalIgnoreCase)).ToArray();
                if(matches.Length==0)throw new IOException("关键模块尚未加载："+name);
                if(matches.Length!=1)throw new IOException("已加载模块名称不唯一："+name);
                path=matches[0].FileName;digest=BackendFiles.Hash(path);
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
            {issue=new("evidence_insufficient",e.Message,"retry_target_check",new Dictionary<string,string>{{"module",name}});}
            modules.Add(new(name,path,digest,issue));
        }
        target.Validate(new(target.ProcessId,target.Path));
        return new(target,hash,modules.ToArray(),failure);
    }
    static Process OpenTarget(int pid)
    {
        try{return Process.GetProcessById(pid);}
        catch(ArgumentException e){throw new OperationException("target_changed","目标进程已退出。","choose_target",innerException:e);}
    }
}
public sealed record BuildEvaluation(GameMatch Build,OperationError? Failure);
public sealed record BuildMatchResult(string? ProfileId,GameMatch[] Candidates,TargetEvidence Evidence,
    BuildEvaluation[] Records,OperationError? Failure)
{
    public bool Matched=>Failure==null;
}
public static class BuildMatcher
{
    // Compare each whole record. A known mismatch cannot be repaired with evidence from another record.
    public static BuildMatchResult Match(IEnumerable<GameMatch> records,TargetEvidence evidence)
    {
        var evaluated=records.Select(build=>new BuildEvaluation(build,Compare(build,evidence))).ToArray();
        var matched=evaluated.Where(r=>r.Failure==null).Select(r=>r.Build).ToArray();
        var profiles=matched.Select(b=>b.ProfileId).Distinct().ToArray();
        OperationError? error=null;
        if(profiles.Length>1)error=new("profile_conflict","多条完整构建记录匹配了不同 Profile，请修正配置或明确选择。","choose_profile");
        else if(profiles.Length==1)
        {
            error=evaluated.FirstOrDefault(r=>r.Failure?.Code=="evidence_insufficient" && r.Build.ProfileId!=profiles[0])?.Failure;
        }
        else error=evaluated.FirstOrDefault(r=>r.Failure?.Code=="evidence_insufficient")?.Failure
            ?? evaluated.FirstOrDefault()?.Failure ?? new("build_mismatch","没有此目标的构建记录。","choose_package_version");
        return new(error==null?profiles[0]:null,matched,evidence,evaluated,error);
    }
    static OperationError? Compare(GameMatch build,TargetEvidence evidence)
    {
        if(!build.Executable.Equals(Path.GetFileName(evidence.Target.Path),StringComparison.OrdinalIgnoreCase))
            return Mismatch("EXE 文件名不匹配。",build.Executable,Path.GetFileName(evidence.Target.Path));
        if(evidence.ExeSha256!=null && !string.Equals(build.Sha256,evidence.ExeSha256,StringComparison.OrdinalIgnoreCase))
            return Mismatch("EXE 构建摘要不匹配。",build.Sha256 ?? "",evidence.ExeSha256);
        OperationError? missing=evidence.Failure ?? (evidence.ExeSha256==null?new("evidence_insufficient","无法取得 EXE 摘要。","retry_target_check"):null);
        foreach(var(name,expected)in build.Modules ?? new Dictionary<string,string>())
        {
            var actual=evidence.Modules.Where(m=>m.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(actual.Length!=1 || actual[0].Failure!=null || actual[0].Sha256==null)
            {missing ??= actual.Length==1?actual[0].Failure:null;missing ??=new("evidence_insufficient","关键模块证据不可用："+name,"retry_target_check");continue;}
            if(!expected.Equals(actual[0].Sha256,StringComparison.OrdinalIgnoreCase))return Mismatch("关键模块摘要不匹配："+name,expected,actual[0].Sha256!);
        }
        return missing;
    }
    static OperationError Mismatch(string message,string expected,string actual)=>new("build_mismatch",message,"choose_package_version",
        new Dictionary<string,string>{{"expected",expected},{"actual",actual}});
}
