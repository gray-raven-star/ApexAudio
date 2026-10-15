using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using ApexAudio.Contracts;

// Acceptance fixture for the public ring format, enabled only by the test runner.
sealed class RealtimeMix : IDisposable
{
    readonly MemoryMappedFile map;
    readonly MemoryMappedViewAccessor view;
    readonly object gate = new();
    HashSet<string> selected = new();
    int writes;
    public FeedbackSource Source { get; } = new("apex-f32-planar-ring-v1", $"Local\\IndependentAdapterMix-{Environment.ProcessId}", Environment.ProcessId);
    public RealtimeMix()
    {
        map = MemoryMappedFile.CreateNew(Source.MappingName, 3150488); view = map.CreateViewAccessor();
        view.Write(0, 0x37504341u); view.Write(4, 1); view.Write(8, Environment.ProcessId); view.Write(16, 1);
        view.Write(56, Stopwatch.Frequency); view.Write(96, 48000); view.Write(100, 0x3102);
    }
    public void Select(string[] ids) { lock (gate) selected = ids.ToHashSet(); }
    public void Publish(IReadOnlyList<(string Id, AudioPacket Packet)> packets)
    {
        float[] mix = new float[960];
        lock (gate)
            foreach (var (id, packet) in packets.Where(p => selected.Contains(p.Id)))
                for (int i = 0; i < 480; i++)
                { mix[i] += BitConverter.ToInt16(packet.Pcm, i * 4) / 32768f; mix[480 + i] += BitConverter.ToInt16(packet.Pcm, i * 4 + 2) / 32768f; }
        long address = 104 + writes % 64 * 16408, now = Stopwatch.GetTimestamp(); int seq = (writes + 1) * 2;
        view.Write(address, seq - 1); view.Write(address + 4, 480); view.Write(address + 8, 2); view.Write(address + 16, now);
        view.WriteArray(address + 24, mix, 0, 480); view.WriteArray(address + 24 + 2048 * 4, mix, 480, 480);
        Thread.MemoryBarrier(); view.Write(address, seq); view.Write(80, ++writes); view.Write(72, now);
    }
    public void Dispose() { view.Write(16, 0); view.Dispose(); map.Dispose(); }
}
