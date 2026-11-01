using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApexAudio.Contracts;
using ApexAudio.Core;

string root=Path.GetFullPath(args[0]), packages=Path.Combine(root,"tests/fixtures/v2"), scratch=Path.Combine(root,"artifacts/v2-test-"+Guid.NewGuid().ToString("N"));
int packagesAt=Array.IndexOf(args,"--packages");
if(packagesAt>=0) packages=Path.GetFullPath(args[packagesAt+1]);
Directory.CreateDirectory(scratch);
void Check(bool yes,string text) { if(!yes)throw new Exception(text); Console.WriteLine("PASS: "+text); }
void Reject(Action work,string text) { try {work();}catch(Exception e) when(e is IOException or InvalidDataException){Check(true,text);return;} throw new Exception("Unexpected acceptance: "+text); }
var store=new PackageStore(scratch);var backends=new BackendStore(scratch);
var packs=Directory.GetFiles(packages,"*.gpack").Select(store.Install).ToArray();
Check(packs.Length==2 && packs.All(p=>p.Manifest.SchemaVersion==2),"two executable-free V2 configuration packages imported");
var dependency=packs[0].Manifest.Profiles[0].Backend;
FmodBoundaryChecks.Run(packs[0],scratch,Check,Reject);
await FmodStreamChecks.Run(Check);
BuildMatchingChecks.Run(Check);
Reject(()=>backends.Resolve(dependency),"missing shared backend remains an explicit dependency error");
string backendArchive=Directory.GetFiles(packages,"*.gbackend").Single();
string shared=backends.Install(backendArchive);
Check(shared==backends.Resolve(packs[1].Manifest.Profiles[0].Backend),"two games resolve to exactly one immutable backend installation");
Check(backends.Install(backendArchive)==shared,"reimporting identical backend does not create another copy");
Reject(()=>BackendFiles.PathIn(shared,"../other.dll"),"backend traversal rejected");
Reject(()=>backends.Resolve(dependency with {DescriptorSha256=new string('0',64)}),"wrong backend descriptor digest rejected");
foreach(var pack in packs)
{
    var groupsBefore=new PresetStore(scratch).Authors(pack).SelectMany(p=>p.Groups.Keys).Distinct().ToHashSet();
    await using var client=new AdapterClient();await client.Launch(pack);
    var info=await client.Call<BackendIdentity>("GetBackendInfo");
    var groups=await client.Call<List<AudioGroup>>("GetAudioGroups");
    Check(info.Id==dependency.Id && info.Version==dependency.Version,"shared executable identity handshake: "+pack.Manifest.Id);
    Check(groups.Count>0 && groups.Select(g=>g.Id).Distinct().Count()==groups.Count && groupsBefore.All(id=>groups.Any(g=>g.Id==id)),"stable group IDs match presets: "+pack.Manifest.Id);
    var result=await client.Call<TargetValidation>("ValidateTarget",new ConnectRequest(Environment.ProcessId,Environment.ProcessPath));
    Check(!result.Allowed,"wrong target rejected before native module loading: "+pack.Manifest.Id);
    if(dependency.Version=="0.3.0")
    {
        bool rejected=false;
        try{await client.Call<AdapterStatus>("StartCapture",new CaptureRequest(new[]{"unknown"}));}
        catch(AdapterOperationException e)when(e.Error.Code=="capture_not_ready" && e.Error.NextAction=="connect"){rejected=true;}
        Check(rejected,"structured backend operation errors survive IPC with an actionable code");
        var stopped=await client.Call<AdapterStatus>("StopCapture");
        Check(!stopped.Connected && !stopped.Capturing && stopped.Failure==null,"stopping an offline backend is idempotent and control remains responsive");
    }
    string config=PackageStore.SafePath(pack.Directory,pack.Manifest.Profiles[0].Parameters),before=File.ReadAllText(config);
    File.AppendAllText(config," ");Reject(()=>PackageStore.Validate(pack.Directory,pack.Manifest),"tampered V2 parameters rejected");File.WriteAllText(config,before);
    var rules=pack.Manifest.Games.ToArray(); var profile=pack.Manifest.Profiles[0];
    pack.Manifest.Profiles.Add(profile with{Id="ambiguous"});pack.Manifest.Games.Add(rules[0] with{ProfileId="ambiguous"});
    Reject(()=>V2Packages.SelectOffline(pack),"ambiguous profile is not silently selected");
    pack.Manifest.Profiles.RemoveAt(pack.Manifest.Profiles.Count-1);pack.Manifest.Games.RemoveAt(pack.Manifest.Games.Count-1);
}
if(dependency.Version=="0.3.0")
{
    var pack=packs[0];string packManifestFile=Path.Combine(pack.Directory,"manifest.json"),savedManifest=File.ReadAllText(packManifestFile);
    var identity=TargetIdentity.Read(Environment.ProcessId);
    try
    {
        var manifest=JsonNode.Parse(savedManifest)!;
        manifest["games"]![0]!["executable"]=Path.GetFileName(identity.Path);manifest["games"]![0]!["sha256"]=PackageStore.Hash(identity.Path);
        File.WriteAllText(packManifestFile,manifest.ToJsonString(Wire.Json));bool denied=false;
        var changedPack=new InstalledPack(pack.Directory,PackageStore.ReadManifest(packManifestFile),pack.DataRoot);
        try{using var prepared=PreparedSession.Prepare(changedPack,identity.Path,identity.ProcessId);}
        catch(OperationException e)when(e.Error.Code=="evidence_insufficient"){denied=true;}
        Check(denied && !store.IsInUse(pack),"host preparation rejects unavailable modules before starting a backend and releases its version use");
    }
    finally {File.WriteAllText(packManifestFile,savedManifest);}
    await BackendPreflightChecks.Run(pack,shared,scratch,Check);
    string descriptorPath=Path.Combine(shared,"backend.json"),originalDescriptor=File.ReadAllText(descriptorPath);
    string invalidExecutable=Path.Combine(shared,"InvalidBackend.exe");
    var originalProfile=pack.Manifest.Profiles[0];
    try
    {
        File.WriteAllText(invalidExecutable,"synthetic invalid executable");
        var descriptor=JsonNode.Parse(originalDescriptor)!;
        descriptor["entryPoint"]="InvalidBackend.exe";descriptor["files"]!["InvalidBackend.exe"]=PackageStore.Hash(invalidExecutable);
        File.WriteAllText(descriptorPath,descriptor.ToJsonString(Wire.Json));
        pack.Manifest.Profiles[0]=originalProfile with{Backend=originalProfile.Backend with{DescriptorSha256=PackageStore.Hash(descriptorPath)}};
        var manifest=JsonNode.Parse(savedManifest)!;manifest["profiles"]![0]!["backend"]!["descriptorSha256"]=PackageStore.Hash(descriptorPath);
        File.WriteAllText(packManifestFile,manifest.ToJsonString(Wire.Json));
        await using var failed=new AdapterClient();bool rejected=false;
        var changedPack=new InstalledPack(pack.Directory,PackageStore.ReadManifest(packManifestFile),pack.DataRoot);
        try{await failed.Launch(changedPack);}catch(System.ComponentModel.Win32Exception){rejected=true;}
        Check(rejected && Directory.GetFiles(Path.Combine(scratch,"sessions")).Length==0,"failed process start removes prepared context and disposes client");
    }
    finally
    {
        File.WriteAllText(descriptorPath,originalDescriptor);File.Delete(invalidExecutable);pack.Manifest.Profiles[0]=originalProfile;File.WriteAllText(packManifestFile,savedManifest);
    }
}
Check(Directory.GetFiles(Path.Combine(scratch,"sessions")).Length==0,"session contexts removed after orderly shutdown");
{
    var client=new AdapterClient();await client.Launch(packs[0]);
    string contextFile=Directory.GetFiles(Path.Combine(scratch,"sessions")).Single();
    using(var held=new FileStream(contextFile,FileMode.Open,FileAccess.Read,FileShare.Read))
    {
        bool cleanupFailure=false;
        try{await client.DisposeAsync();}catch(OperationException e) when(e.Error.Code=="cleanup_incomplete"){cleanupFailure=true;}
        Check(cleanupFailure && !store.IsInUse(packs[0]),"context cleanup failure still releases package use after backend exit");
    }
    File.Delete(contextFile);
}
if(args.Contains("--legacy"))
{
    int at=Array.IndexOf(args,"--legacy");var legacy=new PackageStore(Path.Combine(scratch,"v1")).Install(args[at+1]);
    await using var old=new AdapterClient();await old.Launch(legacy);
    Check((await old.Call<List<AudioGroup>>("GetAudioGroups")).Count>0,"unchanged V1 package imports and launches through original entry point");
}
if(args.Contains("--capture"))
{
    int at=Array.IndexOf(args,"--capture");int pid=int.Parse(args[at+1]);string id=args[at+2];
    using var process=System.Diagnostics.Process.GetProcessById(pid);string path=process.MainModule!.FileName;
    var pack=packs.Single(p=>p.Manifest.Id==id);
    await using var client=new AdapterClient();long packets=0;float peak=0;var peaks=new System.Collections.Concurrent.ConcurrentDictionary<uint,float>();
    client.Audio+=p=>{Interlocked.Increment(ref packets);peak=Math.Max(peak,p.Peak);peaks.AddOrUpdate(p.GroupIndex,p.Peak,(_,v)=>Math.Max(v,p.Peak));};
    await client.Launch(pack,path,pid);await client.Connect(pid,path);await Task.Delay(5000);
    var report=new{pack=pack.Manifest.Id,packets,peak,groups=peaks.Select(p=>new{id=client.Groups[(int)p.Key].Id,name=client.Groups[(int)p.Key].Name,peak=p.Value}),status=await client.Call<AdapterStatus>("GetStatus")};
    string json=JsonSerializer.Serialize(report,Wire.Json);Console.WriteLine(json);File.WriteAllText(Path.Combine(root,"artifacts",pack.Manifest.Id+"-v2-capture.json"),json);
    Check(packets>0 && peak>0,"real game nonzero PCM using shared backend");
}
File.WriteAllText(Path.Combine(root,"artifacts/v2-last-test.json"),JsonSerializer.Serialize(new{success=true,scratch},Wire.Json));
