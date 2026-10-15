using ApexAudio.Contracts;
using ApexAudio.Core;
using System.Text.Json;

namespace ApexAudio.Desktop;

sealed class AppSettings
{
    public float Gain { get; set; } = 1;
    public int Port { get; set; } = 18789;
    public bool AutoConnect { get; set; } = true;
    public bool AutoFeedback { get; set; } = true;
    public bool AutoStartXh { get; set; } = true;
    public string XhExecutable { get; set; } = "";
    public string Theme { get; set; } = "dark";
    public string RecordingMode { get; set; } = "timed";
    public int RecordingSeconds { get; set; } = 10;
    public int RecordingDelay { get; set; }
    public bool RecordingSounds { get; set; } = true;
    public uint HotkeyModifiers { get; set; } = 3;
    public uint HotkeyKey { get; set; } = 0x78;
    public string HotkeyName { get; set; } = "Ctrl + Alt + F9";
    [System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.WriteAsString | System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)]
    public ulong ControllerMask { get; set; }
    public string ControllerName { get; set; } = "未绑定";
    public string? LastPackId { get; set; }
    public Dictionary<string, string> LastPresets { get; set; } = new();
    public static AppSettings Load(string root)
    {
        string file = Path.Combine(root, "settings.json");
        var result = File.Exists(file) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), Wire.Json)! : new();
        result.Validate(); return result;
    }
    public void Validate()
    {
        if (Port is < 1024 or > 65535 || !float.IsFinite(Gain) || Gain is < 0 or > 4) throw new IOException("端口或增益无效。");
        if (Theme is not ("dark" or "light") || RecordingMode is not ("timed" or "manual") || RecordingSeconds is < 1 or > 60 || RecordingDelay is < 0 or > 10) throw new IOException("主题或录音设置无效。");
        if ((HotkeyModifiers & ~15u) != 0 || HotkeyKey > 255) throw new IOException("快捷键无效。");
    }
    public void Save(string root) { Validate(); PresetStore.AtomicWrite(Path.Combine(root, "settings.json"), this); }
}
