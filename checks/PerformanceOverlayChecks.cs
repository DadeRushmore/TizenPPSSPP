using System;
using System.IO;
internal static class PerformanceOverlayChecks
{
    static void Need(bool ok,string message){if(!ok)throw new Exception(message);}
    static PerformanceCounters At(double time,long runs,uint video,uint duplicate=0)=>new PerformanceCounters{Time=time,Runs=runs,Video=video,Duplicates=duplicate,FrameSeconds=1.0/60,RunP95Ms=7.5};
    internal static void Run() {
        var sample=new PerformanceSampler();
        Need(!sample.Sample(At(0,0,0)).HasRates,"Overlay starts unknown, not zero");
        var r=sample.Sample(At(.5,30,15,15));
        Need(r.HasRates && Math.Abs(r.VideoFps-30)<.01 && Math.Abs(r.RunHz-60)<.01 && Math.Abs(r.CadencePercent-100)<.01 && Math.Abs(r.DuplicateHz-30)<.01,"Thirty video submissions must not imply half-speed emulation");
        Need(r.Primary.Contains("30.0 FPS") && r.Primary.Contains("100%") && r.Primary.Contains("7.5 ms"),"Overlay format separates rates and CPU latency");
        sample.Reset();sample.Sample(At(30,300,200));r=sample.Sample(At(30.5,330,230));
        Need(Math.Abs(r.RunHz-60)<.01,"Paused wall time not included after reset");
        for(int i=1;i<=20;i++)sample.Sample(At(30.5+i*.5,330+i*30,(uint)(230+i*30)));
        r=sample.Sample(At(41,960,860));Need(Math.Abs(r.RunHz-60)<.01,"Bounded ring retains correct two-second baseline");
        Need(!sample.Sample(At(42,1,1)).HasRates,"Counter reset cannot produce negative/huge FPS");
        Need(!sample.Sample(At(42,2,2)).HasRates,"Duplicate timestamp cannot divide by zero");
        sample.Reset();
        var guest=At(50,0,0);guest.HasGuestClock=true;guest.GuestMilliseconds=1000;guest.ActualFlips=10;
        sample.Sample(guest);
        guest=At(52,120,120);guest.HasGuestClock=true;guest.GuestMilliseconds=2000;guest.ActualFlips=70;
        r=sample.Sample(guest);
        Need(r.HasGuestRates && Math.Abs(r.GuestSpeedPercent-50)<.01 && Math.Abs(r.GameFps-30)<.01,
            "60 submissions per second with half-speed guest time must show 50% speed and 30 game FPS");
        Need(r.Primary.Contains("Speed 50%") && r.Primary.Contains("Game 30.0 FPS"),"Display measured guest speed");
        guest.Time=54;guest.Runs=240;guest.Video=240;guest.GuestMilliseconds=4000;guest.ActualFlips=130;
        r=sample.Sample(guest);
        Need(Math.Abs(r.GuestSpeedPercent-100)<.01 && Math.Abs(r.GameFps-30)<.01,"30 FPS game can run at full speed");
        guest.Time=55;guest.GuestMilliseconds=1;
        Need(!sample.Sample(guest).HasRates,"Guest clock reset invalidates rate window");
        var path=Path.Combine(Path.GetTempPath(),"tizenpsp-overlay-"+Guid.NewGuid().ToString("N")+".txt");
        try {
            var prefs=PerformanceOverlayPreference.Load(path,_=>{});Need(prefs.Visible,"HUD defaults on");
            prefs.Toggle();prefs.Save(path);Need(!PerformanceOverlayPreference.Load(path,_=>{}).Visible,"Hidden preference persists");
            prefs.Toggle();prefs.Save(path);File.WriteAllText(path,"broken");
            Need(!PerformanceOverlayPreference.Load(path,_=>{}).Visible,"Corrupt primary recovers previous preference");
            bool rejected=false;try{PerformanceOverlayPreference.Parse("1");}catch(InvalidDataException){rejected=true;}Need(rejected,"Malformed preference rejected");
        }finally{foreach(string file in new[]{path,path+".bak",path+".tmp"})if(File.Exists(file))File.Delete(file);}
        Console.WriteLine("PASS: live video/cadence distinction, pause exclusion, bounded window, counter resets and persistent overlay visibility");
    }
}
