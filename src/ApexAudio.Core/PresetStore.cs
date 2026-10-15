using System.Text.Json;
using ApexAudio.Contracts;

namespace ApexAudio.Core;

public sealed record PresetOption(string Id, string Name, bool Author);

public sealed class PresetStore
{
    readonly string root;
    public PresetStore(string dataRoot) { root = Path.Combine(dataRoot, "presets"); Directory.CreateDirectory(root); }
    static SoundPreset Read(string file) => JsonSerializer.Deserialize<SoundPreset>(File.ReadAllText(file), Wire.Json) ?? throw new InvalidDataException("预设为空。");
    static SoundPreset Copy(SoundPreset preset) => JsonSerializer.Deserialize<SoundPreset>(JsonSerializer.Serialize(preset, Wire.Json), Wire.Json)!;
    public static void Validate(SoundPreset preset, string packId)
    {
        if (preset == null || preset.SchemaVersion != 1 || preset.PackId != packId || string.IsNullOrWhiteSpace(preset.Id) || string.IsNullOrWhiteSpace(preset.Name)) throw new InvalidDataException("预设格式或适配包身份不匹配。");
        if (preset.Folders.Select(f => f.Id).Distinct().Count() != preset.Folders.Count || preset.Folders.Any(f => !f.Id.StartsWith("folder:"))) throw new InvalidDataException("显示文件夹编号无效。");
        var folders = preset.Folders.ToDictionary(f => f.Id);
        foreach (var folder in preset.Folders)
        {
            var seen = new HashSet<string> { folder.Id }; var parent = folder.ParentId;
            while (parent != null) { if (!folders.TryGetValue(parent, out var next) || !seen.Add(parent)) throw new InvalidDataException("显示文件夹层级无效。"); parent = next.ParentId; }
        }
        if (preset.Groups.Values.Any(s => s.FolderId != null && !folders.ContainsKey(s.FolderId))) throw new InvalidDataException("预设引用了不存在的显示文件夹。");
    }
    public List<SoundPreset> Authors(InstalledPack pack) => pack.Manifest.Presets.Select(p => Read(PackageStore.SafePath(pack.Directory, p))).ToList();
    string UserDirectory(string packId) { string path = PackageStore.SafePath(root, packId); Directory.CreateDirectory(path); return path; }
    public List<PresetOption> List(InstalledPack pack) => Authors(pack).Select(p => new PresetOption("author:" + p.Id, p.Name, true))
        .Concat(Directory.EnumerateFiles(UserDirectory(pack.Manifest.Id), "*.json").Select(Read).Select(p => new PresetOption(p.Id, p.Name, false))).ToList();
    public SoundPreset Load(InstalledPack pack, string id, IReadOnlyList<AudioGroup> groups)
    {
        SoundPreset preset;
        if (id.StartsWith("author:"))
        {
            preset = Copy(Authors(pack).Single(p => "author:" + p.Id == id));
            preset.BasePresetId = preset.Id; preset.Id = id; preset.BasePackVersion = pack.Manifest.Version;
        }
        else preset = Read(Path.Combine(UserDirectory(pack.Manifest.Id), id + ".json"));
        Validate(preset, pack.Manifest.Id);
        foreach (var g in groups) preset.Groups.TryAdd(g.Id, new(null, false));
        return preset;
    }
    public SoundPreset Empty(InstalledPack pack, IReadOnlyList<AudioGroup> groups) => new()
    {
        Id = "user-" + Guid.NewGuid().ToString("N"), Name = "我的预设", PackId = pack.Manifest.Id,
        Groups = groups.ToDictionary(g => g.Id, g => new GroupSetting())
    };
    public SoundPreset Save(InstalledPack pack, SoundPreset preset, string name, bool copy)
    {
        var next = Copy(preset); next.Name = name.Trim();
        if (copy || next.Id.StartsWith("author:")) next.Id = "user-" + Guid.NewGuid().ToString("N");
        Validate(next, pack.Manifest.Id);
        if (!System.Text.RegularExpressions.Regex.IsMatch(next.Id, "^user-[a-f0-9]{32}$")) throw new InvalidDataException("用户预设编号无效。");
        string file = Path.Combine(UserDirectory(pack.Manifest.Id), next.Id + ".json"); AtomicWrite(file, next); return next;
    }
    public SoundPreset Restore(InstalledPack pack, SoundPreset current, IReadOnlyList<AudioGroup> groups)
    {
        string? basis = current.BasePresetId;
        if (basis == null) throw new InvalidOperationException("该预设没有作者原稿。");
        var restored = Load(pack, "author:" + basis, groups); restored.Id = current.Id; restored.Name = current.Name; return restored;
    }
    public void Delete(InstalledPack pack, string id)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^user-[a-f0-9]{32}$")) throw new InvalidOperationException("作者预设保留在适配包中。");
        File.Delete(Path.Combine(UserDirectory(pack.Manifest.Id), id + ".json"));
    }
    public SoundPreset Import(InstalledPack pack, string file, IReadOnlyList<AudioGroup> groups)
    {
        var p = Read(file); Validate(p, pack.Manifest.Id); p = Save(pack, p, p.Name, true);
        foreach (var g in groups) p.Groups.TryAdd(g.Id, new()); return p;
    }
    public static void AtomicWrite<T>(string file, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!); string temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Wire.Json)); File.Move(temporary, file, true);
    }
}
