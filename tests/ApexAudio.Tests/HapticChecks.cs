using System.Diagnostics;
using System.Text.Json;
using ApexAudio.XHaptics;

static class HapticChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        await using var collector=new HapticCollector(18757,"test");
        using var payload=JsonDocument.Parse("""
        {"backendRuntime":{"realtimeAudio":{
          "gripDsp":{"diagnostics":{"sourceMix":{"gripMixedLeftPeak":0.3,"gripMixedRightPeak":0.2,"gripAudioLeftPeak":0.1,"gripAudioRightPeak":0.05}}},
          "triggerDsp":{"diagnostics":{"sourceMix":{"triggerMixedPeak":0.4,"triggerAudioPeak":0.15}}}
        }}}
        """);
        long origin=Stopwatch.GetTimestamp();double us=origin*1e6/Stopwatch.Frequency;
        collector.AcceptFormal(payload.RootElement,us+20000,us+60000);
        collector.AcceptFormal(payload.RootElement,us+70000,us+90000);
        var result=collector.MakeResult(origin,4800,0);
        check(result.Blocks.Count==2&&result.Blocks.All(b=>b.Span==0)&&Math.Abs(result.Blocks[0].Seconds-.04)<.000001,
            "instantaneous diagnostics use request midpoint without fabricating sustained vibration");
        check(result.Blocks[0].Left==.3f&&result.Blocks[0].SupplementLeft==.1f&&result.Blocks[0].Trigger==.4f&&result.Blocks[0].SupplementTrigger==.15f,
            "supplement and mixed output remain separate at their measured amplitudes");
        check(result.SamplingMethod=="instantaneous-poll"&&Math.Abs(result.SampleIntervalMs-40)<.001&&result.ApproximateClock,
            "diagnostic recording declares its sampling interval and approximate clock");
    }
}
