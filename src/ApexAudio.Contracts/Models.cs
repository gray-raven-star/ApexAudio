using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApexAudio.Contracts;

public static class Wire
{
    public const int Version = 1;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    public static JsonElement Element(object? value) => JsonSerializer.SerializeToElement(value, Json);
    public static T Read<T>(JsonElement value) => value.Deserialize<T>(Json) ?? throw new InvalidDataException("消息内容为空。");
}

public sealed record AdapterCapabilities(bool Groups, bool IndependentPcm, bool Activity, bool SourceInfo, bool WholeMix, bool RealtimeMix = false);
public sealed record FeedbackSource(string Format, string MappingName, int ProducerProcessId);
public sealed record AdapterInfo(string Id, string Name, int ProtocolVersion, AdapterCapabilities Capabilities);
public sealed record AudioGroup(string Id, string Name, string? ParentId, string Category, bool HasPcm, bool ContainsChildren = false, string NameSource = "适配包");
public sealed record ConnectRequest(int ProcessId, string? GamePath);
public sealed record CaptureRequest(string[] GroupIds);
public sealed record AdapterStatus(bool Connected, bool Capturing, string Detail, long DroppedBlocks = 0);
public sealed record Request(int Version, long Id, string Method, JsonElement Params);
public sealed record Response(int Version, long Id, bool Ok, JsonElement Result, string? Error = null, string? Code = null);

public sealed class PackManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = "";
    public string Source { get; set; } = "";
    public string Description { get; set; } = "";
    public int ProtocolVersion { get; set; } = Wire.Version;
    public string EntryPoint { get; set; } = "adapter.exe";
    public bool Demo { get; set; }
    public List<GameMatch> Games { get; set; } = new();
    public string[] Presets { get; set; } = Array.Empty<string>();
    public Dictionary<string, string> Files { get; set; } = new();
}
public sealed record GameMatch(string Executable, string? Sha256 = null);

public sealed class SoundPreset
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string PackId { get; set; } = "";
    public string? BasePresetId { get; set; }
    public string? BasePackVersion { get; set; }
    public List<PresetFolder> Folders { get; set; } = new();
    public Dictionary<string, GroupSetting> Groups { get; set; } = new();
}
public sealed record PresetFolder(string Id, string Name, string? ParentId = null, int Order = 0);
public sealed record GroupSetting(string? Name = null, bool Enabled = false, string? FolderId = null, int Order = 0);

public sealed record AudioPacket(uint GroupIndex, uint SampleRate, uint Channels, long Tick, long Sequence, byte[] Pcm)
{
    public int Frames => Pcm.Length / checked((int)Channels * 2);
    public float Peak { get { int max = 0; for (int i = 0; i < Pcm.Length; i += 2) max = Math.Max(max, Math.Abs((int)BitConverter.ToInt16(Pcm, i))); return max / 32768f; } }
}
