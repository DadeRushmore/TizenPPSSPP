using System;
internal static class ButtonIdentityChecks {
    static void Need(bool pass,string reason){if(!pass)throw new Exception("Button check: "+reason);}
    static ButtonIdentityCheck Capture(bool keys,bool sharedKey=false,bool oneMissing=false) {
        var c=new ButtonIdentityCheck(0);
        for(int i=0;i<6;i++) {
            double t=i*6+2;
            c.Raw(1,99,1,i*6+.1); // opening remote/release guard must not enter the sample
            for(int j=0;j<500;j++)c.Raw(3,5,129,t); // axis noise must not consume trace budget
            c.Raw(4,4,900+i%2,t);c.Raw(1,2,1,t+.1);
            if(!oneMissing||i!=5)c.Raw(1,2,0,t+.8);
            if(keys){c.Key("ecore","same-name",sharedKey?12:12+i%2,true,t+.2);c.Key("ecore","same-name",sharedKey?12:12+i%2,false,t+.9);}
        }
        c.Tick(36);return c;
    }
    internal static void Run() {
        var shared=Capture(false);Need(shared.Result.StartsWith("No repeatable"),"Shared button identity must not be renumbered");
        Need(shared.Report.Contains("raw=4/4/900")&&shared.Report.Contains("raw=4/4/901"),"Non-button scan events lost");
        Need(!shared.Report.Contains("raw=1/99")&&shared.Report.Contains("axis_events_omitted=500"),"Settling/noise gate");
        Need(Capture(true).Result.StartsWith("Repeatable distinct"),"Same key name with distinct numeric key codes should be reported");
        Need(Capture(true,true).Result.StartsWith("No repeatable"),"Shared key must not count as distinct");
        Need(Capture(true,false,true).Result.StartsWith("Missing complete"),"Incomplete trial must not count as verified");
        var missing=new ButtonIdentityCheck(0);missing.Tick(36);Need(missing.Result.StartsWith("Missing complete"),"No input isn't proof of a collision");
        var aborted=new ButtonIdentityCheck(0);aborted.Abort("disconnect");aborted.Tick(2);Need(aborted.Finished&&aborted.Result.Contains("disconnect"),"Abort state is durable");
        var bounded=new ButtonIdentityCheck(0);for(int i=0;i<10000;i++)bounded.Raw(4,4,i,2);bounded.Tick(36);
        Need(bounded.Result.Contains("overflowed")&&bounded.Report.Length<5000,"Flood capture bounded and reported");
        var monotonic=new ButtonIdentityCheck(0);monotonic.Tick(8);monotonic.Raw(1,4,1,2);Need(monotonic.Step==1&&!monotonic.Report.Contains("raw=1/4"),"Delayed queued events cannot move capture backward");
        Console.WriteLine("PASS: repeated face-button evidence, numeric key distinctions, shared/incomplete rejection, scan retention, noise bounds and cancellation");
    }
}
