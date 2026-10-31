using System.Text.Json;
using ApexAudio.Contracts;

namespace ApexAudio.Fmod;

sealed class GameDefinition
{
    public string Name { get; set; } = "";
    public string Executable { get; set; } = "";
    public string ExecutableSha256 { get; set; } = "";
    public Dictionary<string, string> Modules { get; set; } = new();
    public string WrapperModule { get; set; } = "";
    public uint WrapperRva { get; set; }
    public uint RuntimeVersion { get; set; }
    public uint StateOffset { get; set; }
    public uint StudioOffset { get; set; }
    public string Locator { get; set; } = "wrapper";
    public uint StudioHandle { get; set; }
    public string CaptureFile { get; set; } = "";
    public string CaptureSha256 { get; set; } = "";
    public List<BusDefinition> Buses { get; set; } = new();
    public LaunchContext? Context { get; private set; }
    public BackendIdentity? Identity { get; private set; }
    public static GameDefinition Load(string[] args)
    {
        int at = Array.IndexOf(args, "--context");
        if (at < 0) return JsonSerializer.Deserialize<GameDefinition>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "game.json")), Wire.Json)!;
        if (at + 1 == args.Length) throw new IOException("缺少启动上下文。");
        string path = Path.GetFullPath(args[at + 1]);
        var context = JsonSerializer.Deserialize<LaunchContext>(File.ReadAllText(path), Wire.Json) ?? throw new IOException("启动上下文为空。");
        if (context.Version != 1 || context.Backend.Id != "apex.fmod" || context.Backend.Version != "0.2.0" || context.Backend.Variant != "studio-v1") throw new IOException("不受支持的共享后端身份或上下文格式。");
        var descriptor = BackendFiles.Read(AppContext.BaseDirectory, context.Backend);
        var game = context.Parameters.Deserialize<GameDefinition>(Wire.Json) ?? throw new IOException("采集参数为空。");
        game.Context = context; game.Identity = new(context.Backend.Id, context.Backend.Version, context.Backend.Variant, BackendFiles.Hash(path));
        game.CaptureFile = descriptor.Variants[context.Backend.Variant]; game.CaptureSha256 = descriptor.Files[game.CaptureFile];
        game.Validate(); return game;
    }
    public void Validate()
    {
        if ((RuntimeVersion >> 8) is not (0x202 or 0x203)) throw new IOException("共享 FMOD 后端仅支持已核对的 2.02/2.03 接口系列。");
        if (Locator is not ("wrapper" or "studioHandle") || Locator == "studioHandle" && StudioHandle == 0 || Locator == "wrapper" && (WrapperRva == 0 || StateOffset > 4092 || StudioOffset > 4088)) throw new IOException("FMOD 实例定位参数无效。");
        if (!System.Text.RegularExpressions.Regex.IsMatch(ExecutableSha256, "^[0-9a-fA-F]{64}$") || Path.GetFileName(Executable) != Executable ||
            !Modules.ContainsKey("fmod.dll") || !Modules.ContainsKey("fmodstudio.dll") || !Modules.ContainsKey(WrapperModule) || Modules.Values.Any(h => !System.Text.RegularExpressions.Regex.IsMatch(h,"^[0-9a-fA-F]{64}$"))) throw new IOException("缺少关键模块和游戏构建的摘要。");
        if (Buses.Count is < 1 or > 64 || Buses.Select(b => b.Id).Distinct().Count() != Buses.Count || Buses.Any(b => !System.Text.RegularExpressions.Regex.IsMatch(b.Id,"^[0-9a-fA-F]{32}$"))) throw new IOException("FMOD 总线编号无效。");
        var stable = Buses.Select(b => b.StableId ?? b.Id).ToArray();
        if (stable.Any(string.IsNullOrWhiteSpace) || stable.Distinct().Count() != stable.Length) throw new IOException("稳定分组 ID 重复或为空。");
        var map = Buses.ToDictionary(b => b.Id);
        foreach(var bus in Buses) { var seen = new HashSet<string>{bus.Id}; var parent = bus.ParentId; while(parent != null) { if(!map.TryGetValue(parent,out var next) || !seen.Add(parent)) throw new IOException("FMOD 父组无效。"); parent=next.ParentId; } }
    }
    public byte[] Configuration()
    {
        if (Buses.Count is < 1 or > 64 || Buses.Select(b => b.Id).Distinct().Count() != Buses.Count) throw new IOException("适配包总线配置无效。");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        foreach (uint value in new uint[] { 0x314d4641, 2, Locator == "studioHandle" ? 0 : WrapperRva, RuntimeVersion, (uint)Buses.Count, Locator == "studioHandle" ? StudioHandle : StateOffset, StudioOffset }) writer.Write(value);
        var module = System.Text.Encoding.Unicode.GetBytes(WrapperModule);
        if (module.Length > 254) throw new IOException("适配包模块名称过长。");
        writer.Write(module); writer.Write(new byte[256 - module.Length]);
        for (int i = 0; i < 64; i++) writer.Write(i < Buses.Count ? Convert.FromHexString(Buses[i].Id) : new byte[16]);
        return stream.ToArray();
    }
}
sealed record BusDefinition(string Id, string Path, string Name, string? ParentId, string Category, bool ContainsChildren, string? StableId = null);
