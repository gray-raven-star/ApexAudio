using System.Diagnostics;
using System.Security.Cryptography;
using ApexAudio.Contracts;
using ApexAudio.Core;

static class WaveformChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        static byte[] Pcm(params (int Frames, short Sample)[] parts)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            foreach (var (frames, sample) in parts) for (int i = 0; i < frames * 2; i++) writer.Write(sample);
            return stream.ToArray();
        }
        var bins = new List<float>();
        WaveformPeaks.Add(bins, Pcm((240, 8192), (480, 16384), (304, short.MinValue)), 240);
        check(bins.Count == 3 && bins.SequenceEqual(new[] { .25f, .5f, 1f }), "waveform splits a packet across every 10 ms bin, including partial bins and negative full scale");
        WaveformPeaks.Add(bins, Pcm((10, 4096)), 0);
        check(bins[0] == .25f, "overlapping peak contributions retain the larger value");

        string data = Path.Combine(root, "waveform");
        var group = new AudioGroup("test", "test", null, "其他", true);
        using var recorder = new RecordingStore(data);
        recorder.Start(new InstalledPack(root, new PackManifest { Id = "test.waveform", Name = "Test" }), new[] { group }, new SoundPreset());
        long origin = recorder.OriginTick;
        var packet = Pcm((240, short.MaxValue), (480, 8192), (480, 16384));
        recorder.Accept(group, new(0, 48000, 2, origin - Stopwatch.Frequency / 200, 0, packet));
        var take = recorder.Stop()!;
        check(take.Tracks[0].Peaks.Take(2).SequenceEqual(new[] { .25f, .5f }), "negative-time trimming excludes discarded samples from waveform peaks");
        string folder = Path.Combine(data, "recordings", take.Id), wav = Path.Combine(folder, take.Tracks[0].File);
        string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(wav)));
        take.WaveformVersion = 0; take.Tracks[0].Peaks = new() { 0 }; recorder.SaveMetadata(take);
        var repaired = recorder.Load(take.Id);
        check(repaired.WaveformVersion == WaveformPeaks.Version && repaired.Tracks[0].Peaks.Take(2).SequenceEqual(new[] { .25f, .5f }), "legacy recording overview is rebuilt from PCM on load");
        check(before == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(wav))), "overview repair preserves original WAV bytes");
        byte[] metadata = File.ReadAllBytes(Path.Combine(folder, "session.json")); recorder.Load(take.Id);
        check(metadata.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "session.json"))), "a repaired overview is not rewritten on subsequent loads");

        using var limited = new RecordingStore(Path.Combine(data, "boundary"));
        limited.Start(new InstalledPack(root, new PackManifest { Id = "test.waveform", Name = "Test" }), new[] { group }, new SoundPreset());
        limited.Accept(group, new(0, 48000, 2, limited.OriginTick + Stopwatch.Frequency * 60 - Stopwatch.Frequency / 200, 0, Pcm((240, 8192), (240, short.MaxValue))));
        var capped = limited.Stop()!;
        check(capped.Tracks[0].Peaks.Count == 6000 && capped.Tracks[0].Peaks[^1] == .25f, "one-minute trimming excludes samples after the limit from the final peak bin");
    }
}
