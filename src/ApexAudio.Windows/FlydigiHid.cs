using System.Runtime.InteropServices;
namespace ApexAudio.Windows;
public sealed partial class FlydigiButtons
{
public static List<(string Path, int Length, ushort UsagePage, ushort Usage)> Devices()
    {
        var result = new List<(string, int, ushort, ushort)>();
        HidD_GetHidGuid(out var guid); var info = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
        if (info == new IntPtr(-1)) return result;
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref guid, index, ref data)) break;
                SetupDiGetDeviceInterfaceDetail(info, ref data, IntPtr.Zero, 0, out uint length, IntPtr.Zero);
                if (length < 8 || length > 8192) continue;
                var buffer = Marshal.AllocHGlobal((int)length);
                try
                {
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(info, ref data, buffer, length, out _, IntPtr.Zero)) continue;
                    var path = Marshal.PtrToStringUni(IntPtr.Add(buffer, 4)) ?? "";
                    if (!path.Contains("vid_37d7&pid_2502", StringComparison.OrdinalIgnoreCase) || (!path.Contains("mi_02", StringComparison.OrdinalIgnoreCase) && !path.Contains("mi_03", StringComparison.OrdinalIgnoreCase))) continue;
                    using var handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out var prep)) continue;
                    var caps = Marshal.AllocHGlobal(64);
                    try { if (HidP_GetCaps(prep, caps) >= 0) result.Add((path, (ushort)Marshal.ReadInt16(caps, 4), (ushort)Marshal.ReadInt16(caps, 2), (ushort)Marshal.ReadInt16(caps, 0))); }
                    finally { Marshal.FreeHGlobal(caps); HidD_FreePreparsedData(prep); }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(info); }
        return result;
    }
public static ulong DecodeNative(ReadOnlySpan<byte> report)
    {
        if (report.Length < 31) return 0;
        int offset = report[0] == 0x5a ? 0 : 1;
        if (report.Length < offset + 31 || report[offset] != 0x5a || report[offset + 1] != 0xa5 || report[offset + 2] != 0xef) return 0;
        return ((ulong)report[offset + 13] << 48) | ((ulong)report[offset + 14] << 56);
    }
int OutputLength(string path)
    {
        using var handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out var prep)) throw new IOException("无法打开目标手柄的命令接口。");
        var caps = Marshal.AllocHGlobal(64);
        try { if (HidP_GetCaps(prep, caps) < 0) throw new IOException("目标手柄接口描述无效。"); return (ushort)Marshal.ReadInt16(caps, 6); }
        finally { Marshal.FreeHGlobal(caps); HidD_FreePreparsedData(prep); }
    }
void Command(string path, byte command, byte[] payload)
    {
        int length = outputLengths.GetOrAdd(path, OutputLength);
        if (length < 33 || length > 65) throw new IOException("原生扩展键命令报文长度不兼容。");
        var bytes = new byte[length]; bytes[1] = 0x5a; bytes[2] = 0xa5; bytes[3] = command; bytes[4] = (byte)(payload.Length + 2); payload.CopyTo(bytes, 5);
        for (int index = 3; index < 32; index++) bytes[32] = unchecked((byte)(bytes[32] + bytes[index]));
        using var handle = CreateFile(path, 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid || !WriteFile(handle, bytes, (uint)bytes.Length, out uint written, IntPtr.Zero) || written != bytes.Length) throw new IOException("原生扩展键命令写入失败：" + Marshal.GetLastWin32Error());
    }
}
