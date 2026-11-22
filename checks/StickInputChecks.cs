using System;
using System.IO;
using System.Linq;

internal static class StickInputChecks {
    static void Check(bool pass,string message){if(!pass)throw new Exception("Sticks: "+message);}
    static void Reject(string text){try{StickInput.Parse(text);}catch(FormatException){return;}throw new Exception("Invalid stick settings accepted");}
    internal static void Run() {
        var s=new StickInput();Check(s.Values.All(v=>v==0),"Unseen axes must be neutral");
        Check(!s.Observe(1,0,1)&&!s.Observe(3,16,1)&&!s.Observe(3,-1,0)&&!s.Observe(3,0,int.MaxValue),"Reject buttons, hats and invalid raw values");
        foreach(int c in new[]{0,1,2,5})s.Observe(3,c,128);
        s.Observe(3,0,130);s.Observe(3,1,124);Check(s.Values.All(v=>v==0),"Center jitter is within dead zone");
        s.Observe(3,0,0);s.Observe(3,1,128);Check(s.Values[0]==-32767&&s.Values[1]==0,"Left endpoint");
        s.Observe(3,0,255);Check(s.Values[0]==32767,"Right endpoint");
        s.Observe(3,1,255);Check(Math.Abs(s.Values[0]-23170)<=1&&Math.Abs(s.Values[1]-23170)<=1,"Radial diagonal is bounded");
        s.Observe(3,0,128);s.Observe(3,1,128);s.Observe(3,2,255);s.Observe(3,5,0);
        var v=s.Values;uint bits=s.Route(v);Check(bits==((1u<<7)|(1u<<4))&&v[2]==0&&v[3]==0,"Right stick D-pad diagonal");
        var map=new PspInputMap();map.Assign(4,"button:4");map.Assign(8,"hat:17:-1");map.ObserveRaw(1,4,1);map.ObserveRaw(3,17,-1);
        s.Observe(3,2,128);s.Observe(3,5,128);v=s.Values;Check((map.Held|s.Route(v))==((1u<<10)|(1u<<4)),"Neutral stick preserves real D-pad and shoulder holds");
        s.Observe(3,2,0);s.RightMode=3;v=s.Values;Check(s.Route(v)==(1u<<10)&&v[0]==-32767&&v[2]==0,"L + analog camera mapping");
        s.RightMode=2;v=s.Values;Check(s.Route(v)==0&&v[0]==-32767,"Right to left mapping");
        s.RightMode=0;v=s.Values;Check(s.Route(v)==0&&v[2]==-32767&&v[0]==0,"Independent native right stick");
        s.Clear();Check(s.Values.All(x=>x==0),"Disconnect/pause/source change releases analog");
        var a=new StickAxis{Code=4,Center=0,Negative=32767,Positive=-32768};
        Check(a.Valid&&a.Normalize(32767)==-1&&a.Normalize(-32768)==1&&a.Normalize(0)==0,"Inverted signed axis");
        a=new StickAxis{Code=0,Center=120,Negative=10,Positive=250};Check(a.Normalize(10)==-1&&a.Normalize(250)==1&&a.Normalize(120)==0,"Asymmetric range");
        string old=s.Serialize();var calibration=new StickCalibration();string error;
        Check(!calibration.Confirm(s,out error)&&calibration.Step==0,"No fabricated calibration from missing data");
        int[] codes={0,1,3,4};foreach(int c in codes)s.Observe(3,c,128);
        Check(calibration.Confirm(s,out error),"Capture rest centers");
        // Simulate a TV-translated layout different from the default raw DualSense axes.
        for(int i=0;i<4;i++) {
            foreach(int c in codes)s.Observe(3,c,128);
            s.Observe(3,codes[i],i==2?255:0);Check(calibration.Confirm(s,out error),"Discover axis "+i);
            Check(!calibration.Confirm(s,out error),"Reject same-side confirmation");
            s.Observe(3,codes[i],i==2?0:255);Check(calibration.Confirm(s,out error),"Opposite endpoint "+i);
        }
        Check(calibration.Complete&&calibration.Result.Select(x=>x.Code).SequenceEqual(codes),"All four axes discovered uniquely");
        Check(s.Serialize()==old,"Calibration doesn't mutate active settings before accepted completion");
        s.Axes=calibration.Result;Check(s.Axes[2].Normalize(255)==-1,"Calibration identifies inverted orientation");
        string saved=s.Serialize();Check(StickInput.Parse(saved).Serialize()==saved,"Stick persistence round trip");
        Reject(saved.Replace("16\t0","99\t0"));Reject(saved.Replace("3\t128","0\t128"));Reject("garbage");
        var path=Path.Combine(Path.GetTempPath(),"tizenpsp-stick-check-"+Guid.NewGuid()+".txt");
        try {
            s.Save(path);s.DeadZone=22;s.Save(path);Check(StickInput.Load(path,_=>{}).DeadZone==22,"Save replacement");
            File.WriteAllText(path,"damaged");Check(StickInput.Load(path,_=>{}).Serialize()==saved,"Recover valid backup");
            File.WriteAllText(path+".bak","damaged");Check(StickInput.Load(path,_=>{}).Axes[2].Code==2,"Malformed settings fall back to defaults");
        } finally {foreach(var p in new[]{path,path+".bak",path+".tmp"})if(File.Exists(p))File.Delete(p);}
        Console.WriteLine("PASS: analog normalization, radial dead zone, independent/right-stick routing, calibration, persistence and reset");
    }
}
