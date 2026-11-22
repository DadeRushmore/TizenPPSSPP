using System;
using System.Globalization;

// Bounded observations of the SDK boundary, including event types the mapper ignores.
internal sealed class ControllerActivity
{
    readonly object sync=new object();
    struct Observation {
        internal int Slot,Result,Type,Code,Value;
        internal double Time;
        internal string Raw=>Type+"/"+Code+"/"+Value;
        public override string ToString()=>"slot="+(Slot+1)+" t="+Time.ToString("F3",CultureInfo.InvariantCulture)+" rc="+Result+" raw="+Raw;
    }
    readonly Observation[] recent=new Observation[16];
    int next,count;
    long reads,empty,buttons,hats,axes,other;
    int slot=-1,mask,result;
    string phase="starting";
    Observation last;
    double phaseSince,lastEvent=-1;
    internal void Phase(string value,double now){lock(sync){phase=value;phaseSince=now;}}
    internal void Slots(int connectedMask,int selected){lock(sync){mask=connectedMask;slot=selected;}}
    internal void Record(int rc,int type,int code,int value,double now) {
        lock(sync) {
            reads++;result=rc;
            if(rc<0)return;
            if(rc==0 && type==0){empty++;return;}
            if(type==1)buttons++;else if(type==3 && (code==16||code==17))hats++;else if(type==3)axes++;else other++;
            last=new Observation{Slot=slot,Result=rc,Type=type,Code=code,Value=value,Time=now};lastEvent=now;
            recent[next]=last;next=(next+1)%recent.Length;if(count<recent.Length)count++;
        }
    }
    internal long Events {get{lock(sync)return buttons+hats+axes+other;}}
    internal bool SingleFirstSlot {get{lock(sync)return mask==1&&slot==0;}}
    internal string Summary {get{lock(sync)return "slot "+(slot<0?"none":(slot+1).ToString())+" | buttons "+buttons+" hats "+hats+" axes "+axes+" other "+other;}}
    internal string Snapshot(double now) {
        lock(sync) {
            // Format only when requested; recording axis traffic never allocates strings.
            var lines=new string[count];
            for(int i=0;i<count;i++)lines[i]=recent[(next-count+i+recent.Length)%recent.Length].ToString();
            return "INPUT_SOURCE selected_slot="+(slot+1)+" connected_mask=0x"+mask.ToString("X")+
            " phase="+phase+" phase_age_s="+Math.Max(0,now-phaseSince).ToString("F2",CultureInfo.InvariantCulture)+
            " read_calls="+reads+" empty_reads="+empty+" button_events="+buttons+" hat_events="+hats+
            " axis_events="+axes+" other_events="+other+" last_rc="+result+" last_raw="+(count==0?"none":last.Raw)+
            " event_age_s="+(lastEvent<0?"never":Math.Max(0,now-lastEvent).ToString("F2",CultureInfo.InvariantCulture))+
            Environment.NewLine+string.Join(Environment.NewLine,lines);
        }
    }
}

// A stalled learning prompt must explain its state without silently discarding holds.
internal sealed class InputLearningWatch
{
    double started,nextNotice;
    long initialEvents;
    internal void Start(double now,long events){started=now;nextNotice=now+8;initialEvents=events;}
    internal string Check(double now,bool ready,string held,bool connected,long events) {
        if(now<nextNotice)return null;nextNotice=now+8;
        if(!ready)return "Waiting for release: "+held+". Release all buttons; Back cancels.";
        if(!connected)return "No active controller reader. Check pairing; Back cancels.";
        if(events==initialEvents)return "Connected, but no controller events arrived. Check Input slot; Back cancels.";
        return "Events arrived but no supported press was learned. Try a face button; Back cancels.";
    }
    internal string Snapshot(double now,bool learning,bool ready,string held)=>"INPUT_LEARNING active="+learning+" ready="+ready+
        " elapsed_s="+(learning?Math.Max(0,now-started).ToString("F1",CultureInfo.InvariantCulture):"0")+" held="+held;
}
