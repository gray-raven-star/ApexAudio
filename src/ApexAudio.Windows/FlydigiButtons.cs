using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ApexAudio.Windows;

public sealed partial class FlydigiButtons : IAsyncDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct InterfaceData { public int Size; public Guid Guid; public int Flags; public nint Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct Gamepad { public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }
    [StructLayout(LayoutKind.Sequential)] struct State { public uint Packet; public Gamepad Pad; }
    [DllImport("xinput1_4.dll")] static extern uint XInputGetState(uint index, out State state);
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern nint SetupDiGetClassDevs(ref Guid guid, nint enumerator, nint hwnd, uint flags);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(nint info, nint device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(nint info, ref InterfaceData data, nint detail, uint size, out uint required, nint device);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(nint info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out nint data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(nint data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(nint data, nint caps);
    [DllImport("hid.dll")] static extern bool HidD_SetNumInputBuffers(SafeFileHandle handle, uint count);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(SafeFileHandle handle, byte[] buffer, uint length, out uint written, nint overlapped);
    readonly CancellationTokenSource stop = new();
    readonly ConcurrentDictionary<string, (ulong Mask, long Time)> states = new();
    readonly ConcurrentDictionary<string, Task> readers = new();
    readonly ConcurrentDictionary<string, int> outputLengths = new();
    readonly Task scanner;
    string? ownedTransport;
    public bool Enabled { get; set; }
    public string Status { get; private set; } = "等待手柄输入";
    public FlydigiButtons() => scanner = Task.Run(Scan);
    async Task Scan()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (Enabled)
                {
                    try
                    {
                        var devices = Devices();
                        foreach (var d in devices.Where(d => d.Length is >= 32 and <= 512))
                            if (!readers.TryGetValue(d.Path, out var worker) || worker.IsCompleted) readers[d.Path] = Read(d.Path, d.Length);
                        if (!states.Values.Any(s => Environment.TickCount64 - s.Time < 500))
                        {
                            var command = devices.Where(d => d.UsagePage == 0xffa0 && d.Path.Contains("mi_02", StringComparison.OrdinalIgnoreCase)).ToArray();
                            if (command.Length == 1) { Command(command[0].Path, 0x11, new byte[] { 255, 1, 255, 255, 255 }); ownedTransport = command[0].Path; }
                        }
                    }
                    catch (IOException e) { Status = e.Message; }
                }
                await Task.Delay(1500, stop.Token);
            }
        }
        catch (OperationCanceledException) { }
    }
    async Task Read(string path, int length)
    {
        try
        {
            using var handle = CreateFile(path, 0x80000000, 3, 0, 3, 0x40000000, 0);
            if (handle.IsInvalid) return;
            HidD_SetNumInputBuffers(handle, 64);
            using var stream = new FileStream(handle, FileAccess.Read, length, true); byte[] report = new byte[length];
            while (!stop.IsCancellationRequested)
            {
                int count = await stream.ReadAsync(report, stop.Token); if (count == 0) break;
                int offset = report[0] == 0x5a ? 0 : 1;
                if (count >= offset + 31 && report[offset] == 0x5a && report[offset + 1] == 0xa5 && report[offset + 2] == 0xef)
                { states[path] = (DecodeNative(report.AsSpan(0, count)), Environment.TickCount64); Status = "手柄扩展键已连接"; }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException e) { Status = e.Message; }
        finally { states.TryRemove(path, out _); }
    }
    public ulong Poll()
    {
        if (!Enabled) return 0;
        ulong mask = 0;
        for (uint i = 0; i < 4; i++) if (XInputGetState(i, out var state) == 0) mask |= state.Pad.Buttons;
        foreach (var state in states.Values) if (Environment.TickCount64 - state.Time < 300) mask |= state.Mask;
        return mask;
    }
    public static string Describe(ulong mask)
    {
        var names = new Dictionary<ulong, string> { [1]="↑",[2]="↓",[4]="←",[8]="→",[16]="START",[32]="BACK",[64]="L3",[128]="R3",[256]="LB",[512]="RB",[4096]="A",[8192]="B",[16384]="X",[32768]="Y",[1UL<<50]="M1",[1UL<<51]="M2",[1UL<<52]="M3",[1UL<<53]="M4",[1UL<<54]="LM",[1UL<<55]="RM",[1UL<<56]="截图键",[1UL<<59]="HOME" };
        ulong known = 0; var labels = new List<string>(); foreach (var (bit, name) in names) if ((mask & bit) != 0) { labels.Add(name); known |= bit; }
        if ((mask & ~known) != 0) labels.Add("扩展键 " + (mask & ~known).ToString("X")); return string.Join(" + ", labels);
    }
    public async ValueTask DisposeAsync()
    {
        Enabled = false; stop.Cancel(); await scanner;
        if (ownedTransport != null) { try { Command(ownedTransport, 0x11, new byte[] { 255, 0, 255, 255, 255 }); } catch (IOException) { } }
        await Task.WhenAll(readers.Values); stop.Dispose();
    }
}
