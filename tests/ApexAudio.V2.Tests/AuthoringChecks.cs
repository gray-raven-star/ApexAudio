using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApexAudio.Authoring;
using ApexAudio.Contracts;
using ApexAudio.Core;
using ApexAudio.Fmod.Definition;

static class AuthoringChecks
{
    public static async Task Run(InstalledPack example,string backend,string scratch,PackageStore store,Action<bool,string> check)
    {
        var descriptor=GamepackAuthoring.Describe(backend);
        check(descriptor.Support!=null && FmodParameterContract.MatchesSupport(descriptor.Support.Parameters["studio-v1"]),"backend describes the same parameter contract used by its implementation");
        string created=Path.Combine(scratch,"author-template");GamepackAuthoring.Create(created,backend);
        var diagnostics=GamepackAuthoring.Validate(created,new[]{backend});
        check(diagnostics.Any(d=>d.Code=="placeholder" && d.File=="manifest.json") && diagnostics.Any(d=>d.File=="profiles/default.json" && d.Path.StartsWith("$.")),
            "new author template is explicitly incomplete with file and field diagnostics");
        bool overwrite=false;try{GamepackAuthoring.Create(created,backend);}catch(IOException){overwrite=true;}
        check(overwrite,"creating a template never overwrites an existing definition");
        string definition=Path.Combine(scratch,"author-definition");
        foreach(string file in Directory.EnumerateFiles(example.Directory,"*",SearchOption.AllDirectories))
        {string output=Path.Combine(definition,Path.GetRelativePath(example.Directory,file));Directory.CreateDirectory(Path.GetDirectoryName(output)!);File.Copy(file,output);}
        string manifest=Path.Combine(definition,"manifest.json"),parameters=PackageStore.SafePath(definition,example.Manifest.Profiles[0].Parameters);
        byte[] originalManifest=File.ReadAllBytes(manifest);string validParameters=File.ReadAllText(parameters);
        check(GamepackAuthoring.Validate(definition,new[]{backend}).Count==0 && File.ReadAllBytes(manifest).SequenceEqual(originalManifest),"static validation accepts an existing definition without rewriting author files");
        var json=JsonNode.Parse(validParameters)!;json["unknownOperation"]="not supported";File.WriteAllText(parameters,json.ToJsonString());
        diagnostics=GamepackAuthoring.Validate(definition,new[]{backend});
        check(diagnostics.Any(d=>d.Path=="$.unknownOperation" && d.Code=="unsupported_field"),"unknown parameter fields are rejected at their JSON path");
        string failed=Path.Combine(scratch,"invalid-author-package.gpack");bool refused=false;
        try{GamepackAuthoring.Package(definition,failed,new[]{backend});}catch(AuthoringValidationException){refused=true;}
        check(refused && !File.Exists(failed) && Directory.GetFiles(scratch,"invalid-author-package.gpack.*.tmp").Length==0,"failed author validation leaves no formal or partial package");
        // Bypass author checks deliberately: the runtime must reject the same parameter independently.
        var document=JsonNode.Parse(File.ReadAllText(manifest))!;document["version"]="0.3.99";
        document["files"]![example.Manifest.Profiles[0].Parameters]=PackageStore.Hash(parameters);File.WriteAllText(manifest,document.ToJsonString());
        string bypass=Path.Combine(scratch,"bypass-author-checks.gpack");ZipFile.CreateFromDirectory(definition,bypass);var invalid=store.Install(bypass);
        bool runtimeRefused=false;await using(var client=new AdapterClient())
        {
            try{await client.Launch(invalid);}
            catch(AdapterOperationException e)when(e.Error.Code=="backend_unsupported" && e.Error.Details?.GetValueOrDefault("field")=="$.unknownOperation"){runtimeRefused=true;}
        }
        check(runtimeRefused && !store.IsInUse(invalid),"runtime independently reports invalid parameters over IPC and cleans up instead of timing out");
        File.WriteAllBytes(manifest,originalManifest);File.WriteAllText(parameters,validParameters);
        string presetFile=PackageStore.SafePath(definition,example.Manifest.Presets[0]);string presetText=File.ReadAllText(presetFile);
        foreach(string bad in new[]{"folders-null","groups-null","folders-item","groups-item"})
        {
            var preset=JsonNode.Parse(presetText)!;
            if(bad=="folders-null")preset["folders"]=null;
            if(bad=="groups-null")preset["groups"]=null;
            if(bad=="folders-item")preset["folders"]=new JsonArray((JsonNode?)null);
            if(bad=="groups-item")preset["groups"]=new JsonObject{{"unknown",null}};
            File.WriteAllText(presetFile,preset.ToJsonString());
            check(GamepackAuthoring.Validate(definition,new[]{backend}).Any(d=>d.File==example.Manifest.Presets[0] && d.Path.StartsWith("$.")),"malformed preset returns file and field diagnostics: "+bad);
        }
        File.WriteAllText(presetFile,presetText);
        string archive=Path.Combine(scratch,"authored-package.gpack");var identity=GamepackAuthoring.Package(definition,archive,new[]{backend});
        check(new PackageStore(Path.Combine(scratch,"authored-import")).Install(archive).Reference==identity,"authored package identity survives normal import");
        foreach(var (field,value) in new (string,JsonNode?)[]{("buses",null),("runtimeVersion",JsonValue.Create(0)),("locator",JsonValue.Create("unsupported")),("wrapperModule",JsonValue.Create("../game.dll"))})
        {
            json=JsonNode.Parse(validParameters)!;json[field]=value;
            check(FmodParameterContract.Check(Wire.Element(json),"parameters.json").Any(d=>d.Path=="$."+field),"parameter types and supported combinations reject invalid "+field);
        }
    }
}
