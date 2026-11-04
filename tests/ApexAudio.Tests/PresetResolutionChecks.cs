using System.Text.Json;
using ApexAudio.Contracts;
using ApexAudio.Core;

static class PresetResolutionChecks
{
    public static void Run(string scratch,Action<bool,string> check)
    {
        var pack=new InstalledPack(scratch,new PackManifest{Id="test.presets",Version="2.0.0"});
        var store=new PresetStore(Path.Combine(scratch,"resolution"));
        var source=new SoundPreset{Id="user-"+new string('a',32),Name="我的选择",PackId=pack.Manifest.Id,BasePackVersion="1.0.0",
            Folders=[new("folder:mine","我的分类")],Groups=new(){{"keep",new("我的名称",true,"folder:mine",7)},{"old",new("旧声音",true,"folder:mine",2)}}};
        source=store.Save(pack,source,source.Name,false);string file=Path.Combine(scratch,"resolution/presets",pack.Manifest.Id,source.Id+".json");byte[] bytes=File.ReadAllBytes(file);
        AudioGroup[] groups=[new("keep","新版作者名称",null,"音效",true),new("new","新声音",null,"音效",true)];
        var resolved=PresetStore.ResolveForGroups(pack,source,groups);
        check(resolved.Preset.Groups["keep"]==source.Groups["keep"] && !resolved.Preset.Groups["new"].Enabled && resolved.Preset.Groups["old"].Enabled,
            "stable IDs retain settings, new groups start disabled, and unavailable settings remain stored");
        check(resolved.Notice.Contains("重新配置") && resolved.UnavailableGroups.SequenceEqual(new[]{"old"}) && File.ReadAllBytes(file).SequenceEqual(bytes) && !source.Groups.ContainsKey("new"),
            "resolution reports partial reconfiguration without changing source preset or disk");
        var mapped=PresetStore.ResolveForGroups(pack,source,groups,new Dictionary<string,string>{{"old","new"}});
        check(mapped.Preset.Groups["new"]==source.Groups["old"] && mapped.Preset.Groups["old"]==source.Groups["old"] && mapped.MappedGroups.Single()=="new",
            "explicit one-to-one mapping copies confirmed settings while retaining rollback data");
        var before=JsonSerializer.Serialize(source,Wire.Json);bool refused=false;
        try{PresetStore.ResolveForGroups(pack,source,groups,new Dictionary<string,string>{{"keep","new"}});}catch(OperationException e)when(e.Error.Code=="preset_mapping_conflict"){refused=true;}
        check(refused && JsonSerializer.Serialize(source,Wire.Json)==before,"ambiguous mapping cannot overwrite current identities or mutate the source");
        var saved=store.Save(pack,resolved.Preset,resolved.Preset.Name,false);
        var rollback=store.LoadResolution(pack,saved.Id,new[]{groups[0],new AudioGroup("old","旧声音",null,"音效",true)});
        check(rollback.Preset.Groups["old"].Enabled && rollback.Preset.Groups["keep"].Order==7 && rollback.Preset.Folders.Single().Name=="我的分类",
            "saving and rolling back preserves historical selection and display organization");
        var aggregate=new SoundPreset{Id="aggregate",Name="全部音效",PackId=pack.Manifest.Id,Groups=new(){{"mix",new(Enabled:true)},{"keep",new(Enabled:true)}}};
        var hierarchy=new[]{new AudioGroup("mix","全部音效",null,"混音",true,true),groups[0] with{ParentId="mix"},groups[1] with{ParentId="mix"}};
        var mixed=PresetStore.ResolveForGroups(pack,aggregate,hierarchy);
        check(mixed.Preset.Groups["mix"].Enabled && !mixed.Preset.Groups["new"].Enabled && mixed.ReviewGroups.Contains("mix") && mixed.Notice.Contains("仍包含"),
            "new independent route preserves an enabled parent mix and explains its inclusive audio");
    }
}
