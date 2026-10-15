using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using ApexAudio.Contracts;
using ApexAudio.XHaptics;

static class FeedbackChecks
{
    // Read the real host's output ring without loading the native bridge,
    // connecting X-Haptics, or sending anything to the controller.
    public static async Task Run(Action<bool, string> check)
    {
        foreach (bool late in new[] { false, true })
        {
            await using var output = new FeedbackOutput();
            using var map = MemoryMappedFile.OpenExisting($"Local\\ApexGroupsInput-{Environment.ProcessId}");
            using var view = map.CreateViewAccessor();
            long origin = view.ReadInt64(64), frequency = Stopwatch.Frequency;
            byte[] Pcm(short value)
            {
                var data = new byte[480 * 4];
                for (int i = 0; i < data.Length; i += 2) BitConverter.GetBytes(value).CopyTo(data, i);
                return data;
            }
            byte[] left = Pcm(8192), right = Pcm(4096);
            await Task.Delay(100);
            long first = Stopwatch.GetTimestamp() - (late ? frequency * 60 / 1000 : 0);
            const int packets = 40;
            void Send(int index)
            {
                long tick = first + index * frequency / 100;
                output.Accept(new AudioPacket(0, 48000, 2, tick, index * 2, left));
                output.Accept(new AudioPacket(1, 48000, 2, tick, index * 2 + 1, right));
            }
            int read = view.ReadInt32(80), sent = 0, nonzero = 0;
            double sum = 0; bool aligned = true;
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 900)
            {
                while (sent < packets && timer.ElapsedMilliseconds >= sent * 10) Send(sent++);
                int written = view.ReadInt32(80);
                check(written - read <= 64, "feedback observer stays within the output ring");
                while (read < written)
                {
                    long address = 104 + read++ % 64 * 16408;
                    for (int i = 0; i < 480; i++)
                    {
                        float l = view.ReadSingle(address + 24 + i * 4);
                        float r = view.ReadSingle(address + 24 + 2048 * 4 + i * 4);
                        sum += l;
                        if (l != 0) { nonzero++; aligned &= l == .375f && r == l; }
                    }
                }
                await Task.Delay(1);
            }
            check(nonzero == packets * 480 && Math.Abs(sum - packets * 480 * .375) < .001,
                late ? "late feedback audio keeps every sample" : "timer coalescing keeps every feedback sample");
            check(aligned, "feedback groups remain aligned at unchanged amplitude");
            if (!late)
            {
                output.Accept(new AudioPacket(0, 48000, 2, Stopwatch.GetTimestamp() + frequency / 5, 100, left));
                output.Clear(); read = view.ReadInt32(80); await Task.Delay(300);
                bool silent = true;
                while (read < view.ReadInt32(80))
                {
                    long address = 104 + read++ % 64 * 16408;
                    for (int i = 0; i < 480; i++) silent &= view.ReadSingle(address + 24 + i * 4) == 0;
                }
                check(silent, "changing feedback selection clears queued audio");
            }
        }
        await using (var output = new FeedbackOutput())
        {
            using var map = MemoryMappedFile.OpenExisting($"Local\\ApexGroupsInput-{Environment.ProcessId}");
            using var view = map.CreateViewAccessor();
            byte[] pulse = new byte[480 * 4];
            for (int i = 0; i < pulse.Length; i += 2) BitConverter.GetBytes((short)16384).CopyTo(pulse, i);
            await Task.Delay(100);
            output.Accept(new AudioPacket(0, 48000, 2, Stopwatch.GetTimestamp() - Stopwatch.Frequency / 2, 0, new byte[480 * 4]));
            await Task.Delay(5);
            int read = view.ReadInt32(80), receivedFrames = 0;
            var elapsed = Stopwatch.StartNew();
            output.Accept(new AudioPacket(0, 48000, 2, Stopwatch.GetTimestamp(), 1, pulse));
            while (elapsed.ElapsedMilliseconds < 150)
            {
                int written = view.ReadInt32(80);
                while (read < written)
                {
                    long address = 104 + read++ % 64 * 16408;
                    for (int i = 0; i < 480; i++) if (view.ReadSingle(address + 24 + i * 4) == .5f) receivedFrames++;
                }
                if (receivedFrames == 480) break;
                await Task.Delay(1);
            }
            check(receivedFrames == 480 && elapsed.ElapsedMilliseconds < 120, "a slow packet does not add persistent latency to following sounds");
        }
    }
}
