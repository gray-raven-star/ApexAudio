using System.Diagnostics;
using System.Runtime.InteropServices;
using ApexAudio.Windows;

namespace ApexAudio.Desktop;

sealed partial class MainWindow
{
    const int RecordingHotkeyId = 101;
    [DllImport("user32", SetLastError = true)] static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32")] static extern bool UnregisterHotKey(nint window, int id);
    readonly FlydigiButtons controller = new();
    readonly System.Windows.Forms.Timer inputTimer = new() { Interval = 20 };
    CancellationTokenSource? recordingTimer;
    long recordingStarted, countdownEnd, lastShortcut; bool stoppingRecording, controllerPressed, learningController;
    long learningUntil;
    string RecordingPhase => countdownEnd > 0 ? "countdown" : workspace.Recordings.IsRecording ? "recording" : stoppingRecording ? "saving" : "idle";
    double Countdown => Math.Max(0, (countdownEnd - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency);
    void RegisterRecordingHotkey()
    {
        UnregisterHotKey(Handle, RecordingHotkeyId);
        if (settings.HotkeyKey != 0 && !RegisterHotKey(Handle, RecordingHotkeyId, settings.HotkeyModifiers | 0x4000u, settings.HotkeyKey))
            throw new IOException("录音快捷键被其他程序占用，请换一个组合。");
        controller.Enabled = learningController || settings.ControllerMask != 0;
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x312 && message.WParam.ToInt32() == RecordingHotkeyId) Shortcut();
        base.WndProc(ref message);
    }
    async void Shortcut()
    {
        if (closing || busy || stoppingRecording) return;
        long now = Environment.TickCount64; if (now - lastShortcut < 350) return; lastShortcut = now;
        await Run(ToggleRecording);
    }
    void PollController()
    {
        ulong mask = controller.Poll();
        if (learningController)
        {
            if (Environment.TickCount64 >= learningUntil) { learningController = false; controller.Enabled = settings.ControllerMask != 0; Post(new { type = "hotkeyLearn", listening = false, message = "没有收到按键，请重试" }); }
            else if (mask != 0 && !controllerPressed)
            {
                settings.ControllerMask = mask; settings.ControllerName = FlydigiButtons.Describe(mask); settings.Save(dataRoot);
                learningController = false; controller.Enabled = true; Post(new { type = "hotkeyLearn", listening = false, message = settings.ControllerName }); FullState();
            }
            controllerPressed = mask != 0; return;
        }
        bool pressed = settings.ControllerMask != 0 && (mask & settings.ControllerMask) == settings.ControllerMask;
        if (pressed && !controllerPressed) Shortcut(); controllerPressed = pressed;
    }
    async Task ToggleRecording()
    {
        if (countdownEnd > 0 || workspace.Recordings.IsRecording) { await EndRecording(); return; }
        if (!workspace.Connected) throw new IOException("请先连接声音采集。");
        recordingTimer?.Dispose(); recordingTimer = new(); var token = recordingTimer.Token;
        if (settings.RecordingDelay > 0)
        {
            countdownEnd = Stopwatch.GetTimestamp() + settings.RecordingDelay * Stopwatch.Frequency;
            _ = BeginAfterDelay(token); return;
        }
        await BeginRecording(token);
    }
    async Task BeginAfterDelay(CancellationToken token)
    {
        try { await Task.Delay(settings.RecordingDelay * 1000, token); if (!closing) await Run(async () => { countdownEnd = 0; if (!token.IsCancellationRequested) await BeginRecording(token); }); }
        catch (OperationCanceledException) { }
    }
    async Task BeginRecording(CancellationToken token)
    {
        await workspace.StartRecording(); recordingStarted = Stopwatch.GetTimestamp();
        if (settings.RecordingSounds && smokeDirectory == null) System.Media.SystemSounds.Asterisk.Play();
        int seconds = settings.RecordingMode == "timed" ? settings.RecordingSeconds : 60;
        _ = EndAfter(seconds, token);
    }
    async Task EndAfter(int seconds, CancellationToken token)
    {
        try { await Task.Delay(seconds * 1000, token); if (!closing) await Run(async () => { if (!token.IsCancellationRequested) await EndRecording(); }); }
        catch (OperationCanceledException) { }
    }
    async Task EndRecording()
    {
        recordingTimer?.Cancel(); countdownEnd = 0;
        if (!workspace.Recordings.IsRecording) return;
        stoppingRecording = true;
        try { await workspace.StopRecording(); if (settings.RecordingSounds && smokeDirectory == null) System.Media.SystemSounds.Beep.Play(); }
        finally { stoppingRecording = false; }
    }
}
