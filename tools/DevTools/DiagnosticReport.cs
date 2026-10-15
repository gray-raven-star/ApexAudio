using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApexAudio.Contracts;

sealed class DiagnosticContext
{
    public string Command { get; set; } = "help";
    public string Stage { get; set; } = "arguments";
    public string? Scratch { get; set; }
    public string? InstalledPackDirectory { get; set; }
    public string? EntryPointPath { get; set; }
    public string[] ModulePaths { get; set; } = [];
    public bool AdapterExecuted { get; set; }
    public bool ConnectionAttempted { get; set; }
    public bool GameConnected { get; set; }
    public string Cleanup { get; set; } = "not-needed";
    public string? CleanupError { get; set; }
}

static class DiagnosticReport
{
    public static void Write(string[] args, DiagnosticContext context, object? value, Exception? failure)
    {
        var report = value == null ? new JsonObject() : JsonSerializer.SerializeToNode(value, Wire.Json)!.AsObject();
        report["reportVersion"] = 1; report["command"] = context.Command; report["stage"] = context.Stage;
        report["outcome"] = failure == null && context.CleanupError == null && Environment.ExitCode == 0 ? "completed" : "failed";
        report["adapterExecuted"] = context.AdapterExecuted; report["gameConnected"] = context.GameConnected;
        report["connectionAttempted"] = context.ConnectionAttempted;
        report["moduleLoadState"] = context.ConnectionAttempted ? "not-observed; modules may remain until target exit" : "not-attempted";
        report["scratch"] = context.Scratch; report["installedPackDirectory"] = context.InstalledPackDirectory;
        report["entryPointPath"] = context.EntryPointPath; report["modulePaths"] = JsonSerializer.SerializeToNode(context.ModulePaths);
        long? bytes = null;
        try { if (context.Scratch != null && Directory.Exists(context.Scratch)) bytes = Directory.EnumerateFiles(context.Scratch, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        report["cleanup"] = JsonSerializer.SerializeToNode(new { adapter = context.Cleanup, error = context.CleanupError, temporaryFilesRetained = context.Scratch != null, bytes,
            reason = context.ConnectionAttempted ? "Keep package files: the target may still have its DLL loaded. Remove this exact scratch directory only after the target exits." : "Retained for inspection; this exact scratch directory can be removed after the command exits." }, Wire.Json);
        if (failure != null)
        {
            report["error"] = failure.Message; report["type"] = failure.GetType().Name; report["hresult"] = failure.HResult;
            Win32Exception? native = null;
            for (Exception? current = failure; current != null; current = current.InnerException) if (current is Win32Exception win32) { native = win32; break; }
            report["nativeErrorCode"] = native?.NativeErrorCode;
            report["failureKind"] = failure switch { FileNotFoundException or DirectoryNotFoundException => "file-not-found", UnauthorizedAccessException => "access-denied", BadImageFormatException or DllNotFoundException => "module-load", ArgumentException => "arguments", InvalidDataException => "invalid-data", _ when native?.NativeErrorCode == 5 => "access-denied", _ => "operation-failed" };
        }
        int output = Array.IndexOf(args, "--output");
        if (output >= 0 && output + 1 < args.Length)
        {
            try
            {
                string path = Path.GetFullPath(args[output + 1]); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, report.ToJsonString(Wire.Json));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                report["reportWriteError"] = e.Message; Environment.ExitCode = 1;
            }
        }
        (failure != null || context.CleanupError != null || Environment.ExitCode != 0 || report.ContainsKey("reportWriteError") ? Console.Error : Console.Out).WriteLine(report.ToJsonString(Wire.Json));
    }
}
