using System.IO.Compression;
using System.Text.Json;
using ApexAudio.Contracts;
using ApexAudio.Core;

string root=Path.GetFullPath(args[0]), packages=Path.Combine(root,"artifacts/v2-packages"), scratch=Path.Combine(root,"artifacts/v2-test-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
void Check(bool yes,string text) { if(!yes)throw new Exception(text); Console.WriteLine("PASS: "+text); }
void Reject(Action work,string text) { try {work();}catch(Exception e) when(e is IOException or InvalidDataException){Check(true,text);return;} throw new Exception("Unexpected acceptance: "+text); }
var store=new PackageStore(scratch);var backends=new BackendStore(scratch);
var packs=Directory.GetFiles(packages,"*.gpack").Select(store.Install).ToArray();
Check(packs.Length==2 && packs.All(p=>p.Manifest.SchemaVersion==2),"two executable-free V2 configuration packages imported");
var dependency=packs[0].Manifest.Profiles[0].Backend;
Reject(()=>backends.Resolve(dependency),"missing shared backend remains an explicit dependency error");
string shared=backends.Install(Path.Combine(packages,"ApexAudio-Fmod-0.2.0.gbackend"));
Check(shared==backends.Resolve(packs[1].Manifest.Profiles[0].Backend),"two games resolve to exactly one immutable backend installation");
Check(backends.Install(Path.Combine(packages,"ApexAudio-Fmod-0.2.0.gbackend"))==shared,"reimporting identical backend does not create another copy");
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
    string config=PackageStore.SafePath(pack.Directory,pack.Manifest.Profiles[0].Parameters),before=File.ReadAllText(config);
    File.AppendAllText(config," ");Reject(()=>PackageStore.Validate(pack.Directory,pack.Manifest),"tampered V2 parameters rejected");File.WriteAllText(config,before);
    var rules=pack.Manifest.Games.ToArray(); var profile=pack.Manifest.Profiles[0];
    pack.Manifest.Profiles.Add(profile with{Id="ambiguous"});pack.Manifest.Games.Add(rules[0] with{ProfileId="ambiguous"});
    Reject(()=>V2Packages.Select(pack,null),"ambiguous profile is not silently selected");
    pack.Manifest.Profiles.RemoveAt(pack.Manifest.Profiles.Count-1);pack.Manifest.Games.RemoveAt(pack.Manifest.Games.Count-1);
}
Check(Directory.GetFiles(Path.Combine(scratch,"sessions")).Length==0,"session contexts removed after orderly shutdown");
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
    await client.Launch(pack,path);await client.Connect(pid,path);await Task.Delay(5000);
    var report=new{pack=pack.Manifest.Id,packets,peak,groups=peaks.Select(p=>new{id=client.Groups[(int)p.Key].Id,name=client.Groups[(int)p.Key].Name,peak=p.Value}),status=await client.Call<AdapterStatus>("GetStatus")};
    string json=JsonSerializer.Serialize(report,Wire.Json);Console.WriteLine(json);File.WriteAllText(Path.Combine(root,"artifacts",pack.Manifest.Id+"-v2-capture.json"),json);
    Check(packets>0 && peak>0,"real game nonzero PCM using shared backend");
}
File.WriteAllText(Path.Combine(root,"artifacts/v2-last-test.json"),JsonSerializer.Serialize(new{success=true,scratch},Wire.Json));
