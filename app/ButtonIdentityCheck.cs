using System;
using System.Collections.Generic;
using System.Linq;

// Diagnostic only. Never assigns a binding or infers identity from timing.
internal sealed class ButtonIdentityCheck
{
    const double SlotSeconds=6,SettleSeconds=1.5;
    readonly double started;
    readonly List<string>[] traces=Enumerable.Range(0,6).Select(_=>new List<string>()).ToArray();
    readonly Dictionary<string,int>[] downs=Enumerable.Range(0,6).Select(_=>new Dictionary<string,int>()).ToArray();
    readonly HashSet<string>[] complete=Enumerable.Range(0,6).Select(_=>new HashSet<string>()).ToArray();
    readonly int[] dropped=new int[6],axisEvents=new int[6];
    internal int Step {get;private set;}
    internal bool Finished=>Step>=6;
    internal string AbortReason {get;private set;}
    uint lastFaces;
    internal void Faces(bool active,uint faces,double now) {
        uint current=active?faces:0,changed=lastFaces^current;lastFaces=current;
        if((changed&DualSenseReports.Circle)!=0)Key("hid","Circle",8,(current&DualSenseReports.Circle)!=0,now);
        if((changed&DualSenseReports.Triangle)!=0)Key("hid","Triangle",9,(current&DualSenseReports.Triangle)!=0,now);
    }
    internal ButtonIdentityCheck(double now){started=now;}
    internal void Tick(double now){if(!Finished)Step=Math.Min(6,Math.Max(Step,(int)Math.Max(0,(now-started)/SlotSeconds)));}
    bool Recording(double now){Tick(now);return !Finished && now>=started+Step*SlotSeconds+SettleSeconds;}
    internal void Abort(string reason){AbortReason=reason;Step=6;}
    internal string Prompt(double now) {
        Tick(now);if(Finished)return "Check complete. Your mappings have not changed.";
        string button=Step%2==0?"CIRCLE":"TRIANGLE";
        return now<started+Step*SlotSeconds+SettleSeconds?"Release all buttons. Next: "+button:
            "Press and release "+button+" once now. Use the actual face button, not R3.";
    }
    void Record(string line) {if(traces[Step].Count<96)traces[Step].Add(line);else dropped[Step]++;}
    void Edge(string identity,bool down) {
        if(down){if(!downs[Step].ContainsKey(identity))downs[Step][identity]=1;}
        else if(downs[Step].Remove(identity))complete[Step].Add(identity);
    }
    internal void Raw(int type,int code,int value,double now,string extra=null) {
        if(!Recording(now))return;
        // Axis drift must not bury the button or scan-code evidence.
        if(type==3){axisEvents[Step]++;return;}
        Record("raw="+type+"/"+code+"/"+value+(string.IsNullOrEmpty(extra)?"":" fields="+extra));
        if(type==1 && (value==0||value==1))Edge("raw-button:"+code,value==1);
    }
    internal void Key(string channel,string name,int code,bool down,double now) {
        if(!Recording(now))return;
        name=(name??"").Replace("\r","").Replace("\n","");if(name.Length>80)name=name.Substring(0,80);
        Record(channel+" key="+name+" code="+code+" "+(down?"down":"up"));
        Edge(channel+":"+name+":"+code,down);
    }
    string[] Stable(int parity) {
        var values=new HashSet<string>(complete[parity]);
        for(int i=parity+2;i<6;i+=2)values.IntersectWith(complete[i]);
        return values.Where(v=>!Enumerable.Range(0,6).Where(i=>i%2!=parity).Any(i=>complete[i].Contains(v)||downs[i].ContainsKey(v))).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
    }
    internal string Result {
        get {
            if(AbortReason!=null)return "Check interrupted: "+AbortReason+". Mappings unchanged.";
            if(!Finished)return "Check still running.";
            if(dropped.Any(n=>n!=0))return "Capture overflowed; report is incomplete. Mappings unchanged.";
            if(complete.Any(s=>!s.Any(x=>x.StartsWith("raw-button:"))))return "Missing complete raw presses in one or more steps. Mappings unchanged.";
            var a=Stable(0);var b=Stable(1);
            if(a.Length>0&&b.Length>0)return "Repeatable distinct identities found. Export the report to implement and verify the route. Mappings unchanged.";
            return "No repeatable separate button/key identity found. Export the scan/event trace for review. Keep R3 for Circle.";
        }
    }
    internal string Report=>"BUTTON_IDENTITY_CHECK v1\n"+Result+"\n"+
        string.Join("\n",Enumerable.Range(0,6).Select(i=>"STEP "+(i+1)+" "+(i%2==0?"Circle":"Triangle")+
            " axis_events_omitted="+axisEvents[i]+" trace_dropped="+dropped[i]+" complete=["+string.Join(",",complete[i].OrderBy(x=>x,StringComparer.Ordinal))+"]\n"+string.Join("\n",traces[i])))+
        "\nStable Circle=["+string.Join(",",Stable(0))+"] Triangle=["+string.Join(",",Stable(1))+"]";
}
