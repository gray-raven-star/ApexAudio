using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

return Probe.Run(args);

static unsafe class Probe
{
    static T Fn<T>(nint library, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static int Run(string[] args)
    {
        if (args.Contains("--help")) { Console.WriteLine("ApexAudio.FmodProbe --core FMOD.dll --studio FMODStudio.dll --header-version 0x00020306 [--bank FILE repeated] --output REPORT.json\nOwn-process NOSOUND metadata and SDK-110 DSP construction/attachment only; no game connection or audio playback."); return 0; }
        string Value(string key) { int at = Array.IndexOf(args, key); return at >= 0 && at + 1 < args.Length ? args[at + 1] : throw new ArgumentException("Required: " + key); }
        var steps = new List<object>(); var cleanup = new List<object>(); var banks = new List<object>();
        var report = new Dictionary<string, object?> { ["reportVersion"] = 1, ["mode"] = "own-process-nosound", ["gameConnectionAttempted"] = false, ["pluginSdkVersion"] = 110, ["steps"] = steps, ["banks"] = banks, ["cleanup"] = cleanup };
        string phase = "arguments"; nint studio = 0, core = 0, dsp = 0, group = 0, c = 0, s = 0;
        bool attached = false, suspended = false, clean = true;
        Description description = default;
        void Check(string stage, int code) { phase = stage; steps.Add(new { stage, result = code, passed = code == 0 }); Console.Error.WriteLine(stage + ": " + code); if (code != 0) throw new IOException(stage + " returned FMOD_RESULT " + code); }
        void Clean(string operation, Func<int> action) { try { int result = action(); cleanup.Add(new { operation, result }); clean &= result == 0; } catch (Exception e) { clean = false; cleanup.Add(new { operation, error = e.Message }); } }
        try
        {
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new PlatformNotSupportedException("Windows x64 required.");
            string corePath = Path.GetFullPath(Value("--core")), studioPath = Path.GetFullPath(Value("--studio"));
            string text = Value("--header-version"); uint header = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToUInt32(text[2..], 16) : uint.Parse(text);
            _ = Value("--output");
            if ((header >> 8) is not (0x202 or 0x203)) throw new NotSupportedException("Only verified 2.02/2.03 API families are supported.");
            var fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(corePath);
            // Some FMOD files encode fixed parts as 0.2.3.6 but display ProductVersion 2.3.6.
            var family = System.Text.RegularExpressions.Regex.Match(fileVersion.ProductVersion ?? fileVersion.FileVersion ?? "", @"^(\d+)\.(\d+)\.");
            if (!family.Success || uint.Parse(family.Groups[1].Value) != (header >> 16) || uint.Parse(family.Groups[2].Value) != ((header >> 8) & 255)) throw new IOException("Runtime file version and requested API family differ or are unknown; verify the matching SDK before calling native functions.");
            report["headerVersion"] = "0x" + header.ToString("X8");
            report["libraries"] = new[] { new { path = corePath, sha256 = Hash(corePath) }, new { path = studioPath, sha256 = Hash(studioPath) } };
            phase = "load-libraries"; c = NativeLibrary.Load(corePath); s = NativeLibrary.Load(studioPath);
            Check("studio-create", Fn<Create>(s, "FMOD_Studio_System_Create")(out studio, header));
            Check("get-core", Fn<GetPointer>(s, "FMOD_Studio_System_GetCoreSystem")(studio, out core));
            uint version, buildNumber = 0;
            // 2.03 adds a third output pointer. Omitting it can corrupt caller memory.
            if ((header >> 8) == 0x203) Check("runtime-version", Fn<GetVersion203>(c, "FMOD_System_GetVersion")(core, out version, out buildNumber));
            else if ((header >> 8) == 0x202) Check("runtime-version", Fn<GetVersion>(c, "FMOD_System_GetVersion")(core, out version));
            else throw new NotSupportedException("Verify the API family before calling GetVersion.");
            report["runtimeVersion"] = "0x" + version.ToString("X8");
            report["runtimeBuild"] = buildNumber;
            if ((version >> 8) is not (0x202 or 0x203)) throw new NotSupportedException("Only 2.02/2.03 SDK-110 layouts are covered. Do not change the SDK number to force compatibility.");
            Check("set-nosound", Fn<SetInt>(c, "FMOD_System_SetOutput")(core, 2));
            Check("studio-initialize", Fn<Initialize>(s, "FMOD_Studio_System_Initialize")(studio, 32, 0, 0, 0));
            // Probe API acceptance without executing audio callbacks on runtime mixer threads.
            Check("mixer-suspend", Fn<One>(c, "FMOD_System_MixerSuspend")(core)); suspended = true;
            if (sizeof(Description) != 216 || Marshal.OffsetOf<Description>(nameof(Description.Read)).ToInt32() != 72) throw new IOException("DSP layout mismatch.");
            description.Sdk = 110; description.Version = 0x10000; description.Inputs = description.Outputs = 1;
            byte[] name = Encoding.ASCII.GetBytes("Apex probe pass-through");
            for (int i = 0; i < name.Length; i++) description.Name[i] = name[i];
            description.Read = (nint)(delegate* unmanaged[Cdecl]<nint, float*, float*, uint, int, int*, int>)&PassThrough;
            Check("dsp-create", Fn<CreateDsp>(c, "FMOD_System_CreateDSP")(core, (nint)(&description), out dsp));
            Check("master-channel-group", Fn<GetPointer>(c, "FMOD_System_GetMasterChannelGroup")(core, out group));
            Check("dsp-attach", Fn<AddDsp>(c, "FMOD_ChannelGroup_AddDSP")(group, -1, dsp)); attached = true;
            // Remove before resuming: metadata inspection never exercises the probe effect.
            Check("dsp-remove", Fn<Two>(c, "FMOD_ChannelGroup_RemoveDSP")(group, dsp)); attached = false;
            Check("dsp-release", Fn<One>(c, "FMOD_DSP_Release")(dsp)); dsp = 0;
            Check("mixer-resume", Fn<One>(c, "FMOD_System_MixerResume")(core)); suspended = false;

            var loaded = new List<(string Path, nint Bank)>();
            for (int i = 0; i < args.Length; i++) if (args[i] == "--bank")
            {
                if (++i >= args.Length) throw new ArgumentException("Missing bank path.");
                string path = Path.GetFullPath(args[i]);
                Check("bank-load", Fn<LoadBank>(s, "FMOD_Studio_System_LoadBankFile")(studio, path, 0, out nint bank)); loaded.Add((path, bank));
            }
            foreach (var entry in loaded)
            {
                Check("bank-bus-count", Fn<Count>(s, "FMOD_Studio_Bank_GetBusCount")(entry.Bank, out int count));
                if (count is < 0 or > 4096) throw new IOException("Bus count exceeds bounded capacity.");
                nint[] array = new nint[count];
                Check("bank-bus-list", Fn<ListPointers>(s, "FMOD_Studio_Bank_GetBusList")(entry.Bank, array, count, out int actual));
                if (actual < 0 || actual > count) throw new IOException("Invalid bus count.");
                var rows = new List<object>();
                for (int i = 0; i < actual; i++)
                {
                    Check("bus-id", Fn<GetId>(s, "FMOD_Studio_Bus_GetID")(array[i], out Guid id));
                    byte[] buffer = new byte[4096]; int code = Fn<GetPath>(s, "FMOD_Studio_Bus_GetPath")(array[i], buffer, buffer.Length, out int required);
                    int length = Array.IndexOf(buffer, (byte)0); if (length < 0) length = buffer.Length;
                    rows.Add(new { id = Convert.ToHexString(id.ToByteArray()), path = code == 0 ? Encoding.UTF8.GetString(buffer, 0, length) : null, pathResult = code, pathBytesRequired = required });
                }
                banks.Add(new { file = entry.Path, buses = rows });
            }
            report["outcome"] = "completed";
        }
        catch (Exception e) { report["outcome"] = "failed"; report["failedStage"] = phase; report["error"] = e.Message; }
        finally
        {
            if (attached) Clean("remove-dsp", () => Fn<Two>(c, "FMOD_ChannelGroup_RemoveDSP")(group, dsp));
            if (dsp != 0) Clean("release-dsp", () => Fn<One>(c, "FMOD_DSP_Release")(dsp));
            if (suspended) Clean("resume-mixer", () => Fn<One>(c, "FMOD_System_MixerResume")(core));
            if (studio != 0) Clean("release-studio", () => Fn<One>(s, "FMOD_Studio_System_Release")(studio));
            // Keep the description address valid through release; OS unloads DLLs at process exit.
        }
        report["limitations"] = "No game connection, playback or audio callback validation. System creation, DSP creation and attachment are separate results. No game instance, independent PCM, parent routing or haptics verified. Missing paths may mean a missing strings bank. Own-process handles do not identify game handles.";
        try { string output = Path.GetFullPath(Value("--output")); Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(new { outcome = report["outcome"], steps = steps.Count, banks = banks.Count, cleanupPassed = clean, report = output })); }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
        return Equals(report["outcome"], "completed") && clean ? 0 : 1;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static int PassThrough(nint state, float* input, float* output, uint frames, int channels, int* outChannels)
    { if (outChannels != null) *outChannels = channels; if (input != null && output != null && input != output && channels > 0) { long bytes = (long)frames * channels * 4; Buffer.MemoryCopy(input, output, bytes, bytes); } return 0; }
    [StructLayout(LayoutKind.Sequential)]
    struct Description { public uint Sdk; public fixed byte Name[32]; public uint Version; public int Inputs, Outputs; public nint Create, Release, Reset, Read, Process, SetPosition; public int NumParameters; public nint ParamDesc; public fixed long ParameterCallbacks[8]; public nint ShouldProcess, UserData; public fixed long SystemCallbacks[3]; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Create(out nint system, uint version);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetPointer(nint system, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetVersion(nint system, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetVersion203(nint system, out uint value, out uint buildNumber);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetInt(nint system, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Initialize(nint system, int channels, uint flags, uint coreFlags, nint extra);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int One(nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Two(nint handle, nint other);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CreateDsp(nint system, nint description, out nint dsp);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int AddDsp(nint group, int index, nint dsp);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int LoadBank(nint system, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint flags, out nint bank);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Count(nint handle, out int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ListPointers(nint handle, [Out] nint[] values, int capacity, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetId(nint handle, out Guid id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetPath(nint handle, [Out] byte[] buffer, int size, out int required);
}
