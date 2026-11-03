using System.Text.Json;
using ApexAudio.Application;
using ApexAudio.Contracts;
using ApexAudio.Core;
using ApexAudio.XHaptics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ApexAudio.Desktop;

sealed partial class MainWindow : Form
{
    readonly WebView2 browser = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(12, 17, 27) };
    readonly AudioWorkspace workspace; readonly string dataRoot;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    readonly SemaphoreSlim operations = new(1);
    readonly HashSet<string> trusted = new();
    AppSettings settings;
    List<DetectedGame> games = new(); int ticks; bool ready, busy, closing, closed, detecting, autoConnect = true;
    string? autoSuppressed; string lastError = ""; long nextConnectionRetry;
    OperationError? operationError;
    readonly string? smokeDirectory;
    public MainWindow(string root, string? smoke = null)
    {
        dataRoot = root; smokeDirectory = smoke; workspace = new(root,new ApexAudio.Fmod.Definition.FmodConfigurationValidator());
        Text = "Apex Audio · 声音采集"; Size = new(1380, 900); MinimumSize = new(900, 650); StartPosition = FormStartPosition.CenterScreen;
        Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "assets", "apex.ico")); Controls.Add(browser);
        string trust = Path.Combine(root, "trusted.json"); if (File.Exists(trust)) foreach (string key in JsonSerializer.Deserialize<string[]>(File.ReadAllText(trust), Wire.Json) ?? Array.Empty<string>()) trusted.Add(key);
        settings = AppSettings.Load(root); workspace.Feedback.Gain = settings.Gain; workspace.Feedback.Port = settings.Port; autoConnect = settings.AutoConnect;
        if (string.IsNullOrWhiteSpace(settings.XhExecutable)) settings.XhExecutable = FindXhExecutable();
        inputTimer.Tick += (_, _) => PollController();
        workspace.ConnectionLost += (connectionId, reason) => BeginInvoke(new Action(async () =>
        {
            if (closing) return;
            await Run(async () => { if (connectionId == workspace.ConnectionId) await RecoverConnection(reason); }, reportError: false);
        }));
        Shown += async (_, _) =>
        {
            double scale = DeviceDpi / 96.0; var area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new(Math.Min(area.Width, (int)(900 * scale)), Math.Min(area.Height, (int)(650 * scale)));
            Size = new(Math.Min(area.Width - 40, (int)(1280 * scale)), Math.Min(area.Height - 40, (int)(840 * scale)));
            CenterToScreen(); ApplyAppearance(); try { RegisterRecordingHotkey(); } catch (IOException error) { lastError = error.Message; } inputTimer.Start(); await Initialize();
        }; timer.Tick += async (_, _) => await Tick();
        FormClosing += async (_, e) =>
        {
            if (closed) return; e.Cancel = true; if (closing) return; closing = true; timer.Stop(); inputTimer.Stop(); UnregisterHotKey(Handle, RecordingHotkeyId); recordingTimer?.Cancel();
            await operations.WaitAsync();
            try { await workspace.DisposeAsync(); await controller.DisposeAsync(); } catch (Exception error) { File.AppendAllText(Path.Combine(root, "diagnostics.log"), error + Environment.NewLine); }
            finally { operations.Release(); closed = true; Close(); }
        };
    }
    async Task Initialize()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(dataRoot, "webview"));
            await browser.EnsureCoreWebView2Async(environment);
            var core = browser.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("apex-audio.local", Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith("https://apex-audio.local/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true; };
            core.WebMessageReceived += async (_, e) =>
            {
                if (!e.Source.StartsWith("https://apex-audio.local/", StringComparison.OrdinalIgnoreCase)) return;
                try { using var doc = JsonDocument.Parse(e.WebMessageAsJson); await HandleMessage(doc.RootElement.Clone()); }
                catch (Exception error) { lastError = error.Message; Post(new { type = "error", message = error.Message }); }
            };
            long uiVersion = Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "ui"), "*", SearchOption.AllDirectories).Max(f => File.GetLastWriteTimeUtc(f).Ticks);
            core.Navigate("https://apex-audio.local/index.html?v=" + uiVersion); timer.Start();
        }
        catch (Exception e) { MessageBox.Show(e.Message, "界面加载失败"); Close(); }
    }
    void Post(object value) { if (ready && !closing) browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value, Wire.Json)); }
    void FullState() => Post(new
    {
        type = "state", busy, autoConnect, error = lastError, status = workspace.Status, connected = workspace.Connected,
        packs = workspace.Packages.Current().Concat(workspace.Pack == null ? Array.Empty<InstalledPack>() : new[]{workspace.Pack}).DistinctBy(p=>p.Manifest.Id).Where(p => smokeDirectory != null || !p.Manifest.Demo).Select(p => p.Manifest), packId = workspace.Pack?.Manifest.Id,
        packVersion=workspace.Pack?.Manifest.Version, operationError,
        packVersions=workspace.Packages.ListVersions().Where(v=>smokeDirectory!=null || !v.Pack.Manifest.Demo).Select(v=>new{
            v.Pack.Manifest.Id,v.Pack.Manifest.Name,v.Pack.Manifest.Version,v.Pack.Manifest.Author,v.Pack.Manifest.Source,
            manifestSha256=v.Pack.Reference.ManifestSha256,kind=v.Pack.Manifest.SchemaVersion==1?"包含独立程序":"JSON 共享后端配置",
            v.Current,v.InUse,v.Ready,v.Issues,dependencies=v.Pack.Manifest.Profiles.Select(p=>DescribeDependency(p,v.Backends)).ToArray()}),
        packageCatalogIssues=workspace.Packages.CatalogIssues,
        groups = workspace.Groups, preset = workspace.Preset, presetNotice=workspace.PresetNotice,dirty = workspace.Dirty,
        presets = workspace.Pack == null ? new List<PresetOption>() : workspace.Presets.List(workspace.Pack),
        capabilities = workspace.Capabilities, recordings = workspace.Recordings.List(), take = workspace.Take,
        games, feedback = workspace.Feedback.Enabled, gain = workspace.Feedback.Gain, port = workspace.Feedback.Port,
        recording = workspace.Recordings.IsRecording, recordingPhase = RecordingPhase, settings, feedbackDetail = FeedbackStatus
    });
    static object DescribeDependency(CaptureProfile profile,IReadOnlyList<ResolvedBackend>? backends)
    {
        var description=backends?.FirstOrDefault(b=>b.Dependency==profile.Backend)?.Description;
        var parameters=description?.Support?.Parameters.GetValueOrDefault(profile.Backend.Variant);
        return new{profileId=profile.Id,profile.Backend.Id,profile.Backend.Version,profile.Backend.Variant,
            available=description!=null,parameterVersion=parameters?.Version};
    }
    async Task Run(Func<Task> work, bool reportError = true)
    {
        await operations.WaitAsync();
        busy = true; lastError = "";operationError=null;
        try { FullState();await work(); }
        catch (Exception e) { lastError = e.Message; operationError=(e as OperationException)?.Error; if (reportError) Post(new { type = "error", message = e.Message,operationError }); }
        finally
        {
            busy = false;operations.Release();
            try {FullState();}catch(Exception e){lastError=e.Message;Post(new{type="error",message=e.Message});}
        }
    }
    bool Trust(InstalledPack pack)
    {
        string key = pack.Manifest.Id + ":" + PackageStore.Hash(Path.Combine(pack.Directory, "manifest.json"));
        if (trusted.Contains(key)) return true;
        string behavior = pack.Manifest.SchemaVersion == 2 ? "即将使用配置包指定的共享后端连接游戏。" : "即将运行适配包中的程序。";
        if (MessageBox.Show(this, $"{behavior}\n\n{pack.Manifest.Name}  {pack.Manifest.Version}\n作者：{pack.Manifest.Author}\n来源：{pack.Manifest.Source}\n\n请仅运行你信任的适配包。", "首次运行适配包", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return false;
        trusted.Add(key); PresetStore.AtomicWrite(Path.Combine(dataRoot, "trusted.json"), trusted.ToArray()); return true;
    }
    async Task Connect()
    {
        string id=workspace.Pack?.Manifest.Id ?? throw new InvalidOperationException("请选择适配包。");
        var pack=workspace.Packages.GetCurrent(id) ?? throw new OperationException("package_invalid","请先选择当前默认版本。","choose_package_version");
        var selected=pack.Reference;if (!Trust(pack)) return;
        if (pack.Manifest.Demo) { await workspace.Connect(0, null,selected); return; }
        var match = games.Where(g => g.PackIds.Contains(pack.Manifest.Id) && g.VersionMatched).ToList();
        if (match.Count == 0) throw new IOException("没有找到版本匹配的游戏进程，请启动游戏或更新适配包。");
        if (match.Count > 1) throw new IOException("检测到多个匹配进程，请只保留一个游戏实例。");
        await workspace.Connect(match[0].ProcessId, match[0].Path,selected);
        if (!workspace.Dirty) RestorePresetSelection(); RememberSelection(); nextConnectionRetry = 0; await MaintainFeedback();
    }
    async Task HandleMessage(JsonElement message)
    {
        string action = message.GetProperty("action").GetString()!;
        string Text(string key) => message.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
        bool Bool(string key) => message.TryGetProperty(key, out var v) && v.GetBoolean();
        if (action == "ready") { ready = true; var packs = workspace.Packages.Current().Where(p => !p.Manifest.Demo).ToList(); var initial = packs.FirstOrDefault(p => p.Manifest.Id == settings.LastPackId) ?? packs.FirstOrDefault(); if (initial != null) await workspace.SelectPack(initial.Manifest.Id); FullState(); if (smokeDirectory != null) _ = Smoke(); return; }
        await Run(async () =>
        {
            switch (action)
            {
                case "selectPack": await EndRecording(); await workspace.SelectPack(Text("id")); autoSuppressed = null; nextConnectionRetry = 0; RememberSelection(); break;
                case "setCurrent":
                    workspace.Packages.SelectCurrent(Text("id"),new(Text("id"),Text("version"),Text("manifestSha256")));
                    if(!workspace.Connected && !workspace.Dirty) await workspace.SelectPack(Text("id"));
                    await RefreshGames();break;
                case "clearCurrent": workspace.Packages.SelectCurrent(Text("id"),null);await RefreshGames();break;
                case "removeVersion":
                    if(MessageBox.Show(this,"移除此版本？用户预设和录音会保留。","移除版本",MessageBoxButtons.OKCancel)==DialogResult.OK)
                    {
                        workspace.Packages.RemoveVersion(new(Text("id"),Text("version"),Text("manifestSha256")));
                        if(workspace.Pack?.Manifest.Id==Text("id") && workspace.Pack.Manifest.Version==Text("version")) await workspace.ClearPack();
                    }
                    await RefreshGames();break;
                case "connect": autoSuppressed = null; nextConnectionRetry = 0; await RefreshGames(); await Connect(); break;
                case "disconnect": autoSuppressed = workspace.Pack?.Manifest.Id; await EndRecording(); await workspace.Disconnect(); feedbackDetail = "反馈已关闭"; break;
                case "install":
                    using (var dialog = new OpenFileDialog { Filter = "适配包与共享后端 (*.gpack;*.gbackend)|*.gpack;*.gbackend", InitialDirectory = Path.Combine(AppContext.BaseDirectory, "packages") })
                    {
                        if (dialog.ShowDialog(this) != DialogResult.OK) break;
                        if (Path.GetExtension(dialog.FileName).Equals(".gbackend", StringComparison.OrdinalIgnoreCase))
                        {
                            if (MessageBox.Show(this,"导入共享采集后端？后端包含可执行程序，请确认文件来源可信。","导入共享后端",MessageBoxButtons.OKCancel)!=DialogResult.OK) break;
                            await Task.Run(()=>new BackendStore(dataRoot).Install(dialog.FileName));
                            MessageBox.Show(this,"共享后端已导入，可以连接对应的 V2 适配包。","导入完成"); break;
                        }
                        var manifest = workspace.Packages.Inspect(dialog.FileName);
                        string contents = manifest.SchemaVersion == 2 ? "V2 配置包需要对应版本的共享后端。" : "适配包包含可执行程序。";
                        if (MessageBox.Show(this, $"安装 {manifest.Name} {manifest.Version}\n作者：{manifest.Author}\n来源：{manifest.Source}\n\n{contents}", "导入适配包", MessageBoxButtons.OKCancel) != DialogResult.OK) break;
                        await Task.Run(() => workspace.Packages.Install(dialog.FileName));
                        MessageBox.Show(this,"版本已安装。请在游戏适配包页面明确选择当前默认版本。进行中的会话不受影响。","导入完成");
                    }
                    break;
                case "uninstall":
                    if (MessageBox.Show(this, "卸载适配包？用户预设和录音会保留。", "卸载", MessageBoxButtons.OKCancel) == DialogResult.OK)
                    { workspace.Packages.Uninstall(Text("id")); }
                    break;
                case "group": workspace.SetGroup(Text("id"), Bool("enabled")); break;
                case "folderToggle": workspace.SetFolder(Text("id"), Bool("enabled")); break;
                case "editGroup": workspace.EditGroup(Text("id"), Text("name"), string.IsNullOrEmpty(Text("folderId")) ? null : Text("folderId"), message.GetProperty("order").GetInt32()); break;
                case "renameTrack": workspace.RenameTrack(Text("takeId"), Text("id"), Text("name")); break;
                case "addFolder": workspace.AddFolder(Text("name")); break;
                case "editFolder": workspace.EditFolder(Text("id"), Text("name"), Bool("delete")); break;
                case "loadPreset": workspace.LoadPreset(Text("id")); break;
                case "savePreset": workspace.SavePreset(Text("name"), Bool("copy")); break;
                case "restorePreset": workspace.RestorePreset(); break;
                case "deletePreset": workspace.Presets.Delete(workspace.Pack!, Text("id")); var first = workspace.Presets.List(workspace.Pack!).FirstOrDefault(); if (first != null) workspace.LoadPreset(first.Id); break;
                case "exportPreset":
                    using (var dialog = new SaveFileDialog { Filter = "声音预设 (*.json)|*.json", FileName = "声音预设.json" }) if (dialog.ShowDialog(this) == DialogResult.OK) PresetStore.AtomicWrite(dialog.FileName, workspace.Preset);
                    break;
                case "importPreset":
                    using (var dialog = new OpenFileDialog { Filter = "声音预设 (*.json)|*.json" }) if (dialog.ShowDialog(this) == DialogResult.OK) { var preset = workspace.Presets.Import(workspace.Pack!, dialog.FileName, workspace.Groups); workspace.LoadPreset(preset.Id); }
                    break;
                case "record": await ToggleRecording(); break;
                case "take": await Task.Run(() => workspace.SelectTake(Text("id"))); break;
                case "clip":
                    byte[] clip = await Task.Run(() => workspace.Recordings.Clip(Text("sessionId"), Text("groupId"), message.GetProperty("start").GetDouble(), message.GetProperty("end").GetDouble(), message.GetProperty("gain").GetSingle()));
                    Post(new { type = "clip", requestId = Text("requestId"), wav = Convert.ToBase64String(clip) }); break;
                case "feedback":
                    feedbackSuppressed = !Bool("enabled"); settings.AutoFeedback = Bool("enabled"); settings.Save(dataRoot);
                    if (Bool("enabled")) { if (!workspace.Connected) throw new IOException("请先连接声音采集。"); nextFeedbackRetry = 0; await MaintainFeedback(); } else { await workspace.Feedback.Stop(); feedbackDetail = "反馈已关闭"; } break;
                case "settings":
                    var next = Wire.Read<AppSettings>(message.GetProperty("settings")); next.LastPackId = settings.LastPackId; next.LastPresets = settings.LastPresets; next.Validate();
                    var previous = settings; settings = next;
                    try { RegisterRecordingHotkey(); } catch { settings = previous; RegisterRecordingHotkey(); throw; }
                    if (workspace.Feedback.Port != next.Port) await workspace.Feedback.Stop(); settings.Save(dataRoot);
                    workspace.Feedback.Gain = next.Gain; workspace.Feedback.Port = next.Port; autoConnect = next.AutoConnect; feedbackSuppressed = !next.AutoFeedback; nextFeedbackRetry = 0; ApplyAppearance();
                    if (!next.AutoFeedback) { await workspace.Feedback.Stop(); feedbackDetail = "反馈已关闭"; } else await MaintainFeedback(); Post(new { type = "settingsSaved" }); break;
                case "theme": settings.Theme = Text("theme"); settings.Validate(); settings.Save(dataRoot); ApplyAppearance(); break;
                case "chooseXh":
                    using (var dialog = new OpenFileDialog { Filter = "X-Haptics 程序|xHaptics Engine.exe;x-haptic-engine-fusion.exe" }) if (dialog.ShowDialog(this) == DialogResult.OK) { settings.XhExecutable = dialog.FileName; var support=EngineApi.Supported.SingleOrDefault(s=>s.Sha256.Equals(PackageStore.Hash(dialog.FileName),StringComparison.OrdinalIgnoreCase));if(support!=null)settings.Port=support.Port;settings.Save(dataRoot); } break;
                case "learnController": learningController = true; learningUntil = Environment.TickCount64 + 15000; controller.Enabled = true; controllerPressed = controller.Poll() != 0; Post(new { type = "hotkeyLearn", listening = true, message = "请短按要绑定的手柄按键" }); break;
                case "clearController": learningController = false; settings.ControllerMask = 0; settings.ControllerName = "未绑定"; controller.Enabled = false; settings.Save(dataRoot); Post(new { type = "hotkeyLearn", listening = false, message = "未绑定" }); break;
                case "keyboardLearn": UnregisterHotKey(Handle, RecordingHotkeyId); break;
                case "keyboardLearnEnd": RegisterRecordingHotkey(); break;
                case "refresh": await RefreshGames(); break;
                case "openData": System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dataRoot) { UseShellExecute = true }); break;
            }
            if (action is "savePreset" or "loadPreset" or "restorePreset" or "importPreset") RememberSelection();
        });
    }
    async Task RefreshGames() { var packs = workspace.Packages.Current(); games = await Task.Run(() => GameDetector.Detect(packs)); }
    async Task RecoverConnection(string reason)
    {
        recordingTimer?.Cancel(); countdownEnd = 0;
        await workspace.Disconnect();
        nextConnectionRetry = Environment.TickCount64 + 5000; nextFeedbackRetry = 0;
        feedbackDetail = "等待采集恢复";
        lastError = autoConnect && autoSuppressed == null ? reason + " 将自动重新连接。" : reason;
    }
    async Task Tick()
    {
        if (!ready || closing) return;
        Post(new { type = "activity", activity = workspace.Activity(), recording = workspace.Recordings.IsRecording, duration = workspace.Recordings.Frames / 48000.0, recordingPhase = RecordingPhase, countdown = Countdown, feedback = workspace.Feedback.Enabled, delivered = workspace.Connected && workspace.Feedback.Delivered && workspace.Feedback.EngineAlive, feedbackDetail = FeedbackStatus });
        if (++ticks % 30 != 0 || busy || detecting) return;
        detecting = true;
        try
        {
            await RefreshGames();
            if (workspace.Connected)
            {
                if (await operations.WaitAsync(0)) { try { await workspace.Poll(); await MaintainFeedback(); } catch (Exception error) { await RecoverConnection(error.Message); } finally { operations.Release(); } }
            }
            else if (autoConnect && autoSuppressed == null && Environment.TickCount64 >= nextConnectionRetry)
            {
                var matches = games.Where(g => g.VersionMatched).SelectMany(g => g.PackIds).Distinct().ToArray();
                if (matches.Length == 1)
                {
                    var pack = workspace.Packages.Current().Single(p => p.Manifest.Id == matches[0]);
                    string key = pack.Manifest.Id + ":" + PackageStore.Hash(Path.Combine(pack.Directory, "manifest.json"));
                    if (trusted.Contains(key)) await Run(async () =>
                    {
                        if (workspace.Pack?.Manifest.Id != pack.Manifest.Id) await workspace.SelectPack(pack.Manifest.Id);
                        nextConnectionRetry = Environment.TickCount64 + 5000;
                        await Connect();
                    }, reportError: false);
                }
            }
            FullState();
        }
        catch (Exception e) { lastError = e.Message; }
        finally { detecting = false; }
    }
}
