using System.Diagnostics;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Text.Json;
using ApexAudio.Contracts;
using ApexAudio.Core;
using ApexAudio.Application;
using ApexAudio.Wwise;

string root = Path.GetFullPath(args[0]), scratch = Path.Combine(root, "artifacts", "acceptance-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
var checks = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); checks.Add(name); Console.WriteLine("PASS: " + name); }
WaveformChecks.Run(scratch, Check);
if (args.Contains("--waveform-only")) return;
await FeedbackChecks.Run((condition, name) => { if (!condition) throw new Exception("FAIL: " + name); if (!checks.Contains(name)) Check(true, name); });
await HapticChecks.Run(Check);
if (args.Contains("--feedback-only"))
{
    File.WriteAllText(Path.Combine(root, "artifacts", "feedback-repair.json"), JsonSerializer.Serialize(new { success = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
    return;
}
var store = new PackageStore(scratch); var pack = store.Install(Path.Combine(root, "artifacts", "packages", "demo.gpack"));
Check(store.List().Count == 1, "complete package import and integrity");
var presets = new PresetStore(scratch);
int shutdownFailures = 0;
for (int i = 0; i < 3; i++)
{
    var closingClient = new AdapterClient(); closingClient.Failed += _ => shutdownFailures++;
    await closingClient.Launch(pack); await closingClient.Connect(0, null); await closingClient.DisposeAsync();
}
Check(shutdownFailures == 0, "normal adapter shutdown never reports a failed connection");
await using (var workspace = new AudioWorkspace(scratch))
{
    int lost = 0; workspace.ConnectionLost += (_, _) => lost++;
    await workspace.SelectPack(pack.Manifest.Id); await workspace.Connect(0, null);
    workspace.SetGroup("impact", false); long connectionId = workspace.ConnectionId;
    await workspace.Connect(0, null);
    Check(workspace.Connected && workspace.ConnectionId != connectionId && lost == 0, "reconnecting replaces the connection without stale failure events");
    Check(workspace.Dirty && !workspace.Preset!.Groups["impact"].Enabled, "reconnecting preserves unsaved sound selections");
    await workspace.StartRecording(); await Task.Delay(400); await workspace.StopRecording();
    var take = workspace.Take!; byte[] beforeRename = workspace.Recordings.Clip(take.Id, "impact", 0, .2, 1);
    workspace.RenameTrack(take.Id, "impact", "我的撞击音效");
    Check(workspace.Recordings.Load(take.Id).Tracks.Single(t => t.GroupId == "impact").Name == "我的撞击音效" && workspace.Preset!.Groups["impact"].Name == "我的撞击音效",
        "listening-page rename persists the recording label and updates the matching preset");
    Check(beforeRename.SequenceEqual(workspace.Recordings.Clip(take.Id, "impact", 0, .2, 1)) && !workspace.Preset!.Groups["impact"].Enabled,
        "renaming never changes audio bytes or feedback selection");
    var foreign = workspace.Recordings.Load(take.Id); foreign.PackId = "another.game"; workspace.Recordings.SaveMetadata(foreign);
    workspace.RenameTrack(take.Id, "impact", "另一游戏的音轨");
    Check(workspace.Preset!.Groups["impact"].Name == "我的撞击音效", "historical recording rename never edits another game's preset");
}
var render = new RenderProgress(0, 48000);
render.Observe(480, 480); render.Observe(480, 48000 * 12);
Check(render.Waiting, "paused game render clock is reported as waiting");
long pauseGap = render.Observe(960, 48000 * 13);
Check(!render.Waiting && pauseGap > 48000 * 12 && render.Observe(1440, 48000 * 13 + 480) == 0,
    "game render resumption clears waiting and preserves the pause in timestamps");
await using (var client = new AdapterClient())
{
    var received = new List<AudioPacket>(); object gate = new();
    client.Audio += p => { lock (gate) received.Add(p); };
    await client.Launch(pack); await client.Connect(0, null);
    await Task.Delay(450);
    Check(client.Groups.Count == 5 && client.Info!.Capabilities.IndependentPcm, "protocol handshake and hierarchy");
    AudioPacket[] data; lock (gate) data = received.ToArray();
    Check(data.Select(p => p.GroupIndex).Distinct().Count() == 4, "four separate PCM streams");
    Check(data.GroupBy(p => p.GroupIndex).Select(g => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(g.First().Pcm))).Distinct().Count() == 4, "independent stream contents are different");
    var original = presets.Load(pack, "author:author-start", client.Groups); original.Groups["impact"] = new("我的冲击", false);
    var user = presets.Save(pack, original, "我的配置", false); var second = presets.Save(pack, user, "第二个配置", true);
    Check(user.Id != second.Id && presets.List(pack).Count == 3, "multiple user presets preserve author original");
    var restored = presets.Restore(pack, user, client.Groups);
    Check(restored.Groups["impact"].Enabled && restored.Groups["impact"].Name == null && restored.Id == user.Id, "restore original without replacing user identity");
    using var recorder = new RecordingStore(scratch);
    recorder.Start(pack, client.Groups, original);
    void Record(AudioPacket p) => recorder.Accept(client.Groups[(int)p.GroupIndex], p);
    client.Audio += Record; await Task.Delay(700); client.Audio -= Record;
    var take = recorder.Stop()!;
    var impact = recorder.Clip(take.Id, "impact", 0, Math.Min(.5,take.Duration), 1);
    var music = recorder.Clip(take.Id, "music", 0, Math.Min(.5,take.Duration), 1);
    Check(impact.Length > 44 && !impact.SequenceEqual(music), "per-group WAV capture and selected-track clipping");
    Check(take.Tracks.Count == 4 && take.Tracks.All(t => t.Frames > 0), "session alignment and silence padding");
    using var limited = new RecordingStore(Path.Combine(scratch, "limit-test")); limited.Start(pack, client.Groups, original);
    long boundary = limited.OriginTick + Stopwatch.Frequency * 60 - Stopwatch.Frequency / 200;
    var impactGroup = client.Groups.Single(g => g.Id == "impact");
    limited.Accept(impactGroup, new(1, 48000, 2, boundary, 0, new byte[480 * 4]));
    limited.Accept(impactGroup, new(1, 48000, 2, boundary + Stopwatch.Frequency, 1, new byte[480 * 4]));
    var capped = limited.Stop()!; Check(capped.Duration == 60 && capped.Tracks.All(t => t.Frames == 60 * 48000), "one-minute cap truncates boundary packets and ignores later audio");
    await client.Call<AdapterStatus>("StopCapture");
    Check(!(await client.Call<AdapterStatus>("GetStatus")).Capturing, "capture stop keeps control responsive");
    await client.Call<AdapterStatus>("StartCapture", new CaptureRequest(client.Groups.Where(g => g.HasPcm).Select(g => g.Id).ToArray()));
    var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); client.Failed += message => failed.TrySetResult(message);
    using var process = Process.GetProcessById(client.ProcessId); process.Kill(true);
    await failed.Task.WaitAsync(TimeSpan.FromSeconds(3)); Check(!client.IsAlive, "adapter crash is isolated and detected");
}
string malformed = Path.Combine(scratch,"traversal.gpack");
string? previousRealtime = Environment.GetEnvironmentVariable("APEX_AUDIO_MOCK_REALTIME");
Environment.SetEnvironmentVariable("APEX_AUDIO_MOCK_REALTIME", "1");
try
{
    await using var client = new AdapterClient();
    var audioGroups = new HashSet<uint>(); object audioGate = new();
    client.Audio += p => { lock (audioGate) audioGroups.Add(p.GroupIndex); };
    await client.Launch(pack); await client.Connect(0, null);
    Check(client.Info!.Capabilities.RealtimeMix, "optional realtime mix capability uses the same adapter protocol");
    var source = await client.Call<FeedbackSource>("GetFeedbackSource");
    using var map = MemoryMappedFile.OpenExisting(source.MappingName);
    using var view = map.CreateViewAccessor();
    float MixPeak()
    {
        int writes = view.ReadInt32(80); if (writes == 0) return 0;
        long address = 104 + (writes - 1) % 64 * 16408;
        float peak = 0; for (int i = 0; i < 480; i++) peak = Math.Max(peak, Math.Abs(view.ReadSingle(address + 24 + i * 4)));
        return peak;
    }
    await client.Call<AdapterStatus>("SetFeedbackGroups", new CaptureRequest(new[] { "music" })); await Task.Delay(120);
    Check(source.Format == "apex-f32-planar-ring-v1" && source.ProducerProcessId == client.ProcessId && MixPeak() > .05f,
        "adapter-defined mapping publishes selected audio without a game or engine name in the host");
    await client.Call<AdapterStatus>("SetFeedbackGroups", new CaptureRequest(Array.Empty<string>())); await Task.Delay(120);
    Check(MixPeak() == 0, "disabling every group silences the direct mix without reconnecting");
    lock (audioGate) Check(audioGroups.Count == 4, "direct mix selection preserves all independent recording tracks");
}
finally { Environment.SetEnvironmentVariable("APEX_AUDIO_MOCK_REALTIME", previousRealtime); }
using (var zip = ZipFile.Open(malformed,ZipArchiveMode.Create)) { using var text = new StreamWriter(zip.CreateEntry("../escape.txt").Open()); text.Write("test"); }
bool rejected = false; try { store.Install(malformed); } catch (InvalidDataException) { rejected = true; }
Check(rejected && !File.Exists(Path.Combine(scratch,"escape.txt")), "archive path traversal rejected");
string manifestFile = Path.Combine(pack.Directory,"manifest.json"); var manifest = PackageStore.ReadManifest(manifestFile); manifest.ProtocolVersion = 999; PresetStore.AtomicWrite(manifestFile,manifest);
rejected = false; try { PackageStore.Validate(pack.Directory,manifest); } catch (InvalidDataException) { rejected = true; }
Check(rejected, "incompatible protocol rejected before executable starts");
manifest.ProtocolVersion = 1; PresetStore.AtomicWrite(manifestFile,manifest);
File.AppendAllText(Path.Combine(pack.Directory,"presets","author.json")," ");
rejected = false; try { PackageStore.Validate(pack.Directory,manifest); } catch (InvalidDataException) { rejected = true; }
Check(rejected, "modified package content rejected");
store.Uninstall(pack.Manifest.Id);
Check(store.List().Count == 0 && Directory.EnumerateFiles(Path.Combine(scratch,"presets",pack.Manifest.Id),"*.json").Count() == 2, "uninstall retains user data");
File.WriteAllText(Path.Combine(root,"artifacts","acceptance.json"),JsonSerializer.Serialize(new { success=true, checks, testedAt=DateTime.Now },Wire.Json));
