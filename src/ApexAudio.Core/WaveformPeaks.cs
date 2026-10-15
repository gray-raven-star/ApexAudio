using System.Buffers.Binary;

namespace ApexAudio.Core;

/// <summary>10 ms peak bins for the protocol's 48 kHz stereo PCM16 recordings.</summary>
public static class WaveformPeaks
{
    public const int Version = 1, FramesPerBin = 480, BytesPerFrame = 4;

    public static void Add(List<float> peaks, ReadOnlySpan<byte> pcm, long firstFrame)
    {
        if (firstFrame < 0 || pcm.Length % BytesPerFrame != 0) throw new ArgumentException("Invalid waveform frame range.");
        int frame = 0, frames = pcm.Length / BytesPerFrame;
        while (frame < frames)
        {
            int bin = checked((int)((firstFrame + frame) / FramesPerBin));
            int count = Math.Min(frames - frame, FramesPerBin - (int)((firstFrame + frame) % FramesPerBin));
            int peak = 0;
            var block = pcm.Slice(frame * BytesPerFrame, count * BytesPerFrame);
            for (int i = 0; i < block.Length; i += 2) peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(block[i..])));
            while (peaks.Count <= bin) peaks.Add(0);
            peaks[bin] = Math.Max(peaks[bin], peak / 32768f);
            frame += count;
        }
    }

    public static List<float> FromRecording(Stream input)
    {
        Span<byte> header = stackalloc byte[44]; input.ReadExactly(header);
        if (!header[..4].SequenceEqual("RIFF"u8) || !header.Slice(8, 8).SequenceEqual("WAVEfmt "u8)
            || BinaryPrimitives.ReadInt32LittleEndian(header[16..]) != 16
            || BinaryPrimitives.ReadInt16LittleEndian(header[20..]) != 1
            || BinaryPrimitives.ReadInt16LittleEndian(header[22..]) != 2
            || BinaryPrimitives.ReadInt32LittleEndian(header[24..]) != 48000
            || BinaryPrimitives.ReadInt16LittleEndian(header[34..]) != 16
            || !header.Slice(36, 4).SequenceEqual("data"u8)) throw new InvalidDataException("录音 WAV 格式不受支持，未修改原文件。");
        int bytes = BinaryPrimitives.ReadInt32LittleEndian(header[40..]);
        if (bytes < 0 || bytes > RecordingStore.MaxSeconds * 192000 || bytes % BytesPerFrame != 0) throw new InvalidDataException("录音长度无效。");
        var peaks = new List<float>(); byte[] buffer = new byte[FramesPerBin * BytesPerFrame * 16]; long frame = 0;
        while (bytes > 0)
        {
            int count = Math.Min(bytes, buffer.Length); input.ReadExactly(buffer.AsSpan(0, count));
            Add(peaks, buffer.AsSpan(0, count), frame); frame += count / BytesPerFrame; bytes -= count;
        }
        return peaks;
    }
}
