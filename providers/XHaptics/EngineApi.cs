using System.Net.Http.Headers;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApexAudio.Contracts;

namespace ApexAudio.XHaptics;

public sealed record EngineSupport(string ProcessName, string Sha256, string Kind, int Port);
public sealed record DiagnosticReading(JsonElement Payload, long RequestedTick, long ReceivedTick);

// Driver version differences stay here; game adapters only provide audio.
public sealed class EngineApi : IDisposable
{
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(3) };
    readonly Func<int> port;
    readonly SemaphoreSlim configuration = new(1);
    string? accessToken, diagnosticsToken; string scope = "global"; int processId;
    public string SessionFile { get; set; } = "";
    public bool Formal { get; private set; }
    public EngineApi(Func<int> port) { this.port = port; }
    public static EngineSupport[] Supported => JsonSerializer.Deserialize<EngineSupport[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"native","xh-engines.json")),Wire.Json)!;
    public void Bind(EngineSupport support, int pid) { Formal = support.Kind == "formal"; processId = pid; accessToken = null; diagnosticsToken = null; }
    public void SaveLaunch(int pid, string launchToken)
    {
        processId = pid; accessToken = null;
        if (!string.IsNullOrEmpty(SessionFile)) File.WriteAllText(SessionFile, JsonSerializer.Serialize(new { processId = pid, launchToken }, Wire.Json));
    }
    async Task Authenticate()
    {
        if (!Formal || accessToken != null) return;
        JsonElement cache = default;
        if (File.Exists(SessionFile)) { using var document=JsonDocument.Parse(File.ReadAllText(SessionFile));cache=document.RootElement.Clone(); }
        if (cache.ValueKind != JsonValueKind.Object || cache.GetProperty("processId").GetInt32() != processId)
            throw new IOException("请关闭正式 Engine，再由声音工具自动启动，以建立连接授权。");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port()}/api/engine/version");
        request.Headers.Add("Origin", $"http://127.0.0.1:{port()}");
        request.Headers.Add("X-Protocol-Version", "1");
        if (cache.TryGetProperty("accessToken", out var saved)) request.Headers.Authorization = new("Bearer",saved.GetString());
        else request.Headers.Add("X-Engine-Launch-Token", cache.GetProperty("launchToken").GetString());
        using var response=await http.SendAsync(request); using var payload=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!response.IsSuccessStatusCode) throw new IOException("正式 Engine 授权已失效，请关闭 Engine 后由声音工具重新启动。");
        accessToken=payload.RootElement.GetProperty("accessToken").GetString();
        File.WriteAllText(SessionFile,JsonSerializer.Serialize(new {processId,accessToken},Wire.Json));
    }
    public async Task<JsonElement> Request(string path, HttpMethod? method=null, object? body=null, CancellationToken token=default)
    {
        await Authenticate();
        using var request=new HttpRequestMessage(method??HttpMethod.Get,$"http://127.0.0.1:{port()}{path}");
        if (Formal) { request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",accessToken); request.Headers.Add("Origin",$"http://127.0.0.1:{port()}"); }
        if (body != null) request.Content=new StringContent(JsonSerializer.Serialize(body,Wire.Json),System.Text.Encoding.UTF8,"application/json");
        using var response=await http.SendAsync(request,token);using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (!response.IsSuccessStatusCode)
        {
            string message=document.RootElement.TryGetProperty("message",out var text)?text.GetString()??"":response.ReasonPhrase??"";
            throw new IOException("X-Haptics："+message);
        }
        return document.RootElement.Clone();
    }
    public async Task<bool> InputEnabled()
    {
        if (!Formal) return (await Request("/api/info")).GetProperty("engine").GetProperty("inputEnabled").GetBoolean();
        var status=await Request("/api/xhaptics/status");
        var session=status.GetProperty("session");
        if (session.GetProperty("state").GetString()=="Running" && session.GetProperty("source").GetString()!="automatic")
            throw new IOException("正式 Engine 正被其他客户端控制，请先结束该反馈会话。");
        scope=session.GetProperty("scope").GetString()??"global";if(string.IsNullOrEmpty(scope))scope="global";
        var config=await Request($"/api/v1/xhaptics/realtime-audio/configurations/{Uri.EscapeDataString(scope)}");
        return config.GetProperty("config").GetProperty("enabled").GetBoolean();
    }
    public async Task<DiagnosticReading> ReadDiagnostics(CancellationToken token)
    {
        string origin=$"http://127.0.0.1:{port()}";
        if(diagnosticsToken==null)
        {
            string page=await http.GetStringAsync(origin+"/diagnostics/",token);
            var match=System.Text.RegularExpressions.Regex.Match(page,"<meta name=\"gpa6-ui-token\" content=\"([^\"]+)\"");
            if(!match.Success)throw new IOException("正式 Engine 诊断页面没有提供连接授权。");
            using var bootstrap=new HttpRequestMessage(HttpMethod.Get,origin+"/api/engine/version");
            bootstrap.Headers.Add("Origin",origin);bootstrap.Headers.Add("X-Protocol-Version","1");
            bootstrap.Headers.Add("X-Engine-UI-Token",match.Groups[1].Value);bootstrap.Headers.Add("X-Engine-UI-Path","/diagnostics/");
            using var response=await http.SendAsync(bootstrap,token);response.EnsureSuccessStatusCode();
            using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));diagnosticsToken=doc.RootElement.GetProperty("accessToken").GetString();
        }
        using var request=new HttpRequestMessage(HttpMethod.Get,origin+"/api/diagnostics/runtime-inspection");
        request.Headers.Authorization=new("Bearer",diagnosticsToken);request.Headers.Add("Origin",origin);
        long requested=Stopwatch.GetTimestamp();using var result=await http.SendAsync(request,token);
        if(result.StatusCode==System.Net.HttpStatusCode.Unauthorized)diagnosticsToken=null;
        result.EnsureSuccessStatusCode();using var payload=JsonDocument.Parse(await result.Content.ReadAsStringAsync(token));return new(payload.RootElement.Clone(),requested,Stopwatch.GetTimestamp());
    }
    public async Task SetInput(bool enabled)
    {
        if (!Formal) { await Request(enabled?"/api/input/on":"/api/input/off",HttpMethod.Post,new{});return; }
        await configuration.WaitAsync();
        try
        {
            string path=$"/api/v1/xhaptics/realtime-audio/configurations/{Uri.EscapeDataString(scope)}";
            var snapshot=await Request(path);var config=JsonNode.Parse(snapshot.GetProperty("config").GetRawText())!;
            if (config["enabled"]!.GetValue<bool>()!=enabled)
            {
                config["enabled"]=enabled;
                await Request(path,HttpMethod.Put,new{expectedRevision=snapshot.GetProperty("revision").GetInt64(),config});
            }
            // Stop/restart only when binding the capture bridge, never on group edits.
            for(int i=0;i<20;i++)
            {
                var status=await Request("/api/xhaptics/status");
                if(status.GetProperty("realtimeAudio").GetProperty("wasapi").GetProperty("running").GetBoolean()==enabled)return;
                await Task.Delay(100);
            }
            throw new IOException("正式 Engine 声音输入没有完成切换。");
        }
        finally { configuration.Release(); }
    }
    public void Dispose() {http.Dispose();configuration.Dispose();}
}
