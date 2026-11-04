using ApexAudio.Contracts;

namespace ApexAudio.Core;

public sealed record PresetResolution(SoundPreset Preset,string[] AddedGroups,string[] UnavailableGroups,string[] MappedGroups,string[] ReviewGroups,string Notice);

public sealed partial class PresetStore
{
    public static PresetResolution ResolveForGroups(InstalledPack pack,SoundPreset original,IReadOnlyList<AudioGroup> groups,
        IReadOnlyDictionary<string,string>? mapping=null,bool existingSelection=true)
    {
        Validate(original,pack.Manifest.Id);
        if(groups.Any(g=>g==null || string.IsNullOrWhiteSpace(g.Id)) || groups.Select(g=>g.Id).Distinct().Count()!=groups.Count)
            throw new OperationException("group_invalid","声音组身份无效或重复。","refresh_groups");
        var actual=groups.ToDictionary(g=>g.Id);
        foreach(var group in groups)
        {
            var seen=new HashSet<string>{group.Id};string? parent=group.ParentId;
            while(parent!=null){if(!actual.TryGetValue(parent,out var next) || !seen.Add(parent))throw new OperationException("group_invalid","声音组层级无效。","refresh_groups");parent=next.ParentId;}
        }
        mapping ??= new Dictionary<string,string>();
        var targets=new HashSet<string>();
        foreach(var(source,target)in mapping)
            if(string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target) || source==target || !original.Groups.ContainsKey(source) || actual.ContainsKey(source) || !actual.ContainsKey(target) || original.Groups.ContainsKey(target) || !targets.Add(target))
                throw new OperationException("preset_mapping_conflict","仅能将不可用旧组明确一对一映射到尚无设置的新组，不能覆盖、拆分或合并。","configure_groups");
        var preset=Copy(original);
        string[] added=groups.Where(g=>!original.Groups.ContainsKey(g.Id)).Select(g=>g.Id).ToArray();
        string[] unavailable=original.Groups.Keys.Where(id=>!actual.ContainsKey(id)).ToArray();
        foreach(var(source,target)in mapping)preset.Groups[target]=original.Groups[source];
        foreach(var group in groups)preset.Groups.TryAdd(group.Id,new(Enabled:false));
        var unconfigured=added.Where(id=>actual[id].HasPcm && !targets.Contains(id)).ToArray();
        var review=new HashSet<string>();
        foreach(string id in groups.Where(g=>g.HasPcm && !preset.Groups[g.Id].Enabled).Select(g=>g.Id))
        {
            string? parent=actual[id].ParentId;
            while(parent!=null)
            {
                var group=actual[parent];
                if(group.HasPcm && group.ContainsChildren && preset.Groups[parent].Enabled)
                    review.Add(parent);
                parent=group.ParentId;
            }
        }
        var messages=new List<string>();
        if(existingSelection && unconfigured.Length>0)messages.Add($"部分设置需要重新配置：{unconfigured.Length} 个新增或未对应的声音组默认不参与反馈。");
        if(review.Count>0)messages.Add($"{review.Count} 个已启用父混音组仍包含未勾选子组的声音。若要排除其中的声音，请关闭父混音组并改选需要的独立组。");
        if(unavailable.Length>0)messages.Add($"{unavailable.Length} 个历史组暂不可用，旧设置已保留供回退。");
        return new(preset,added,unavailable,targets.ToArray(),review.ToArray(),string.Join(" ",messages));
    }
}
