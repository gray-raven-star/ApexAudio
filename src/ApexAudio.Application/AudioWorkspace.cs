using System.Diagnostics;
using System.Text.Json;
using ApexAudio.Contracts;
using ApexAudio.Core;
using ApexAudio.XHaptics;

namespace ApexAudio.Application;

public sealed class AudioWorkspace : IAsyncDisposable
{
    readonly object gate = new();
    readonly Dictionary<string, (float Peak, long Tick)> levels = new();
    readonly HashSet<string> verified = new();
    HashSet<string> enabled = new(); long sequence = -1, missing;
    AdapterClient? adapter; HapticCollector? haptics; string status = "请选择适配包，或导入 .gpack";
    Task<bool> feedbackSelection = Task.FromResult(true);
    public PackageStore Packages { get; }
    public PresetStore Presets { get; }
    public RecordingStore Recordings { get; }
    public FeedbackOutput Feedback { get; } = new();
    public InstalledPack? Pack { get; private set; }
    public SoundPreset? Preset { get; private set; }
    public IReadOnlyList<AudioGroup> Groups { get; private set; } = Array.Empty<AudioGroup>();
    public AdapterCapabilities? Capabilities => adapter?.Info?.Capabilities;
    public int GamePid { get; private set; }
    public RecordingSession? Take { get; private set; }
    public bool Connected => adapter?.IsAlive == true && Groups.Count > 0;
    public bool Dirty { get; private set; }
    public string? LastPresetId { get; private set; }
    public long ConnectionId { get; private set; }
    public event Action<long, string>? ConnectionLost;
    public AudioWorkspace(string root) { Packages = new(root); Presets = new(root); Recordings = new(root); Feedback.Engine.SessionFile=Path.Combine(root,"xh-engine-session.json"); }
    public async Task SelectPack(string id)
    {
        await Disconnect(); Pack = Packages.List().Single(p => p.Manifest.Id == id);
        Groups = Array.Empty<AudioGroup>(); Preset = null; LastPresetId = null; Take = Recordings.List().FirstOrDefault(t => t.PackId == id);
        status = Pack.Manifest.Demo ? "演示适配包 · 可直接连接" : "等待游戏运行";
    }
    public async Task Connect(int pid, string? path)
    {
        var pack = Pack ?? throw new InvalidOperationException("请先选择适配包。");
        var previousPreset = Preset?.PackId == pack.Manifest.Id ? Preset : null; bool previousDirty = Dirty;
        await Disconnect(); status = "正在连接采集接口…";
        var client = new AdapterClient(); adapter = client;
        long connectionId = ++ConnectionId;
        client.Audio += Accept; client.Failed += reason => { if (!ReferenceEquals(adapter, client)) return; status = reason; ConnectionLost?.Invoke(connectionId, reason); };
        try
        {
            await client.Launch(pack); await client.Connect(pid, path); Groups = client.Groups; GamePid = pid;
            var options = Presets.List(pack); string? first = options.FirstOrDefault(o => o.Id == LastPresetId)?.Id ?? options.FirstOrDefault()?.Id;
            Preset = previousPreset ?? (first != null ? Presets.Load(pack, first, Groups) : Presets.Empty(pack, Groups)); LastPresetId = Preset.Id; Dirty = previousPreset != null && previousDirty;
            Apply(); status = "已连接 · 等待音频数据";
        }
        catch { await Disconnect(); throw; }
    }
    void Accept(AudioPacket packet)
    {
        if (packet.GroupIndex >= Groups.Count) return; var group = Groups[(int)packet.GroupIndex];
        lock (gate)
        {
            if (sequence >= 0 && packet.Sequence > sequence + 1) missing += packet.Sequence - sequence - 1;
            sequence = packet.Sequence; verified.Add(group.Id); levels[group.Id] = (packet.Peak, Stopwatch.GetTimestamp());
            if (Feedback.Enabled && Capabilities?.RealtimeMix != true && enabled.Contains(group.Id)) Feedback.Accept(packet);
        }
        Recordings.Accept(group, packet);
    }
    public object Activity()
    {
        lock (gate)
        {
            long now = Stopwatch.GetTimestamp();
            return new { levels = levels.ToDictionary(p => p.Key, p => now - p.Value.Tick < Stopwatch.Frequency / 3 ? p.Value.Peak : 0), verified = verified.ToArray(), missing };
        }
    }
    public string Status => status;
    public void Apply()
    {
        if (Preset == null) return;
        var groups = Groups.ToDictionary(g => g.Id);
        bool Selected(string id) => Preset.Groups.GetValueOrDefault(id)?.Enabled == true;
        bool Covered(AudioGroup g)
        {
            string? parent = g.ParentId;
            while (parent != null && groups.TryGetValue(parent, out var ancestor))
            { if (ancestor.HasPcm && ancestor.ContainsChildren && Selected(parent)) return true; parent = ancestor.ParentId; }
            return false;
        }
        string[] selection;
        lock (gate) { enabled = Groups.Where(g => g.HasPcm && Selected(g.Id) && !Covered(g)).Select(g => g.Id).ToHashSet(); selection = enabled.ToArray(); Feedback.Clear(); }
        if (adapter is { } client && Capabilities?.RealtimeMix == true) feedbackSelection = SendFeedbackSelection(client, selection);
    }
    async Task<bool> SendFeedbackSelection(AdapterClient client, string[] selection)
    {
        try { await client.Call<AdapterStatus>("SetFeedbackGroups", new CaptureRequest(selection)); return true; }
        catch (Exception e) when (e is IOException or OperationCanceledException)
        {
            if (ReferenceEquals(adapter, client)) ConnectionLost?.Invoke(ConnectionId, e.Message);
            return false;
        }
    }
    public async Task StartFeedback()
    {
        var client = adapter ?? throw new IOException("请先连接声音采集。");
        FeedbackSource? source = null;
        if (Capabilities?.RealtimeMix == true)
        {
            if (!await feedbackSelection || !ReferenceEquals(adapter, client)) throw new IOException("声音分组设置尚未应用。");
            source = await client.Call<FeedbackSource>("GetFeedbackSource");
        }
        await Feedback.Start(source);
    }
    public void SetGroup(string id, bool value)
    {
        if (Preset == null) return;
        var group = Groups.Single(g => g.Id == id);
        var descendants = new HashSet<string> { id }; bool changed;
        do { changed = false; foreach (var g in Groups) if (g.ParentId != null && descendants.Contains(g.ParentId)) changed |= descendants.Add(g.Id); } while (changed);
        foreach (string child in descendants) { var old = Preset.Groups.GetValueOrDefault(child) ?? new(); Preset.Groups[child] = old with { Enabled = value }; }
        // A child override breaks an aggregate parent's selection.
        if (!value)
        {
            string? parent = group.ParentId; while (parent != null) { var old = Preset.Groups.GetValueOrDefault(parent) ?? new(); Preset.Groups[parent] = old with { Enabled = false }; parent = Groups.Single(g => g.Id == parent).ParentId; }
        }
        Dirty = true; Apply();
    }
    public void SetFolder(string folderId, bool value)
    {
        if (Preset == null) return;
        var folders = new HashSet<string> { folderId }; bool changed;
        do { changed = false; foreach (var f in Preset.Folders) if (f.ParentId != null && folders.Contains(f.ParentId)) changed |= folders.Add(f.Id); } while (changed);
        foreach (string id in Preset.Groups.Where(k => k.Value.FolderId != null && folders.Contains(k.Value.FolderId)).Select(k => k.Key).ToArray()) SetGroup(id, value);
    }
    public void EditGroup(string id, string name, string? folderId, int order)
    {
        if (Preset == null) return;
        var old = Preset.Groups.GetValueOrDefault(id) ?? new(); Preset.Groups[id] = old with { Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(), FolderId = folderId, Order = order };
        PresetStore.Validate(Preset, Pack!.Manifest.Id); Dirty = true;
    }
    public void AddFolder(string name) { if (Preset == null) return; Preset.Folders.Add(new("folder:" + Guid.NewGuid().ToString("N"), name.Trim())); Dirty = true; }
    public void EditFolder(string id, string name, bool delete)
    {
        if (Preset == null) return;
        int at = Preset.Folders.FindIndex(f => f.Id == id); if (at < 0) return;
        if (!delete) Preset.Folders[at] = Preset.Folders[at] with { Name = name.Trim() };
        else
        {
            Preset.Folders.RemoveAt(at);
            for (int i = 0; i < Preset.Folders.Count; i++) if (Preset.Folders[i].ParentId == id) Preset.Folders[i] = Preset.Folders[i] with { ParentId = null };
            foreach (string key in Preset.Groups.Keys.ToArray()) if (Preset.Groups[key].FolderId == id) Preset.Groups[key] = Preset.Groups[key] with { FolderId = null };
        }
        Dirty = true;
    }
    public void LoadPreset(string id) { Preset = Presets.Load(Pack!, id, Groups); LastPresetId = id; Dirty = false; Apply(); }
    public void SavePreset(string name, bool copy) { Preset = Presets.Save(Pack!, Preset!, name, copy); LastPresetId = Preset.Id; Dirty = false; Apply(); }
    public void RestorePreset() { Preset = Presets.Restore(Pack!, Preset!, Groups); Dirty = true; Apply(); }
    public Task StartRecording()
    {
        if (!Connected || Capabilities?.IndependentPcm != true) throw new InvalidOperationException("需要连接能够提供独立音轨的适配包。");
        Recordings.Start(Pack!, Groups, Preset!); haptics = new(Feedback.Port, "X-Haptics 声音补充分量",Feedback.Engine); haptics.Start(); return Task.CompletedTask;
    }
    public async Task StopRecording(string note = "")
    {
        if (!Recordings.IsRecording) return;
        Take = Recordings.Stop(note: note); if (Take == null) return;
        if (haptics != null) { var collected = await haptics.Finish(Take.OriginTick, (int)(Take.Duration * 48000), 0); Take.Haptics = Wire.Element(collected); await haptics.DisposeAsync(); haptics = null; Recordings.SaveMetadata(Take); }
    }
    public async Task<AdapterStatus?> Poll()
    {
        if (!Connected) return null;
        var client = adapter!; long connectionId = ConnectionId;
        var result = await client.Call<AdapterStatus>("GetStatus", timeoutMs: 2000);
        if (!ReferenceEquals(adapter, client)) return null;
        status = result.Detail;
        if (!result.Connected || (Capabilities?.IndependentPcm == true && !result.Capturing)) ConnectionLost?.Invoke(connectionId, result.Detail);
        return result;
    }
    public void SelectTake(string id) => Take = Recordings.Load(id);
    public void RenameTrack(string takeId, string groupId, string name)
    {
        name = name.Trim(); if (name.Length is < 1 or > 200) throw new IOException("名称需要为 1～200 个字符。");
        var take = Recordings.Load(takeId); var track = take.Tracks.Single(t => t.GroupId == groupId);
        track.Name = name; Recordings.SaveMetadata(take);
        if (Take?.Id == takeId) Take = take;
        // A recording from another game must never edit this game's preset.
        if (Preset?.PackId == take.PackId && Groups.Any(g => g.Id == groupId))
        {
            var old = Preset.Groups.GetValueOrDefault(groupId) ?? new();
            Preset.Groups[groupId] = old with { Name = name }; Dirty = true;
        }
    }
    public async Task Disconnect()
    {
        ++ConnectionId;
        var client = adapter; adapter = null;
        await StopRecording("采集连接结束。"); await Feedback.Stop();
        if (client != null) { client.Audio -= Accept; await client.DisposeAsync(); }
        lock (gate) { levels.Clear(); verified.Clear(); enabled.Clear(); sequence = -1; missing = 0; }
        GamePid = 0; status = "已断开";
    }
    public async ValueTask DisposeAsync() { await Disconnect(); await Feedback.DisposeAsync(); Recordings.Dispose(); }
}
