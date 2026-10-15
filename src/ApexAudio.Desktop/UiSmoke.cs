using System.Text.Json;
using ApexAudio.Contracts;
using Microsoft.Web.WebView2.Core;

namespace ApexAudio.Desktop;

sealed partial class MainWindow
{
    // Optional developer-only layout smoke using the bundled synthetic adapter.
    async Task Smoke()
    {
        try
        {
            Directory.CreateDirectory(smokeDirectory!); autoConnect = false; settings.AutoFeedback = false;
            var demo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "artifacts", "packages", "demo.gpack"));
            workspace.Packages.Install(demo);
            await Run(async () => { await workspace.SelectPack("demo.independent-audio"); await workspace.Connect(0, null); });
            await Task.Delay(500); FullState(); await Task.Delay(200);
            await CaptureLayout("workspace-wide");
            double scale = DeviceDpi / 96.0; var wide = Size;
            Size = new((int)(900 * scale), (int)(720 * scale)); await Task.Delay(200); await CaptureLayout("workspace-small");
            Size = wide;
            await workspace.StartRecording(); await Task.Delay(1200); await workspace.StopRecording(); FullState();
            await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-page=\"recordings\"]').click()");
            await Task.Delay(250); await CaptureLayout("timeline-wide");
            await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-page=\"settings\"]').click()");
            await Task.Delay(200); await CaptureLayout("settings-dark");
            await browser.CoreWebView2.ExecuteScriptAsync("document.documentElement.dataset.theme='light'");
            await Task.Delay(200); await CaptureLayout("settings-light");
            settings.RecordingMode = "timed"; settings.RecordingSeconds = 1; settings.RecordingDelay = 1;
            await Run(ToggleRecording); await Task.Delay(200); bool countdown = countdownEnd > 0 && !workspace.Recordings.IsRecording;
            await Run(ToggleRecording); bool cancelled = countdownEnd == 0 && !workspace.Recordings.IsRecording;
            settings.RecordingDelay = 0; Shortcut(); await Task.Delay(1700);
            bool timedStop = !workspace.Recordings.IsRecording && workspace.Take?.Duration is > .8 and < 1.2;
            var dom = await browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify({rows:document.querySelectorAll('.tree-row').length,tracks:document.querySelectorAll('.track').length,bodyWidth:document.body.scrollWidth,viewport:innerWidth,error:document.getElementById('errorText').textContent})");
            File.WriteAllText(Path.Combine(smokeDirectory!, "ui-result.json"), JsonSerializer.Serialize(new { success = workspace.Take?.Tracks.Count == 4 && countdown && cancelled && timedStop, countdown, cancelled, timedStop, dom }, Wire.Json));
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(smokeDirectory!, "ui-error.txt"), e.ToString()); }
        finally { Close(); }
    }
    async Task CaptureLayout(string name)
    {
        using var image = File.Create(Path.Combine(smokeDirectory!, name + ".png"));
        await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
    }
}
