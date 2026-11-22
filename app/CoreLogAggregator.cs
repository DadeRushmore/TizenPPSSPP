using System;
using System.Collections.Generic;
using System.Globalization;

// Called under GraphicsSessionLog's lock. Only core text is aggregated; host
// lifecycle, loader, frame/audio metrics and JIT boundaries bypass this filter.
internal sealed class CoreLogAggregator
{
    internal const int Capacity=64;
    internal const double WindowSeconds=5;
    readonly Dictionary<string,long> messages=new Dictionary<string,long>(StringComparer.Ordinal);
    double started=-1;
    long overflow,received,originals,suppressed,summaryLines,overflowTotal;
    // INV_PROBE is already bounded at its native source; retain it during unrelated log floods.
    internal static bool IsCore(string message)=>(message.StartsWith("CORE_BRIDGE CORE_LOG ",StringComparison.Ordinal)||message.StartsWith("CORE_BRIDGE [",StringComparison.Ordinal)) && message.IndexOf("INV_PROBE source=",StringComparison.Ordinal)<0;
    internal static bool IsWarningOrError(string message)=>message.StartsWith("CORE_BRIDGE CORE_LOG W ",StringComparison.Ordinal)||
        message.StartsWith("CORE_BRIDGE CORE_LOG E ",StringComparison.Ordinal)||message.StartsWith("CORE_BRIDGE [",StringComparison.Ordinal);
    internal static bool IsError(string message)=>message.StartsWith("CORE_BRIDGE CORE_LOG E ",StringComparison.Ordinal)||message.StartsWith("CORE_BRIDGE [",StringComparison.Ordinal);
    internal void Offer(string message,double now,Action<string> emit) {
        if(started>=0 && now-started>=WindowSeconds)Flush(now,emit);
        if(started<0)started=now;
        received++;
        long count;
        if(messages.TryGetValue(message,out count)){messages[message]=count+1;suppressed++;return;}
        if(messages.Count>=Capacity){overflow++;overflowTotal++;suppressed++;return;}
        messages.Add(message,1);originals++;emit(message);
    }
    internal void Flush(double now,Action<string> emit) {
        if(started<0)return;
        string seconds=Math.Max(0,now-started).ToString("F3",CultureInfo.InvariantCulture);
        foreach(var item in messages)if(item.Value>1) {
            emit("CORE_LOG_REPEAT total="+item.Value+" suppressed="+(item.Value-1)+" window_s="+seconds+" message="+item.Key);summaryLines++;
        }
        if(overflow>0) {
            emit("CORE_LOG_OVERFLOW events="+overflow+" window_s="+seconds+"; distinct-message capacity="+Capacity+"; additional message text not retained");summaryLines++;
        }
        messages.Clear();overflow=0;started=-1;
    }
    internal string Summary=>"CORE_LOG_STATS received="+received+" originals_emitted="+originals+" suppressed="+suppressed+
        " overflow_events="+overflowTotal+" summary_lines="+summaryLines;
}
