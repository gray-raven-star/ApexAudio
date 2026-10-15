using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace ApexAudio.XHaptics;

public sealed class HapticBlock
{
 public double Seconds{get;set;}
 public double Span{get;set;}
 public float? Left{get;set;} public float? Right{get;set;} public float? Trigger{get;set;}
 public float? SupplementLeft{get;set;} public float? SupplementRight{get;set;} public float? SupplementTrigger{get;set;}
}

public sealed class HapticTimeline
{
 public string Source{get;set;}="";
 public string Note{get;set;}="";
 public bool ApproximateClock{get;set;}
 public double StartUncertaintyMs{get;set;}
 public int MissingBlocks{get;set;}
 public int RequestFailures{get;set;}
 public string SamplingMethod{get;set;}="audio-block";
 public double SampleIntervalMs{get;set;}
 public List<HapticBlock> Blocks{get;set;}=new();
 public bool HasSupplement=>Blocks.Any(b=>b.SupplementLeft.HasValue||b.SupplementRight.HasValue||b.SupplementTrigger.HasValue);
}

// Read the same local telemetry that the official X-Haptics waveform page uses.
// No input, output, device or gain settings are changed by this collector.
public sealed class HapticCollector:IAsyncDisposable
{
 readonly HttpClient http=new(){Timeout=TimeSpan.FromMilliseconds(500),MaxResponseContentBufferSize=4*1024*1024};
 readonly CancellationTokenSource cancel=new();readonly int port;readonly string source;readonly EngineApi? engine;
 readonly List<(double CaptureUs,HapticBlock Block)> frames=new();
 Task? worker;long cursor;int missing,failures;bool clockSeen;string lastError="";
 public HapticCollector(int port,string source,EngineApi? engine=null){this.port=port;this.source=source;this.engine=engine;}
 public void Start()=>worker=Task.Run(Collect);
 async Task Collect()
 {
  while(!cancel.IsCancellationRequested){
   try{
    if(port==18757 && engine!=null){var reading=await engine.ReadDiagnostics(cancel.Token);AcceptFormal(reading.Payload,reading.RequestedTick*1e6/Stopwatch.Frequency,reading.ReceivedTick*1e6/Stopwatch.Frequency);}
    else {string text=await http.GetStringAsync($"http://127.0.0.1:{port}/api/state?since={cursor}&maxBlocks=300&prerollBlocks=20",cancel.Token);
     using var doc=JsonDocument.Parse(text);Accept(doc.RootElement,Stopwatch.GetTimestamp()*1e6/Stopwatch.Frequency);}
   }catch(OperationCanceledException)when(cancel.IsCancellationRequested){break;}
   catch(Exception e)when(e is HttpRequestException or TaskCanceledException or JsonException or IOException){failures++;lastError=e is JsonException or InvalidDataException?"驱动返回的波形格式无法读取":"录制期间未能持续连接 X-Haptics";}
   try{await Task.Delay(port==18757?20:80,cancel.Token);}catch(OperationCanceledException){break;}
  }
 }
 internal void AcceptFormal(JsonElement payload,double requestedUs,double receivedUs)
 {
  if(!payload.TryGetProperty("backendRuntime",out var backend)||!backend.TryGetProperty("realtimeAudio",out var audio))return;
  if(!audio.TryGetProperty("gripDsp",out var grip)||!grip.TryGetProperty("diagnostics",out var diagnostics)||!diagnostics.TryGetProperty("sourceMix",out var mix))return;
  float? Peak(JsonElement node,string name)=>node.TryGetProperty(name,out var v)&&v.TryGetSingle(out float f)&&float.IsFinite(f)?Math.Abs(f):null;
  var b=new HapticBlock{Left=Peak(mix,"gripMixedLeftPeak"),Right=Peak(mix,"gripMixedRightPeak"),SupplementLeft=Peak(mix,"gripAudioLeftPeak"),SupplementRight=Peak(mix,"gripAudioRightPeak")};
  if(audio.TryGetProperty("triggerDsp",out var trigger)&&trigger.TryGetProperty("diagnostics",out var td)&&td.TryGetProperty("sourceMix",out var tm)){b.Trigger=Peak(tm,"triggerMixedPeak");b.SupplementTrigger=Peak(tm,"triggerAudioPeak");}
  if(b.Left==null&&b.Right==null&&b.SupplementLeft==null&&b.SupplementRight==null)return;
  // The API returns one instantaneous diagnostic value, not a waveform block.
  // Its exact engine timestamp is unavailable. Plot a point at the request
  // midpoint; never fill the unobserved interval before it with this value.
  b.Span=0;double captureUs=(requestedUs+receivedUs)/2;
  clockSeen=true;if(frames.Count<20000)frames.Add((captureUs,b));
 }
 internal void Accept(JsonElement payload,double receivedUs)
 {
  if(payload.ValueKind!=JsonValueKind.Object||!payload.TryGetProperty("snapshots",out var list)||list.ValueKind!=JsonValueKind.Array)return;
  var batch=list.EnumerateArray().ToArray();if(batch.Length==0)return;
  // The engine uses a monotonic capture timestamp. Prefer the Windows QPC
  // epoch shared with our recorder. Do not shift old/stale telemetry into a new take.
  var last=batch[^1];double newest=Number(last,"captureMonoUs");
  if(newest>0){double delay=receivedUs-newest;if(delay>=-100_000&&delay<=1_000_000)clockSeen=true;}
  long newestId=(long)Number(last,"blockId");
  if(cursor>0&&newestId>0&&newestId<cursor){lastError="录制期间 X-Haptics 重新启动，部分时间段没有记录";cursor=0;missing++;}
  foreach(var frame in batch){
   if(frame.ValueKind!=JsonValueKind.Object)continue;long id=(long)Number(frame,"blockId");if(id<=cursor)continue;
   if(cursor>0&&id>cursor+1)missing+=(int)Math.Min(100000,id-cursor-1);cursor=id;
   double capture=Number(frame,"captureMonoUs");if(capture<=0)continue;
   if(!frame.TryGetProperty("output",out var output)||output.ValueKind!=JsonValueKind.Object)continue;
   double rate=Number(output,"sampleRateHz");if(rate<=0||rate>192000)continue;
   int count=0;
   float? Peak(string name){
    if(!output.TryGetProperty(name,out var array)||array.ValueKind!=JsonValueKind.Array||array.GetArrayLength()==0)return null;
    if(array.GetArrayLength()>19200)throw new InvalidDataException("波形块过长。");
    count=Math.Max(count,array.GetArrayLength());float peak=0;
    foreach(var value in array.EnumerateArray())if(value.ValueKind==JsonValueKind.Number&&value.TryGetSingle(out float f)&&float.IsFinite(f))peak=Math.Max(peak,Math.Abs(f));
    return peak;
   }
   var b=new HapticBlock{Left=Peak("gripLeftWave"),Right=Peak("gripRightWave"),Trigger=Peak("triggerWave"),SupplementLeft=Peak("hpssGripLeftWave"),SupplementRight=Peak("hpssGripRightWave"),SupplementTrigger=Peak("hpssTriggerWave")};
   if(count==0)continue;b.Span=count/rate;
   if(frames.Count<20000)frames.Add((capture,b));
  }
 }
 static double Number(JsonElement e,string name)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out double n)&&double.IsFinite(n)?n:0;
 public async Task<HapticTimeline> Finish(long originTick,int audioFrames,double uncertaintyMs)
 {
  // Drain the small processing tail, without prolonging recording or changing feedback.
  await Task.Delay(200);cancel.Cancel();if(worker!=null)await worker;
  return MakeResult(originTick,audioFrames,uncertaintyMs);
 }
 internal HapticTimeline MakeResult(long originTick,int audioFrames,double uncertaintyMs)
 {
  var result=new HapticTimeline{Source=source,StartUncertaintyMs=uncertaintyMs,ApproximateClock=true,MissingBlocks=missing,RequestFailures=failures};
  double origin=originTick*1e6/Stopwatch.Frequency,length=audioFrames/48000.0;
  foreach(var (capture,b) in frames){double end=(capture-origin)/1e6;b.Seconds=end-b.Span;if(!clockSeen||end<0||b.Seconds>length)continue;result.Blocks.Add(b);}
  result.Blocks=result.Blocks.OrderBy(b=>b.Seconds).ToList();
  if(port==18757){result.SamplingMethod="instantaneous-poll";if(result.Blocks.Count>1)result.SampleIntervalMs=(result.Blocks[^1].Seconds-result.Blocks[0].Seconds)*1000/(result.Blocks.Count-1);}
  result.Note=result.Blocks.Count==0?"未录到 X-Haptics 波形；请先开启 X-Haptics 和音频输入。":port==18757?
   $"正式 Engine 瞬时诊断峰值，平均采样间隔 {result.SampleIntervalMs:F0} 毫秒；每个点按请求时间近似定位，点与点之间未采集，短促震动可能漏记。不是连续波形或马达实测。":
   "按驱动音频块时间对照；曲线是指令幅度，不是马达实测。";
  if(result.Blocks.Count>0&&!result.HasSupplement)result.Note+=" 此驱动没有提供单独的声音补充分量，当前可查看最终输出。";
  if(result.Blocks.Count>0)result.Note+=port==18757?" 补充分量与合成输出分别记录，幅度不等于手感强度。":" 时间为块级近似对齐，可调整偏移；补充分量不等于最终输出。";
  if(result.MissingBlocks>0||failures>0)result.Note+=" 采集有缺口，空白处不代表未震动。";
  if(!string.IsNullOrWhiteSpace(lastError))result.Note+=" "+lastError+"。";
  return result;
 }
 public async ValueTask DisposeAsync(){cancel.Cancel();if(worker!=null)await worker;http.Dispose();cancel.Dispose();}
}


