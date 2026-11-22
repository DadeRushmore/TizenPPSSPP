using System.Diagnostics;

internal static partial class NativeGraphics
{
    internal sealed partial class CoreStartup
    {
        internal PerformanceCounters ReadPerformanceCounters() {
            double now=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
            return new PerformanceCounters{Time=now,FrameSeconds=FrameSeconds,Runs=timing.TotalRuns,
                Video=Frames,Duplicates=duplicates?.Invoke()??0,RunP95Ms=timing.LiveP95Milliseconds(now),
                HasGuestClock=guestMillis!=null,GuestMilliseconds=guestMillis?.Invoke()??0,ActualFlips=actualFlips?.Invoke()??0};
        }
        internal string PerformanceStatus=>CpuSummary+" | "+GraphicsSummary+" | "+
            (audio==null?"Audio muted":audio.Failure!=null?"Audio unavailable":audio.Summary);
    }
}
