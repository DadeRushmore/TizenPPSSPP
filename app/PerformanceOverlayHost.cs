using System;
using System.IO;
using Tizen.Applications;

internal sealed partial class TizenPspApp
{
    PerformanceOverlayView performanceOverlay;
    PerformanceOverlayPreference performancePreference;
    readonly PerformanceSampler performanceSampler=new PerformanceSampler();
    string performancePreferencePath;
    double performanceNext;
    bool performanceWasActive;
    internal string PerformanceOverlayLabel=>"Performance overlay: "+(performancePreference==null || performancePreference.Visible?"On":"Off");
    void InitializePerformanceOverlay() {
        performancePreferencePath=Path.Combine(Application.Current.DirectoryInfo.Data,"performance-overlay.txt");
        performancePreference=PerformanceOverlayPreference.Load(performancePreferencePath,Log);
        performanceOverlay=new PerformanceOverlayView(window);
    }
    void LayoutPerformanceOverlay(int x,int y,int width,int height) {
        performanceOverlay?.Layout(x,y,width,height);
        UpdatePerformanceOverlay();
    }
    void UpdatePerformanceOverlay() {
        if(performanceOverlay==null)return;
        bool active=performancePreference.Visible && game!=null && !game.Faulted && !reportVisible && !mapperVisible && !paused && !terminating;
        if(!active){HidePerformanceOverlay();return;}
        double now=ControllerReader.Now;
        if(!performanceWasActive){performanceSampler.Reset();performanceNext=0;performanceWasActive=true;}
        performanceOverlay.Show();
        if(now<performanceNext)return;
        performanceNext=now+.5;
        performanceOverlay.Update(performanceSampler.Sample(game.ReadPerformanceCounters()),experiment!=null&&experiment.Active?experiment.Summary+" | Vertex setup comparison":game.PerformanceStatus);
    }
    void HidePerformanceOverlay() {
        performanceOverlay?.Hide();performanceWasActive=false;performanceSampler.Reset();
    }
    void TogglePerformanceOverlay() {
        if(performancePreference==null)return;
        performancePreference.Toggle();
        try{performancePreference.Save(performancePreferencePath);}catch(Exception e){Log("OVERLAY_SETTINGS_SAVE "+e.Message);}
        Log("PERFORMANCE_OVERLAY visible="+performancePreference.Visible+"; FPS=non-null HW video submissions; Emu=retro_run cadence versus requested cadence; p95=recent CPU run duration");
        performanceNext=0;UpdatePerformanceOverlay();
    }
    void DisposePerformanceOverlay(){performanceOverlay?.Dispose();performanceOverlay=null;}
}
