using System.Diagnostics;
using System.Text.Json;
using ApexAudio.Core;
using ApexAudio.XHaptics;

namespace ApexAudio.Desktop;

sealed partial class MainWindow
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int length);
    void ApplyAppearance() { int dark = settings.Theme == "dark" ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, 4); browser.DefaultBackgroundColor = dark == 1 ? Color.FromArgb(12, 17, 27) : Color.FromArgb(242, 245, 250); }
    string feedbackDetail = "反馈已关闭"; bool feedbackSuppressed; long nextFeedbackRetry;
    string FeedbackStatus => !settings.AutoFeedback ? "补充震动已关闭" : !workspace.Connected
        ? (autoConnect && autoSuppressed == null ? "等待采集连接 · 恢复后自动开启反馈" : "等待声音采集连接")
        : workspace.Feedback.Enabled && workspace.Feedback.EngineAlive && workspace.Feedback.Delivered
            ? (workspace.Feedback.HasSignal ? "所选声音正在送入 X-Haptics" : "反馈通道已就绪 · 当前无选中声音输出")
            : feedbackDetail;
    static string FindXhExecutable()
    {
        string parent = Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!;
        string formal=Path.Combine(parent,"Engine","xHaptics Engine.exe");
        return File.Exists(formal)?formal:Directory.EnumerateDirectories(parent, "X-Haptics*").Select(d => Path.Combine(d, "x-haptic-engine-fusion.exe")).FirstOrDefault(File.Exists) ?? "";
    }
    async Task MaintainFeedback()
    {
        if (!workspace.Connected || !settings.AutoFeedback || feedbackSuppressed || smokeDirectory != null) return;
        var ds=Process.GetProcessesByName("APEXtoDS");bool dsRunning=ds.Length>0;foreach(var p in ds)p.Dispose();
        if(dsRunning){if(workspace.Feedback.Enabled)await workspace.Feedback.Stop();feedbackDetail="DS5 转接运行中 · 等待普通模式";return;}
        if (workspace.Feedback.Enabled && workspace.Feedback.EngineAlive && workspace.Feedback.Active)
        { feedbackDetail = workspace.Feedback.Delivered ? "反馈通道已就绪" : "等待 X-Haptics 读取声音"; return; }
        if (Environment.TickCount64 < nextFeedbackRetry) return;
        nextFeedbackRetry = Environment.TickCount64 + 3000;
        try
        {
            if (workspace.Feedback.Enabled) await workspace.Feedback.Stop();
            var engines = EngineApi.Supported.SelectMany(s=>Process.GetProcessesByName(s.ProcessName)).ToArray(); bool running = engines.Length > 0; foreach (var p in engines) p.Dispose();
            if (!running)
            {
                if (!settings.AutoStartXh) { feedbackDetail = "等待 X-Haptics 启动"; return; }
                string executable = settings.XhExecutable;
                if (string.IsNullOrWhiteSpace(executable)) executable = FindXhExecutable();
                if (!File.Exists(executable)) { feedbackDetail = "请在应用设置中选择 X-Haptics 程序"; return; }
                var support=EngineApi.Supported.SingleOrDefault(s=>s.Sha256.Equals(PackageStore.Hash(executable),StringComparison.OrdinalIgnoreCase));
                if(support==null)throw new IOException("X-Haptics 版本与反馈模块不匹配。");
                var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                string? launchToken=null;
                if(support.Kind=="formal")
                {
                    if(settings.Port!=support.Port)throw new IOException("正式 Engine 使用本地端口 18757，请在应用设置中保存该端口。");
                    launchToken=Guid.NewGuid().ToString("N");
                    start.ArgumentList.Add("--web-url");start.ArgumentList.Add("http://127.0.0.1:18757/diagnostics/");
                    start.ArgumentList.Add("--web-client-type");start.ArgumentList.Add("develop");
                    start.ArgumentList.Add("--dev-launch-token");start.ArgumentList.Add(launchToken);
                }
                else
                {
                    start.ArgumentList.Add("--root"); start.ArgumentList.Add(Path.GetDirectoryName(executable)!);
                    start.ArgumentList.Add("--config"); start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(executable)!, "config"));
                    start.ArgumentList.Add("--port"); start.ArgumentList.Add(settings.Port.ToString()); start.ArgumentList.Add("--no-browser");
                }
                using var launched=Process.Start(start);
                if(launched!=null&&launchToken!=null)workspace.Feedback.Engine.SaveLaunch(launched.Id,launchToken);
                feedbackDetail = "正在启动 X-Haptics"; return;
            }
            await Task.Run(workspace.StartFeedback); feedbackDetail = "反馈已连接";
        }
        catch (Exception e) when (e is IOException or System.Net.Http.HttpRequestException or TaskCanceledException or System.ComponentModel.Win32Exception) { feedbackDetail = e.Message; }
    }
    void RememberSelection()
    {
        settings.LastPackId = workspace.Pack?.Manifest.Id;
        if (workspace.Pack != null && workspace.Preset != null && !workspace.Dirty) settings.LastPresets[workspace.Pack.Manifest.Id] = workspace.Preset.Id;
        settings.Save(dataRoot);
    }
    void RestorePresetSelection()
    {
        if (workspace.Pack == null || !settings.LastPresets.TryGetValue(workspace.Pack.Manifest.Id, out string? id)) return;
        if (workspace.Presets.List(workspace.Pack).Any(p => p.Id == id)) workspace.LoadPreset(id);
    }
}
