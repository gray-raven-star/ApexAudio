using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ApexAudio.Contracts;

namespace ApexAudio.Core;

public sealed class RecordedTrack
{
    public string GroupId { get; set; } = "";
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public List<float> Peaks { get; set; } = new();
    public long Frames { get; set; }
}
public sealed class RecordingSession
{
    public int SchemaVersion { get; set; } = 1;
    public int WaveformVersion { get; set; }
    public string Id { get; set; } = "";
    public string PackId { get; set; } = "";
    public string PackVersion { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
    public long OriginTick { get; set; }
    public long Frequency { get; set; } = Stopwatch.Frequency;
    public int SampleRate { get; set; } = 48000;
    public int Channels { get; set; } = 2;
    public double Duration { get; set; }
    public long MissingPackets { get; set; }
    public string Note { get; set; } = "";
    public List<RecordedTrack> Tracks { get; set; } = new();
    public JsonElement? Haptics { get; set; }
}

public sealed class RecordingStore : IDisposable
{
    public const int MaxSeconds = 60;
    readonly string root; readonly object gate = new();
    RecordingSession? session; string? directory;
    readonly Dictionary<string, FileStream> files = new();
    long lastSequence = -1;
    public bool IsRecording { get { lock (gate) return session != null; } }
    public RecordingStore(string dataRoot) { root = Path.Combine(dataRoot, "recordings"); Directory.CreateDirectory(root); }
    public void Start(InstalledPack pack, IReadOnlyList<AudioGroup> groups, SoundPreset preset)
    {
        lock (gate)
        {
            if (session != null) throw new InvalidOperationException("正在录制。");
            string id = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
            directory = Path.Combine(root, id); Directory.CreateDirectory(directory);
            session = new() { Id = id, WaveformVersion = WaveformPeaks.Version, PackId = pack.Manifest.Id, PackVersion = pack.Manifest.Version, Name = pack.Manifest.Name, OriginTick = Stopwatch.GetTimestamp() };
            files.Clear(); lastSequence = -1;
            foreach (var g in groups.Where(g => g.HasPcm))
            {
                string file = "track-" + session.Tracks.Count + ".wav";
                session.Tracks.Add(new() { GroupId = g.Id, Name = preset.Groups.GetValueOrDefault(g.Id)?.Name ?? g.Name, File = file });
            }
        }
    }
    public void Accept(AudioGroup group, AudioPacket packet)
    {
        lock (gate)
        {
            if (session == null || directory == null) return;
            if (lastSequence >= 0 && packet.Sequence > lastSequence + 1) session.MissingPackets += packet.Sequence - lastSequence - 1;
            lastSequence = packet.Sequence;
            long at = (long)Math.Round((packet.Tick - session.OriginTick) * 48000.0 / Stopwatch.Frequency);
            if (at + packet.Frames <= 0) return;
            if (at >= 48000L * MaxSeconds) return;
            var track = session.Tracks.SingleOrDefault(t => t.GroupId == group.Id); if (track == null) return;
            if (!files.TryGetValue(group.Id, out var stream))
            {
                stream = new(Path.Combine(directory, track.File), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read); stream.SetLength(44); files.Add(group.Id, stream);
            }
            int skip = at < 0 ? (int)-at : 0; at = Math.Max(at, 0);
            int count = (int)Math.Min(packet.Frames - skip, 48000L * MaxSeconds - at);
            stream.Position = 44 + at * 4; stream.Write(packet.Pcm, skip * 4, count * 4);
            track.Frames = Math.Max(track.Frames, at + count);
            WaveformPeaks.Add(track.Peaks, packet.Pcm.AsSpan(skip * 4, count * 4), at);
            session.Duration = Math.Max(session.Duration, track.Frames / 48000.0);
        }
    }
    public long OriginTick { get { lock (gate) return session?.OriginTick ?? 0; } }
    public int Frames { get { lock (gate) return (int)Math.Round((session?.Duration ?? 0) * 48000); } }
    public RecordingSession? Stop(JsonElement? haptics = null, string note = "")
    {
        lock (gate)
        {
            if (session == null || directory == null) return null;
            var result = session; session = null;
            result.Duration = Math.Min(MaxSeconds, Math.Max(result.Duration, (Stopwatch.GetTimestamp() - result.OriginTick) / (double)Stopwatch.Frequency));
            result.Haptics = haptics; result.Note = note;
            long totalFrames = (long)Math.Round(result.Duration * 48000);
            foreach (var track in result.Tracks)
            {
                if (!files.TryGetValue(track.GroupId, out var stream)) { stream = new(Path.Combine(directory, track.File), FileMode.CreateNew, FileAccess.ReadWrite); files.Add(track.GroupId, stream); }
                stream.SetLength(44 + totalFrames * 4); stream.Position = 0; WriteHeader(stream, checked((int)(totalFrames * 4))); stream.Dispose();
                track.Frames = totalFrames;
                int bins = (int)((totalFrames + WaveformPeaks.FramesPerBin - 1) / WaveformPeaks.FramesPerBin);
                while (track.Peaks.Count < bins) track.Peaks.Add(0);
            }
            files.Clear(); PresetStore.AtomicWrite(Path.Combine(directory, "session.json"), result); return result;
        }
    }
    public static void WriteHeader(Stream stream, int bytes)
    {
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((ushort)1); writer.Write((ushort)2); writer.Write(48000); writer.Write(192000); writer.Write((ushort)4); writer.Write((ushort)16); writer.Write("data"u8); writer.Write(bytes);
    }
    public List<RecordingSession> List() => Directory.EnumerateFiles(root, "session.json", SearchOption.AllDirectories)
        .Select(f => JsonSerializer.Deserialize<RecordingSession>(File.ReadAllText(f), Wire.Json)!).OrderByDescending(r => r.Created).ToList();
    public RecordingSession Load(string id)
    {
        string folder = PackageStore.SafePath(root, id);
        var take = JsonSerializer.Deserialize<RecordingSession>(File.ReadAllText(Path.Combine(folder, "session.json")), Wire.Json)!;
        if (take.Id != id) throw new InvalidDataException("录音编号不匹配。");
        if (take.WaveformVersion < WaveformPeaks.Version)
        {
            // Repair only the derived overview. Original audio and haptic samples stay untouched.
            var rebuilt = take.Tracks.Select(track =>
            {
                using var input = File.OpenRead(PackageStore.SafePath(folder, track.File));
                return WaveformPeaks.FromRecording(input);
            }).ToArray();
            for (int i = 0; i < take.Tracks.Count; i++) take.Tracks[i].Peaks = rebuilt[i];
            take.WaveformVersion = WaveformPeaks.Version; SaveMetadata(take);
        }
        return take;
    }
    public void SaveMetadata(RecordingSession take) => PresetStore.AtomicWrite(Path.Combine(PackageStore.SafePath(root, take.Id), "session.json"), take);
    public byte[] Clip(string sessionId, string groupId, double start, double end, float gain)
    {
        var take = Load(sessionId); var track = take.Tracks.Single(t => t.GroupId == groupId);
        string folder = PackageStore.SafePath(root, sessionId); string file = PackageStore.SafePath(folder, track.File);
        long first = (long)(Math.Clamp(start, 0, take.Duration) * 48000), last = (long)(Math.Clamp(end, start, take.Duration) * 48000);
        int length = checked((int)((last - first) * 4)); if (length <= 0 || length > 60 * 192000) throw new IOException("请选择 0～60 秒的试听区间。");
        byte[] pcm = new byte[length]; using var input = File.OpenRead(file); input.Position = 44 + first * 4; input.ReadExactly(pcm);
        for (int i = 0; i < pcm.Length; i += 2) BitConverter.TryWriteBytes(pcm.AsSpan(i), (short)Math.Clamp((int)(BitConverter.ToInt16(pcm, i) * Math.Clamp(gain, 0, 2)), short.MinValue, short.MaxValue));
        using var output = new MemoryStream(); WriteHeader(output, length); output.Write(pcm); return output.ToArray();
    }
    public void Dispose() => Stop(note: "应用退出时结束录制。");
}
