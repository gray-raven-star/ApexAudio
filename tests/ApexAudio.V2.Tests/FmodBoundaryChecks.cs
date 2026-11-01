using System.Text.Json.Nodes;
using System.Text.Json;
using System.IO.Compression;
using ApexAudio.Contracts;
using ApexAudio.Core;
using ApexAudio.Fmod;

static class FmodBoundaryChecks
{
    public static void Run(InstalledPack example, string scratch, Action<bool, string> check, Action<Action, string> reject)
    {
        var profile = example.Manifest.Profiles[0];
        var parameters = JsonNode.Parse(File.ReadAllText(PackageStore.SafePath(example.Directory, profile.Parameters)))!.AsObject();
        var modules = parameters["modules"]?.Deserialize<Dictionary<string, string>>(Wire.Json)
            ?? example.Manifest.Games[0].Modules!;
        foreach (var name in new[] { "executable", "executableSha256", "modules", "captureFile", "captureSha256" }) parameters.Remove(name);
        parameters["schemaVersion"] = 2;
        var dependency = profile.Backend with { Version = "0.3.0" };
        string first = Path.Combine(scratch, "build-a", "Game.exe"), second = Path.Combine(scratch, "build-b", "Game.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!); Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        File.WriteAllText(first, "synthetic build A"); File.WriteAllText(second, "synthetic build B");
        var a = new GameMatch("Game.exe", PackageStore.Hash(first), profile.Id, modules);
        var b = a with { Sha256 = PackageStore.Hash(second), Modules = new Dictionary<string, string>(modules) { ["fmod.dll"] = new string('F',64) } };
        var manifest = new PackManifest { Profiles = [profile], Games = [a, b] };
        var pack = new InstalledPack(example.Directory, manifest);
        TargetEvidence Evidence(string path,GameMatch build)=>new(new(1,1,path),PackageStore.Hash(path),build.Modules!.Select(m=>new ModuleEvidence(m.Key,m.Key,m.Value)).ToArray());
        var ea=Evidence(first,a);var eb=Evidence(second,b);
        GameMatch Selected(InstalledPack installed,TargetEvidence evidence)
        {
            var result=BuildMatcher.Match(installed.Manifest.Games,evidence);
            if(result.Failure is {} e)throw new OperationException(e.Code,e.Message,e.NextAction,e.Details);
            return result.Candidates.Single();
        }
        check(BuildMatcher.Match(manifest.Games,ea).ProfileId==BuildMatcher.Match(manifest.Games,eb).ProfileId,"different EXE builds select the same Profile");
        LaunchContext Context(GameMatch? build) => new(2, example.Manifest.Id, "0.3.0", "test", profile.Id,
            dependency, Wire.Element(parameters), build);
        var ca = Context(Selected(pack,ea));
        var cb = Context(Selected(pack,eb));
        var ga = GameDefinition.FromContext(ca); var gb = GameDefinition.FromContext(cb);
        check(ga.ExecutableSha256 == a.Sha256 && gb.ExecutableSha256 == b.Sha256 && ga.Configuration().SequenceEqual(gb.Configuration()),
            "backend uses selected build evidence while preserving identical native capture parameters");
        string definition=Path.Combine(scratch,"roundtrip-definition"),archive=Path.Combine(scratch,"build-reuse.gpack");
        foreach(string file in Directory.EnumerateFiles(example.Directory,"*",SearchOption.AllDirectories))
        {
            string output=Path.Combine(definition,Path.GetRelativePath(example.Directory,file));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);File.Copy(file,output);
        }
        string parameterFile=PackageStore.SafePath(definition,profile.Parameters);
        File.WriteAllText(parameterFile,parameters.ToJsonString(Wire.Json));
        var document=JsonNode.Parse(File.ReadAllText(Path.Combine(definition,"manifest.json")))!;
        document["version"]="0.3.0";document["minHostVersion"]="0.3.0";
        document["profiles"]=JsonSerializer.SerializeToNode(new[]{profile with{Backend=dependency}},Wire.Json);
        document["games"]=JsonSerializer.SerializeToNode(new[]{a,b},Wire.Json);
        document["files"]![profile.Parameters]=PackageStore.Hash(parameterFile);
        File.WriteAllText(Path.Combine(definition,"manifest.json"),document.ToJsonString(Wire.Json));
        ZipFile.CreateFromDirectory(definition,archive);
        var imported=new PackageStore(Path.Combine(scratch,"roundtrip")).Install(archive);
        var importedA=Selected(imported,ea);var importedB=Selected(imported,eb);
        check(importedA.ProfileId==importedB.ProfileId && importedA.Modules!["fmod.dll"]==a.Modules!["fmod.dll"] &&
            importedB.Modules!["fmod.dll"]==b.Modules!["fmod.dll"], "packaging and import preserve separate build evidence referencing one Profile");
        check(BuildMatcher.Match(manifest.Games,ea with{ExeSha256=new string('0',64)}).Failure?.Code=="build_mismatch","unknown EXE build rejected");
        manifest.Games.Add(a with { ProfileId = "other" });
        check(BuildMatcher.Match(manifest.Games,ea).Failure?.Code=="profile_conflict","conflicting build records rejected"); manifest.Games.RemoveAt(2);
        reject(() => GameDefinition.FromContext(ca with { Build = a with { Modules = null } }), "omitting required FMOD modules cannot bypass backend checks");
        reject(() => GameDefinition.FromContext(ca with { Build = a with { ProfileId = "other" } }), "build and Profile mismatch rejected");
        parameters["executableSha256"] = a.Sha256;
        reject(() => GameDefinition.FromContext(ca with { Parameters = Wire.Element(parameters) }), "build-specific digest forbidden inside new Profile parameters");
        parameters.Remove("executableSha256");
        reject(() => GameDefinition.FromContext(ca with { Version = 99 }), "unknown startup context version rejected");
        parameters["schemaVersion"] = 99;
        reject(() => GameDefinition.FromContext(ca with { Parameters = Wire.Element(parameters) }), "unknown FMOD parameter format rejected");
        parameters["schemaVersion"] = 2;
        var identity = TargetIdentity.Read(Environment.ProcessId);
        var request = new ConnectRequest(Environment.ProcessId, identity.Path);
        identity.Validate(request);
        reject(() => identity.Validate(request with { ProcessId = 0 }), "different requested PID rejected");
        reject(() => identity.Validate(request with { GamePath = first }), "different requested process path rejected");
        reject(() => (identity with { StartedUtcTicks = identity.StartedUtcTicks + 1 }).Validate(request), "stale process start identity rejected");
        var offline = GameDefinition.FromContext(Context(null));
        check(!offline.ValidateTarget(request).Allowed, "offline metadata launch cannot connect to a target");
        var current = a with { Executable = Path.GetFileName(identity.Path), Sha256 = PackageStore.Hash(identity.Path) };
        var actual = GameDefinition.FromContext(Context(current) with { Target = identity });
        var validation = actual.ValidateTarget(request);
        check(!validation.Allowed && validation.Failure?.Code=="evidence_insufficient", "matching process still fails required-module checks before injection");
        var stale = GameDefinition.FromContext(Context(current) with { Target = identity with { StartedUtcTicks = identity.StartedUtcTicks + 1 } });
        check(!stale.ValidateTarget(request).Allowed, "FMOD backend independently rejects stale target identity");
    }
}
