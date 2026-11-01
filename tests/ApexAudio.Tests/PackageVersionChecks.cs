using System.IO.Compression;
using System.Text.Json.Nodes;
using ApexAudio.Contracts;
using ApexAudio.Core;
using ApexAudio.Application;

static class PackageVersionChecks
{
    public static async Task Run(string root,string scratch,Action<bool,string> check)
    {
        string data=Path.Combine(scratch,"versions"),original=Path.Combine(root,"artifacts/packages/demo.gpack");
        var store=new PackageStore(data);var first=store.Install(original);
        check(store.GetCurrent(first.Manifest.Id)==null,"import records a version without selecting it");
        store.SelectCurrent(first.Manifest.Id,first.Reference);
        string next=Path.Combine(scratch,"next-version.gpack");Rewrite(original,next,"9.0.0");var second=store.Install(next);
        check(store.List().Count==2 && store.GetCurrent(first.Manifest.Id)!.Reference==first.Reference,"new import preserves installed and current old version");
        check(store.Install(next).Reference==second.Reference && store.List().Count==2,"identical version import is idempotent");
        string conflicting=Path.Combine(scratch,"conflicting-version.gpack");Rewrite(original,conflicting,"9.0.0","changed content");
        Reject(()=>store.Install(conflicting),"version_content_conflict");
        check(store.GetCurrent(first.Manifest.Id)!.Reference==first.Reference,"conflicting import does not alter current selection");
        using(store.AcquireUse(first))
        {
            store.SelectCurrent(second.Manifest.Id,second.Reference);
            Reject(()=>store.RemoveVersion(first.Reference),"version_in_use");
            check(store.ListVersions().Single(p=>p.Pack.Reference==first.Reference).InUse,"session use remains visible after selecting a different version");
        }
        Reject(()=>store.RemoveVersion(second.Reference),"current_version_selected");
        check(new PackageStore(data).GetCurrent(second.Manifest.Id)!.Reference==second.Reference,"current version persists across store restart");
        string recording=Path.Combine(data,"recordings/example.wav"),preset=Path.Combine(data,"presets/example.json");
        Directory.CreateDirectory(Path.GetDirectoryName(recording)!);Directory.CreateDirectory(Path.GetDirectoryName(preset)!);
        File.WriteAllText(recording,"recording bytes");File.WriteAllText(preset,"preset bytes");
        store.RemoveVersion(first.Reference);
        check(File.ReadAllText(recording)=="recording bytes" && File.ReadAllText(preset)=="preset bytes" && store.List().Single().Reference==second.Reference,"historical removal preserves user data and other versions");
        var missing=store.Install(Directory.GetFiles(Path.Combine(root,"tests/fixtures/v2"),"*.gpack").First());
        Reject(()=>store.SelectCurrent(missing.Manifest.Id,missing.Reference),"backend_missing");
        check(store.GetCurrent(missing.Manifest.Id)==null && store.GetCurrent(second.Manifest.Id)!.Reference==second.Reference,"missing backend does not replace another current selection");
        string legacyRoot=Path.Combine(scratch,"legacy-versions"),legacy=Path.Combine(legacyRoot,"packs",first.Manifest.Id);
        ZipFile.ExtractToDirectory(original,legacy);var oldStore=new PackageStore(legacyRoot);
        check(oldStore.GetCurrent(first.Manifest.Id)!.Directory==legacy,"legacy single-version installation remains selected without repacking");
        oldStore.Install(next);PackageStore.Validate(legacy,PackageStore.ReadManifest(Path.Combine(legacy,"manifest.json")));
        oldStore.SelectCurrent(first.Manifest.Id,null);
        check(new PackageStore(legacyRoot).GetCurrent(first.Manifest.Id)==null,"explicitly clearing current overrides legacy discovery");
        string brokenManifest=Path.Combine(missing.Directory,"manifest.json"),savedManifest=File.ReadAllText(brokenManifest);
        try
        {
            File.WriteAllText(brokenManifest,"{");var versions=store.ListVersions();
            check(versions.Any(v=>v.Pack.Reference==second.Reference) && store.CatalogIssues.Any(),"broken install is isolated from other package versions");
            var wrongSchema=JsonNode.Parse(savedManifest)!;wrongSchema["schemaVersion"]="2";
            var nullProfiles=JsonNode.Parse(savedManifest)!;nullProfiles["profiles"]=null;
            foreach(string invalid in new[]{"[]",wrongSchema.ToJsonString(),nullProfiles.ToJsonString()})
            {
                File.WriteAllText(brokenManifest,invalid);versions=store.ListVersions();
                check(versions.Any(v=>v.Pack.Reference==second.Reference) && store.CatalogIssues.Any(),"invalid manifest field shapes do not block healthy package versions");
            }
        }
        finally{File.WriteAllText(brokenManifest,savedManifest);}
        string sessionData=Path.Combine(scratch,"version-sessions");
        await using(var workspace=new AudioWorkspace(sessionData))
        {
            var a=workspace.Packages.Install(original);workspace.Packages.SelectCurrent(a.Manifest.Id,a.Reference);
            await workspace.SelectPack(a.Manifest.Id);await workspace.Connect(0,null);
            var b=workspace.Packages.Install(next);workspace.Packages.SelectCurrent(b.Manifest.Id,b.Reference);
            check(workspace.Pack!.Reference==a.Reference && workspace.Connected && workspace.Packages.IsInUse(a),"selecting new default leaves active session and old version pin unchanged");
            var confirmed=b.Reference;workspace.Packages.SelectCurrent(a.Manifest.Id,a.Reference);
            await workspace.Connect(0,null,confirmed);
            check(workspace.Pack!.Reference==confirmed && workspace.Packages.GetCurrent(a.Manifest.Id)!.Reference==a.Reference,"explicitly confirmed version is used even when default changes before connect");
        }
        void Reject(Action work,string code)
        {
            try{work();}catch(OperationException e) when(e.Error.Code==code){check(true,code+" returns a stable actionable error");return;}
            throw new Exception("Expected operation failure: "+code);
        }
    }
    static void Rewrite(string source,string output,string version,string? description=null)
    {
        using var input=ZipFile.OpenRead(source);using var archive=ZipFile.Open(output,ZipArchiveMode.Create);
        foreach(var entry in input.Entries)
        {
            var dest=archive.CreateEntry(entry.FullName);using var written=dest.Open();using var read=entry.Open();
            if(entry.FullName!="manifest.json"){read.CopyTo(written);continue;}
            using var reader=new StreamReader(read);var manifest=JsonNode.Parse(reader.ReadToEnd())!;
            manifest["version"]=version;if(description!=null)manifest["description"]=description;
            using var writer=new StreamWriter(written);writer.Write(manifest.ToJsonString());
        }
    }
}
