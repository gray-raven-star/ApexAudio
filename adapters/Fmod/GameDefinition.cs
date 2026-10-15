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
    public string CaptureFile { get; set; } = "";
    public string CaptureSha256 { get; set; } = "";
    public List<BusDefinition> Buses { get; set; } = new();
    public static GameDefinition Load() => JsonSerializer.Deserialize<GameDefinition>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "game.json")), Wire.Json)!;
    public byte[] Configuration()
    {
        if (Buses.Count is < 1 or > 64 || Buses.Select(b => b.Id).Distinct().Count() != Buses.Count) throw new IOException("适配包总线配置无效。");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        foreach (uint value in new uint[] { 0x314d4641, 2, WrapperRva, RuntimeVersion, (uint)Buses.Count, StateOffset, StudioOffset }) writer.Write(value);
        var module = System.Text.Encoding.Unicode.GetBytes(WrapperModule);
        if (module.Length > 254) throw new IOException("适配包模块名称过长。");
        writer.Write(module); writer.Write(new byte[256 - module.Length]);
        for (int i = 0; i < 64; i++) writer.Write(i < Buses.Count ? Convert.FromHexString(Buses[i].Id) : new byte[16]);
        return stream.ToArray();
    }
}
sealed record BusDefinition(string Id, string Path, string Name, string? ParentId, string Category, bool ContainsChildren);
