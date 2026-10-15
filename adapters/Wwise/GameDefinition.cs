using System.Text;
using System.Text.Json;

namespace ApexAudio.Wwise;

sealed class GameDefinition
{
    public string Title { get; set; } = "";
    public string GameSha256 { get; set; } = "";
    public string EngineModule { get; set; } = "";
    public string EngineSha256 { get; set; } = "";
    public int BankVersion { get; set; }
    public string BankHeader { get; set; } = "";
    public uint BankId { get; set; } = 0x7a0c0001;
    public string CaptureFile { get; set; } = "";
    public string CaptureSha256 { get; set; } = "";
    public uint SdkYear { get; set; }
    public uint SdkSeries { get; set; }
    public List<GameBus> Buses { get; set; } = new();
    public static GameDefinition Load() => JsonSerializer.Deserialize<GameDefinition>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "game.json")), ApexAudio.Contracts.Wire.Json)!;
    public byte[] BuildConfiguration()
    {
        var buses = Buses.Where(b => b.Slot >= 0 && !b.Auxiliary).OrderBy(b => b.Id).ToList();
        if (buses.Count is < 1 or > 1024) throw new IOException("适配包采集分组数量无效。");
        byte[] header = Convert.FromBase64String(BankHeader);
        if (header.Length is < 28 or > 64 || BitConverter.ToInt32(header) != BankVersion) throw new IOException("适配包银行信息不匹配。");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        foreach (uint v in new uint[] { 0x43504741, 2, (uint)BankVersion, SdkYear, (uint)buses.Count, SdkSeries, (uint)header.Length, BankId }) writer.Write(v);
        for (int i = 0; i < 1024; i++) writer.Write(i < buses.Count ? buses[i].Id : 0);
        for (int i = 0; i < 1024; i++) writer.Write(i < buses.Count ? (uint)buses[i].Slot : 0);
        // Start silent. SetFeedbackGroups selects the native mix independently of recording.
        for (int i = 0; i < 1024; i++) writer.Write(0u);
        writer.Write(header); writer.Write(new byte[64 - header.Length]);
        byte[] module = Encoding.Unicode.GetBytes(EngineModule);
        if (module.Length > 254) throw new IOException("模块名称过长。");
        writer.Write(module); writer.Write(new byte[256 - module.Length]); return stream.ToArray();
    }
}
sealed class GameBus
{
    public uint Id { get; set; } public uint Parent { get; set; }
    public string Name { get; set; } = ""; public string NameSource { get; set; } = "编号";
    public string Category { get; set; } = "其他"; public bool Auxiliary { get; set; } public int Slot { get; set; } = -1;
}
