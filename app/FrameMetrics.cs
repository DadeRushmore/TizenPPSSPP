using System;
using System.Globalization;

// Monotonic CPU observations at the native retro_run boundary, not GPU timing.
internal sealed class FrameMetrics
{
    const int Capacity=600;
    readonly double[] runs=new double[Capacity],spacing=new double[Capacity],ended=new double[Capacity],liveScratch=new double[Capacity];
    int runIndex,runCount,spacingIndex,spacingCount;
    long totalRuns;
    int liveCount;
    double started=-1,previous=-1;
    internal void Begin(double now) {if(previous>=0){spacing[spacingIndex]=Math.Max(0,now-previous);spacingIndex=(spacingIndex+1)%Capacity;spacingCount=Math.Min(Capacity,spacingCount+1);}previous=started=now;}
    internal void End(double now) {if(started<0)return;runs[runIndex]=Math.Max(0,now-started);ended[runIndex]=now;runIndex=(runIndex+1)%Capacity;runCount=Math.Min(Capacity,runCount+1);liveCount=Math.Min(Capacity,liveCount+1);totalRuns++;started=-1;}
    internal void Pause() {previous=started=-1;liveCount=0;}
    internal long TotalRuns=>totalRuns;
    // Called at HUD refresh rate, never on the per-frame path. Keep the diagnostic
    // 600-call window intact; the overlay needs a short wall-time window instead.
    internal double LiveP95Milliseconds(double now) {
        int count=0;
        for(int i=0;i<liveCount;i++) {
            int at=(runIndex-1-i+Capacity)%Capacity;
            if(ended[at]<now-2)break;
            if(ended[at]<=now)liveScratch[count++]=runs[at];
        }
        if(count==0)return double.NaN;
        Array.Sort(liveScratch,0,count);
        return liveScratch[(int)Math.Ceiling(count*.95)-1]*1000;
    }
    static string F(double value)=>value.ToString("F2",CultureInfo.InvariantCulture);
    static string Distribution(double[] source,int count) {
        if(count==0)return "n/a";
        var values=new double[count];Array.Copy(source,values,count);Array.Sort(values);
        return F(values[(int)Math.Ceiling(count*.50)-1]*1000)+"/"+F(values[(int)Math.Ceiling(count*.95)-1]*1000)+"/"+
            F(values[(int)Math.Ceiling(count*.99)-1]*1000)+"/"+F(values[count-1]*1000);
    }
    internal string Summary(double frameSeconds,uint video,uint duplicates) {
        double total=0;int late=0;for(int i=0;i<spacingCount;i++){total+=spacing[i];if(spacing[i]>frameSeconds*1.5)late++;}
        return "FRAME_TIMING runs="+totalRuns+" hw_submissions="+video+" duplicate_callbacks="+duplicates+
            " target_ms="+F(frameSeconds*1000)+" recent_runs="+runCount+" spacing_samples="+spacingCount+
            " run_ms_p50_p95_p99_max="+Distribution(runs,runCount)+" spacing_ms_p50_p95_p99_max="+Distribution(spacing,spacingCount)+
            " run_cadence_pct="+(total>0?F(100*frameSeconds*spacingCount/total):"n/a")+" spacing_over_1_5x="+late;
    }
}
