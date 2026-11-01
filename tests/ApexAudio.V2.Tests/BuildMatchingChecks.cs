using System.Diagnostics;
using ApexAudio.Contracts;
using ApexAudio.Core;

static class BuildMatchingChecks
{
    public static void Run(Action<bool,string> check)
    {
        var target=TargetIdentity.Read(Environment.ProcessId);
        using var process=Process.GetCurrentProcess();
        var known=process.Modules.Cast<ProcessModule>().Where(m=>m.ModuleName.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        var evidence=TargetEvidence.Collect(target,known.Select(m=>m.ModuleName).Append("apex-nonexistent-test-module.dll"));
        check(evidence.Modules.Take(2).All(m=>m.Path!=null && m.Sha256==PackageStore.Hash(m.Path) && m.Failure==null) &&
            evidence.Modules.Last().Failure?.Code=="evidence_insufficient","evidence uses actual loaded-module paths and distinguishes an unloaded module");
        var hashes=evidence.Modules.Take(2).ToDictionary(m=>m.Name,m=>m.Sha256!);
        var a=new GameMatch(Path.GetFileName(target.Path),evidence.ExeSha256,"shared",new(){{known[0].ModuleName,hashes[known[0].ModuleName]}});
        var b=a with{Modules=new(a.Modules!){{known[1].ModuleName,hashes[known[1].ModuleName]}}};
        var result=BuildMatcher.Match(new[]{a,b},evidence);
        check(result.Matched && result.ProfileId=="shared" && result.Candidates.Length==2,"same-Profile matches retain every whole candidate record");
        var wrong=a with{Modules=new(){{known[0].ModuleName,new string('0',64)}}};
        check(BuildMatcher.Match(new[]{a,wrong},evidence).Candidates.SequenceEqual(new[]{a}),"same EXE with different modules is resolved by complete evidence");
        var halfA=b with{Modules=new(b.Modules!){[known[0].ModuleName]=new string('0',64)}};
        var halfB=b with{Modules=new(b.Modules!){[known[1].ModuleName]=new string('0',64)}};
        check(BuildMatcher.Match(new[]{halfA,halfB},evidence).Failure?.Code=="build_mismatch","matching halves from separate records cannot be combined");
        check(BuildMatcher.Match(new[]{a,a with{ProfileId="other"}},evidence).Failure?.Code=="profile_conflict","different fully matched Profiles require explicit resolution");
        var unread=a with{ProfileId="other",Modules=new(){{"apex-nonexistent-test-module.dll",new string('F',64)}}};
        check(BuildMatcher.Match(new[]{a,unread},evidence).Failure?.Code=="evidence_insufficient","unreadable evidence cannot silently rule out a different Profile");
        var changed=evidence with{Modules=evidence.Modules.Select(m=>m.Name==known[1].ModuleName?m with{Sha256=new string('0',64)}:m).ToArray()};
        var rechecked=BuildMatcher.Match(result.Candidates,changed);
        check(rechecked.Matched && rechecked.Candidates.SequenceEqual(new[]{a}) && result.Candidates.Length==2,"rechecking preserves the original candidate set while reporting surviving records");
        check(BuildMatcher.Match(new[]{halfA},evidence with{ExeSha256=null,Failure=new("evidence_insufficient","unreadable","retry")}).Failure?.Code=="build_mismatch",
            "a known module mismatch rejects the record even if other evidence is unavailable");
    }
}
