using System;
using System.Globalization;
using System.IO;

// Counters are sampled on the UI thread. Video counts non-null Libretro hardware
// submissions; it does not measure the TV compositor or unique gameplay frames.
internal struct PerformanceCounters
{
    internal double Time,FrameSeconds,RunP95Ms;
    internal long Runs;
    internal uint Video,Duplicates;
    internal bool HasGuestClock;
    internal uint GuestMilliseconds,ActualFlips;
}

internal struct PerformanceReading
{
    internal bool HasRates;
    internal bool HasGuestRates;
    internal double GuestSpeedPercent,GameFps;
    internal double VideoFps,RunHz,CadencePercent,DuplicateHz,RunP95Ms;
    static string N(double value,string format)=>value.ToString(format,CultureInfo.InvariantCulture);
    internal string Primary=>HasRates?
        (HasGuestRates?"Game "+N(GameFps,"F1")+" FPS   |   Speed "+N(GuestSpeedPercent,"F0")+"%":
        "Video "+N(VideoFps,"F1")+" FPS   |   Calls "+N(CadencePercent,"F0")+"%")+
        "   |   Run p95 "+(double.IsNaN(RunP95Ms)?"--":N(RunP95Ms,"F1"))+" ms":
        "Game -- FPS   |   Speed --%   |   Sampling...";
}

internal sealed class PerformanceSampler
{
    const int Capacity=12;
    readonly PerformanceCounters[] history=new PerformanceCounters[Capacity];
    int next,count;
    internal void Reset(){next=count=0;}
    internal PerformanceReading Sample(PerformanceCounters current) {
        var result=new PerformanceReading{RunP95Ms=current.RunP95Ms};
        if(double.IsNaN(current.Time)||double.IsInfinity(current.Time)||current.FrameSeconds<=0||double.IsNaN(current.FrameSeconds)||double.IsInfinity(current.FrameSeconds)) {Reset();return result;}
        if(count>0) {
            var previous=history[(next+Capacity-1)%Capacity];
            if(current.Time<=previous.Time || current.Runs<previous.Runs || current.Video<previous.Video || current.Duplicates<previous.Duplicates ||
                current.HasGuestClock!=previous.HasGuestClock || (current.HasGuestClock &&
                (current.GuestMilliseconds<previous.GuestMilliseconds || current.ActualFlips<previous.ActualFlips)))Reset();
        }
        // Use up to two seconds of wall time. Never include time spent in menus:
        // the host resets this sampler when the game view is hidden.
        int oldest=(next-count+Capacity)%Capacity;
        while(count>1 && history[(oldest+1)%Capacity].Time<=current.Time-2) {oldest=(oldest+1)%Capacity;count--;}
        if(count>0) {
            var baseline=history[oldest];double seconds=current.Time-baseline.Time;
            if(seconds>=.20) {
                result.HasRates=true;
                result.RunHz=(current.Runs-baseline.Runs)/seconds;
                result.VideoFps=(current.Video-baseline.Video)/seconds;
                result.DuplicateHz=(current.Duplicates-baseline.Duplicates)/seconds;
                result.CadencePercent=100*current.FrameSeconds*result.RunHz;
                if(current.HasGuestClock && baseline.HasGuestClock) {
                    result.HasGuestRates=true;
                    result.GuestSpeedPercent=(current.GuestMilliseconds-baseline.GuestMilliseconds)/(seconds*10);
                    result.GameFps=(current.ActualFlips-baseline.ActualFlips)/seconds;
                }
            }
        }
        history[next]=current;next=(next+1)%Capacity;count=Math.Min(Capacity,count+1);
        return result;
    }
}

internal sealed class PerformanceOverlayPreference
{
    internal bool Visible {get;private set;}=true;
    internal string Serialize()=>"TizenPSP-overlay-v1\n"+(Visible?"1":"0")+"\n";
    internal void Toggle(){Visible=!Visible;}
    internal static PerformanceOverlayPreference Parse(string text) {
        if(text==null || text.Length>128)throw new InvalidDataException("Invalid overlay preference");
        text=text.Replace("\r\n","\n");
        if(text!="TizenPSP-overlay-v1\n1\n" && text!="TizenPSP-overlay-v1\n0\n")throw new InvalidDataException("Invalid overlay preference");
        return new PerformanceOverlayPreference{Visible=text=="TizenPSP-overlay-v1\n1\n"};
    }
    internal static PerformanceOverlayPreference Load(string path,Action<string> log) {
        foreach(string candidate in new[]{path,path+".bak"})try {
            if(!File.Exists(candidate))continue;
            if(new FileInfo(candidate).Length>128)throw new InvalidDataException("Overlay preference too large");
            return Parse(File.ReadAllText(candidate));
        }catch(Exception e){log("OVERLAY_SETTINGS_LOAD "+e.Message);}
        return new PerformanceOverlayPreference();
    }
    internal void Save(string path) {
        string temp=path+".tmp";File.WriteAllText(temp,Serialize());
        if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);
    }
}
