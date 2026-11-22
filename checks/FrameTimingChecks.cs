using System;
internal static class FrameTimingChecks
{
    internal static void Run() {
        var metrics=new FrameMetrics();
        if(!metrics.Summary(.02,0,0).Contains("run_cadence_pct=n/a"))throw new Exception("Empty timing is not zero");
        metrics.Begin(0);metrics.End(.005);metrics.Begin(.02);metrics.End(.03);
        string summary=metrics.Summary(.02,2,1);
        if(!summary.Contains("run_ms_p50_p95_p99_max=5.00/10.00/10.00/10.00") || !summary.Contains("run_cadence_pct=100.00") || !summary.Contains("duplicate_callbacks=1"))throw new Exception(summary);
        metrics.Pause();metrics.Begin(20);metrics.End(20.004);
        if(!metrics.Summary(.02,3,1).Contains("spacing_samples=1"))throw new Exception("Pause gap counted as emulation slowdown");
        if(Math.Abs(metrics.LiveP95Milliseconds(20.5)-4)>.001 || !double.IsNaN(metrics.LiveP95Milliseconds(23)))throw new Exception("Live p95 must expire stale samples");
        metrics.Pause();if(!double.IsNaN(metrics.LiveP95Milliseconds(20.5)))throw new Exception("Live p95 must reset across pause");
        for(int i=0;i<1200;i++){metrics.Begin(21+i*.02);metrics.End(21+i*.02+.001);}
        summary=metrics.Summary(.02,1203,1);
        if(!summary.Contains("recent_runs=600") || !summary.Contains("spacing_samples=600") || !summary.Contains("run_ms_p50_p95_p99_max=1.00/1.00/1.00/1.00"))throw new Exception("Unbounded or stale timing window: "+summary);
        Console.WriteLine("PASS: frame timing percentiles, bounded rolling window, cadence, pause gap exclusion.");
        PerformanceOverlayChecks.Run();
    }
}
