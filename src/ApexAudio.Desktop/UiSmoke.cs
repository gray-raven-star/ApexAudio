using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO.Compression;
using ApexAudio.Contracts;
using ApexAudio.Core;
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
            var installed=workspace.Packages.Install(demo);workspace.Packages.SelectCurrent(installed.Manifest.Id,installed.Reference);
            await Run(async () => { await workspace.SelectPack("demo.independent-audio"); await workspace.Connect(0, null); });
            workspace.Preset!.Groups.Remove(workspace.Groups.First(g=>g.HasPcm).Id);workspace.Preset.Groups["historical"]=new(Enabled:true);
            await Run(()=>workspace.Connect(0,null));
            await Task.Delay(500); FullState(); await Task.Delay(200);
            bool presetNotice=await browser.CoreWebView2.ExecuteScriptAsync("!document.getElementById('presetNotice').hidden && document.getElementById('presetNotice').textContent.includes('重新配置')")=="true";
            await CaptureLayout("workspace-wide");
            string next=Path.Combine(smokeDirectory!,"next-demo.gpack");
            File.Copy(demo,next);
            using(var zip=ZipFile.Open(next,ZipArchiveMode.Update))
            {
                var entry=zip.GetEntry("manifest.json")!;JsonNode manifest;
                using(var reader=new StreamReader(entry.Open()))manifest=JsonNode.Parse(reader.ReadToEnd())!;
                entry.Delete();manifest["version"]="9.0.0";
                using var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open());writer.Write(manifest.ToJsonString());
            }
            var newer=workspace.Packages.Install(next);FullState();
            await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-page=\"packages\"]').click()");
            await Task.Delay(200);
            await browser.CoreWebView2.ExecuteScriptAsync("[...document.querySelectorAll('.package-card')].find(c=>c.querySelector('.package-meta').textContent.startsWith('9.0.0')).querySelector('button').click()");
            await Task.Delay(500);
            bool versionSelection=workspace.Packages.GetCurrent(installed.Manifest.Id)?.Reference==newer.Reference && workspace.Pack?.Reference==installed.Reference && workspace.Connected;
            await CaptureLayout("package-versions");
            bool? v2Packages=null;
            string v2Input=Path.Combine(smokeDirectory!,"v2-inputs.json");
            if(File.Exists(v2Input))v2Packages=await SmokeV2Packages(v2Input);
            await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-page=\"workspace\"]').click()");
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
            File.WriteAllText(Path.Combine(smokeDirectory!, "ui-result.json"), JsonSerializer.Serialize(new { success = workspace.Take?.Tracks.Count == 4 && countdown && cancelled && timedStop && versionSelection && presetNotice && v2Packages!=false, countdown, cancelled, timedStop, versionSelection,presetNotice,v2Packages, dom }, Wire.Json));
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(smokeDirectory!, "ui-error.txt"), e.ToString()); }
        finally { Close(); }
    }
    async Task<bool> SmokeV2Packages(string input)
    {
        var paths=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(input),Wire.Json)!;
        var pack=workspace.Packages.Install(paths["pack"]);FullState();await Task.Delay(200);
        string Card(string version)=>"[...document.querySelectorAll('.package-card')].find(c=>c.dataset.packId==="+JsonSerializer.Serialize(pack.Manifest.Id)+" && c.dataset.packVersion==="+JsonSerializer.Serialize(version)+")";
        async Task<bool> Verify(string script)=>await browser.CoreWebView2.ExecuteScriptAsync(script)=="true";
        bool missing=await Verify("(()=>{const c="+Card(pack.Manifest.Version)+";return c?.textContent.includes('后端不可用') && c.textContent.includes('导入所需版本') && c.querySelector('button').disabled;})()");
        await CaptureLayout("v2-missing-backend");
        new BackendStore(dataRoot).Install(paths["backend"]);FullState();await Task.Delay(200);
        bool ready=await Verify("(()=>{const c="+Card(pack.Manifest.Version)+";return c?.textContent.includes('后端已安装') && c.textContent.includes('参数格式 2') && !c.querySelector('button').disabled;})()");
        var active=workspace.Pack!.Reference;
        await browser.CoreWebView2.ExecuteScriptAsync(Card(pack.Manifest.Version)+".querySelector('button').click()");await Task.Delay(300);
        bool selected=workspace.Packages.GetCurrent(pack.Manifest.Id)?.Reference==pack.Reference && workspace.Pack.Reference==active && workspace.Connected;
        var invalid=workspace.Packages.Install(paths["invalidPack"]);FullState();await Task.Delay(200);
        bool invalidDisabled=await Verify("(()=>{const c="+Card(invalid.Manifest.Version)+";return c?.textContent.includes('unknownOperation') && c.querySelector('button').disabled;})()");
        await HandleMessage(Wire.Element(new{action="setCurrent",id=invalid.Manifest.Id,version=invalid.Manifest.Version,manifestSha256=invalid.Reference.ManifestSha256}));await Task.Delay(200);
        bool preserved=workspace.Packages.GetCurrent(pack.Manifest.Id)?.Reference==pack.Reference && workspace.Pack.Reference==active && workspace.Connected;
        bool guidance=await Verify("document.getElementById('errorText').textContent.includes('适配作者') && document.getElementById('errorText').textContent.includes('unknownOperation')");
        await CaptureLayout("v2-invalid-parameters");
        // A known older package keeps its own fixed dependency when selected for the next session.
        new BackendStore(dataRoot).Install(paths["olderBackend"]);
        var previous=workspace.Packages.Install(paths["olderPack"]);FullState();await Task.Delay(200);
        await browser.CoreWebView2.ExecuteScriptAsync(Card(previous.Manifest.Version)+".querySelector('button').click()");await Task.Delay(300);
        bool rollback=workspace.Packages.GetCurrent(pack.Manifest.Id)?.Reference==previous.Reference && workspace.Pack.Reference==active && workspace.Connected &&
            previous.Manifest.Profiles[0].Backend.Version!=pack.Manifest.Profiles[0].Backend.Version &&
            new BackendStore(dataRoot).Describe(previous.Manifest.Profiles[0].Backend).Version==previous.Manifest.Profiles[0].Backend.Version;
        string malformed=Path.Combine(smokeDirectory!,"bad-dependency.gpack");File.Copy(paths["pack"],malformed);
        using(var zip=ZipFile.Open(malformed,ZipArchiveMode.Update))
        {
            var entry=zip.GetEntry("manifest.json")!;JsonNode manifest;using(var reader=new StreamReader(entry.Open()))manifest=JsonNode.Parse(reader.ReadToEnd())!;
            entry.Delete();manifest["version"]="0.3.97";manifest["profiles"]![0]!["backend"]!["id"]="../invalid";
            using var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open());writer.Write(manifest.ToJsonString());
        }
        workspace.Packages.Install(malformed);FullState();await Task.Delay(200);
        bool isolated=await Verify("(()=>{const c="+Card("0.3.97")+";return c?.textContent.includes('后端身份无效') && c.querySelector('button').disabled && !!"+Card(previous.Manifest.Version)+";})()");
        bool noOverflow=await Verify("document.querySelector('.content').scrollWidth<=document.querySelector('.content').clientWidth");
        await CaptureLayout("v2-current-rollback");lastError="";operationError=null;FullState();
        return missing && ready && selected && invalidDisabled && preserved && guidance && rollback && isolated && noOverflow;
    }
    async Task CaptureLayout(string name)
    {
        using var image = File.Create(Path.Combine(smokeDirectory!, name + ".png"));
        await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
    }
}
