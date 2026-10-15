using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ApexAudio.Contracts;
using ApexAudio.Core;

string? Option(string key) { int i = Array.IndexOf(args, key); return i < 0 ? null : i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("Missing value: " + key); }
string Required(string key) => Option(key) ?? throw new ArgumentException("Required: " + key);
object? report = null; Exception? failure = null; AdapterClient? activeClient = null;
var context = new DiagnosticContext { Command = args.FirstOrDefault() ?? "help" };
void Emit(object value) => report = value;
try
{
    _ = Option("--output");
    string command = args.FirstOrDefault() ?? "help";
    if (command == "inspect")
    {
        context.Stage = "static-exports";
        string file = Path.GetFullPath(Required("--exe"));
        string[] staticExports;
        using (var stream = File.OpenRead(file))
        using (var pe = new PEReader(stream))
        {
            int rva = pe.PEHeaders.PEHeader?.ExportTableDirectory.RelativeVirtualAddress ?? 0;
            staticExports = rva == 0 ? [] : Exports.Read(rva, (at, count) => pe.GetSectionData(at).GetContent(0, count).ToArray());
        }
        var modules = new List<object>();
        if (Option("--pid") is string pid)
        {
            context.Stage = "runtime-modules";
            using var process = Process.GetProcessById(int.Parse(pid));
            string? requested = Option("--module");
            foreach (ProcessModule module in process.Modules)
            {
                bool selected = requested != null ? module.ModuleName.Equals(requested, StringComparison.OrdinalIgnoreCase)
                    : module.BaseAddress == process.MainModule!.BaseAddress || new[] { "fmod", "aksoundengine", "wwise" }.Any(s => module.ModuleName.Contains(s, StringComparison.OrdinalIgnoreCase));
                if (!selected) { modules.Add(new { name = module.ModuleName, path = module.FileName }); continue; }
                try
                {
                    using var memory = new ReadOnlyModule(process.Id, module.BaseAddress, module.ModuleMemorySize);
                    byte[] dos = memory.Read(0, 64); int nt = BitConverter.ToInt32(dos, 60);
                    byte[] header = memory.Read(nt, 24 + 120);
                    if (BitConverter.ToUInt16(header,24) != 0x20b) throw new IOException("Only x64 PE images are supported.");
                    int exportRva = BitConverter.ToInt32(header,24+112);
                    string[] names = exportRva == 0 ? [] : Exports.Read(exportRva, memory.Read);
                    modules.Add(new { name = module.ModuleName, path = module.FileName, sha256 = PackageStore.Hash(module.FileName), runtimeExports = names, evidence = Exports.Evidence(names) });
                }
                catch (Exception e) { modules.Add(new { name = module.ModuleName, error = e.Message }); }
            }
        }
        var resources = new List<object>(); bool truncated = false;
        if (Option("--resources") is string directory)
        {
            context.Stage = "resource-candidates";
            int visited = 0;
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (string path in Directory.EnumerateFiles(directory, "*", options))
            {
                if (++visited > 20000 || resources.Count >= 256) { truncated = true; break; }
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (!new[] { ".bnk", ".nbnk", ".bank", ".fsb", ".pak", ".utoc", ".ucas", ".archive", ".pck" }.Contains(extension)) continue;
                int? bankVersion = null;
                if (extension is ".bnk" or ".nbnk") { using var f = File.OpenRead(path); byte[] h = new byte[12]; if (f.Read(h) == 12 && Encoding.ASCII.GetString(h,0,4) == "BKHD") bankVersion = BitConverter.ToInt32(h,8); }
                resources.Add(new { path, extension, bankVersion });
            }
        }
        Emit(new { mode="read-only", file, sha256=PackageStore.Hash(file), staticExports, evidence=Exports.Evidence(staticExports), modules, resources, truncated,
            conclusion="Evidence only. Missing names/exports do not prove an engine is absent. Containers do not identify the audio engine. No process memory was written and no module was loaded into a game." });
    }
    else if(command=="read-memory")
    {
        context.Stage = "read-memory";
        int pid=int.Parse(Required("--pid")),count=int.Parse(Option("--bytes")??"256");
        if(count<1||count>16384)throw new ArgumentException("Read 1..16384 bytes from a known address; this is not a memory scanner.");
        string addressText=Required("--address");
        ulong address=addressText.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?Convert.ToUInt64(addressText[2..],16):ulong.Parse(addressText);
        using var memory=new ReadOnlyModule(pid,(nint)address,count); byte[] data=memory.Read(0,count);
        if(Option("--binary") is string binary) { string path=Path.GetFullPath(binary);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,data); }
        Emit(new {mode="read-only",pid,address="0x"+address.ToString("X"),bytes=count,hex=Convert.ToHexString(data),note="Bounded read at an explicitly supplied address. No writes, loading or function calls in the target."});
    }
    else if(command=="bank-index")
    {
        context.Stage = "bank-index";
        string file=Path.GetFullPath(Required("--file"));using var stream=File.OpenRead(file);using var reader=new BinaryReader(stream);
        var chunks=new List<object>();var objects=new List<object>();int? version=null;
        while(stream.Position+8<=stream.Length)
        {
            string tag=Encoding.ASCII.GetString(reader.ReadBytes(4));uint size=reader.ReadUInt32();long payload=stream.Position,end=payload+size;
            if(end>stream.Length)throw new IOException("Chunk exceeds file. This may not be a raw supported bank.");
            chunks.Add(new {tag,offset=payload,size});
            if(tag=="BKHD"&&size>=4)version=reader.ReadInt32();
            if(tag=="HIRC"&&size>=4)
            {
                uint count=reader.ReadUInt32();if(count>100000)throw new IOException("HIRC object count exceeds inspection limit.");
                for(uint i=0;i<count;i++)
                {
                    if(stream.Position+5>end)throw new IOException("Truncated HIRC directory.");
                    byte type=reader.ReadByte();uint length=reader.ReadUInt32();long at=stream.Position;
                    if(length<4||at+length>end)throw new IOException("Invalid HIRC object length.");
                    uint id=reader.ReadUInt32();objects.Add(new {type,id,offset=at,bytes=length});stream.Position=at+length;
                }
            }
            stream.Position=end;
        }
        Emit(new {file,version,chunks,objects,limitations="Raw little-endian bank chunk/HIRC index only. Object type numbers and internal layouts vary by version. This does not decode routes, names, effects slots or independent audio; do not treat every object as a bus."});
    }
    else if (command is "check" or "capture")
    {
        if (command == "capture" && !args.Contains("--allow-game-connection")) throw new ArgumentException("capture loads the chosen package into its target through its normal Connect operation. Add --allow-game-connection only when this is authorized; it can crash the game. inspect is read-only.");
        string archive = Path.GetFullPath(Required("--pack"));
        context.Stage = "package-integrity";
        string temporaryRoot = Environment.GetEnvironmentVariable("APEX_AUDIO_TEMP") ?? Path.GetTempPath();
        string scratch = context.Scratch = Path.Combine(temporaryRoot,"ApexAudio-DevTools-"+Guid.NewGuid().ToString("N"));
        var pack = new PackageStore(scratch).Install(archive);
        context.InstalledPackDirectory = pack.Directory; context.EntryPointPath = PackageStore.SafePath(pack.Directory, pack.Manifest.EntryPoint);
        context.ModulePaths = pack.Manifest.Files.Keys.Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).Select(p => PackageStore.SafePath(pack.Directory, p)).ToArray();
        bool launch = command == "capture" || args.Contains("--launch");
        if (!launch) { Emit(new { integrity=true, id=pack.Manifest.Id, version=pack.Manifest.Version, adapterExecuted=false, gameConnected=false, scratch }); return; }
        var client = activeClient = new AdapterClient(); context.Cleanup = "pending";
        long packets=0, frames=0; var peaks=new Dictionary<uint,float>(); var payloads=new Dictionary<uint,string>(); object gate=new(); var errors=new List<string>();
        client.Audio += p => { lock(gate) { packets++; frames+=p.Frames; peaks[p.GroupIndex]=Math.Max(peaks.GetValueOrDefault(p.GroupIndex),p.Peak); if(p.Peak>0)payloads.TryAdd(p.GroupIndex,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(p.Pcm))); } };
        client.Failed += message => { lock(gate) errors.Add(message); };
        context.Stage = "adapter-launch"; await client.Launch(pack); context.AdapterExecuted = true;
        context.Stage = "protocol";
        var groups=await client.Call<List<AudioGroup>>("GetAudioGroups");
        AdapterStatus? status=null; object? realtime=null;
        bool connect=command=="capture" || (pack.Manifest.Demo && args.Contains("--demo-audio"));
        if(connect)
        {
            int pid=command=="capture"?int.Parse(Required("--pid")):0;
            string? gamePath=null;
            if(pid!=0) { using var game=Process.GetProcessById(pid); gamePath=game.MainModule?.FileName; }
            context.Stage = "game-connection"; context.ConnectionAttempted = command == "capture";
            await client.Connect(pid,gamePath); context.GameConnected = command == "capture"; groups=client.Groups.ToList();
            context.Stage = "audio-observation";
            int seconds=int.Parse(Option("--seconds")??"5"); if(seconds<1||seconds>60)throw new ArgumentException("seconds must be 1..60.");
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            status=await client.Call<AdapterStatus>("GetStatus");
            if(client.Info?.Capabilities.RealtimeMix==true)
            {
                var source=await client.Call<FeedbackSource>("GetFeedbackSource");
                if(source.Format=="apex-f32-planar-ring-v1") { using var map=MemoryMappedFile.OpenExisting(source.MappingName,MemoryMappedFileRights.Read); using var view=map.CreateViewAccessor(0,104,MemoryMappedFileAccess.Read); realtime=new {source.Format,source.MappingName,active=view.ReadInt32(16),blocks=view.ReadInt32(80)}; }
            }
        }
        lock(gate) Emit(new { integrity=true, id=pack.Manifest.Id, version=pack.Manifest.Version, adapterExecuted=true, gameConnected=command=="capture", demoConnected=connect&&pack.Manifest.Demo, groups, packets, frames, nonZeroGroups=peaks.Count(p=>p.Value>0), peaks=peaks.Select(p=>new {id=groups[(int)p.Key].Id,peak=p.Value,firstPayloadSha256=payloads.GetValueOrDefault(p.Key)}), status, realtime, errors,
            limitations="PCM proves received data, not semantic separation. Matching hashes may indicate parent/child overlap or duplicate data and require listening. No X-Haptics engine or physical feedback was started.", scratch });
    }
    else if(command=="native-check")
    {
        context.Stage = "native-check";
        string file=Path.GetFullPath(Required("--module")),entry=Required("--entry");
        nint module=NativeLibrary.Load(file);
        try { uint result=Marshal.GetDelegateForFunctionPointer<NativeCheck>(NativeLibrary.GetExport(module,entry))(0); Emit(new {file,sha256=PackageStore.Hash(file),entry,result,gameConnected=false}); if(result!=0)Environment.ExitCode=1; }
        finally { NativeLibrary.Free(module); }
    }
    else if (command == "help") Console.WriteLine("ApexAudio.DevTools\ninspect --exe PATH [--pid PID] [--module NAME] [--resources DIR] [--output REPORT.json]\nread-memory --pid PID --address 0xADDRESS [--bytes 256] [--binary snippet.bin] [--output REPORT.json]\nbank-index --file RAW_BANK [--output REPORT.json]\ncheck --pack FILE.gpack [--launch] [--demo-audio] [--seconds 5] [--output REPORT.json]\ncapture --pack FILE.gpack --pid PID --allow-game-connection [--seconds 5] [--output REPORT.json]\nnative-check --module YOUR.dll --entry YOUR_SELF_TEST [--output REPORT.json]\ncheck without --launch only validates/extracts files. --launch executes adapter code. capture performs its normal game connection. native-check executes code in this process. No command starts physical feedback.");
    else throw new ArgumentException("Unknown command: " + command);
}
catch(Exception e) { failure = e; Environment.ExitCode=1; }
finally
{
    if (activeClient != null)
    {
        try { await activeClient.DisposeAsync(); context.Cleanup = "adapter-disposed"; }
        catch (Exception e) { context.Cleanup = "adapter-disposal-failed"; context.CleanupError = e.Message; Environment.ExitCode = 1; }
    }
    if (report != null || failure != null) DiagnosticReport.Write(args, context, report, failure);
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate uint NativeCheck(nint argument);
static class Exports
{
    public static string[] Read(int rva,Func<int,int,byte[]> read)
    {
        byte[] directory=read(rva,40); int count=BitConverter.ToInt32(directory,24), names=BitConverter.ToInt32(directory,32);
        if(count<0||count>32768)throw new IOException("Export count outside inspection limit.");
        byte[] addresses=read(names,checked(count*4)); var result=new List<string>();
        for(int i=0;i<count;i++)
        {
            int at=BitConverter.ToInt32(addresses,i*4); var name=new List<byte>();
            for(int n=0;n<512;n++) { byte value=read(checked(at+n),1)[0]; if(value==0)break; name.Add(value); }
            result.Add(Encoding.ASCII.GetString(name.ToArray()));
        }
        return result.ToArray();
    }
    public static string[] Evidence(IEnumerable<string> names) => names.Where(n=>n.Contains("FMOD",StringComparison.OrdinalIgnoreCase)||n.Contains("SoundEngine",StringComparison.OrdinalIgnoreCase)||n.Contains("Submix",StringComparison.OrdinalIgnoreCase)||n.Contains("Wwise",StringComparison.OrdinalIgnoreCase)).ToArray();
}
sealed class ReadOnlyModule:IDisposable
{
    readonly nint handle,origin; readonly int length;
    public ReadOnlyModule(int pid,nint address,int size) { origin=address;length=size;handle=OpenProcess(0x410,false,pid);if(handle==0)throw new IOException("Read-only process access denied."); }
    public byte[] Read(int rva,int count)
    {
        if(rva<0||count<0||count>4*1024*1024||(long)rva+count>length)throw new IOException("Read outside selected module or inspection limit.");
        byte[] buffer=new byte[count]; if(count!=0&&(!ReadProcessMemory(handle,origin+rva,buffer,(nuint)count,out nuint got)||got!=(nuint)count))throw new IOException("Runtime image is not readable.");return buffer;
    }
    public void Dispose()=>CloseHandle(handle);
    [DllImport("kernel32",SetLastError=true)] static extern nint OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32",SetLastError=true)] static extern bool ReadProcessMemory(nint process,nint address,byte[] buffer,nuint length,out nuint read);
    [DllImport("kernel32")] static extern bool CloseHandle(nint handle);
}
