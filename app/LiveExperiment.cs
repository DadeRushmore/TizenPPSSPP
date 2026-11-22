using System;
using System.Globalization;

// UI-thread state machine. Experiment name and selector supplied by the host.
// No remote input injection, game-clock changes, guest-cache changes or GL calls.
internal sealed class LiveExperiment
{
    readonly string experimentName;readonly Func<double> clock;readonly Func<bool> ready,supported;
    readonly Func<bool,bool> select;readonly Action<bool> frames;readonly Func<string[]> snapshot;
    readonly Func<string,bool> emit;readonly Func<long> drops;
    string id,order;int phase,seconds,generation;double deadline;bool measuring;
    internal bool Active=>id!=null;
    internal string Summary=>Active?"A/B "+order[phase]+" "+(phase+1)+"/4 "+(measuring?"measuring":"warming up"):"A/B idle";
    internal LiveExperiment(Func<double> clock,Func<bool> ready,Func<bool> supported,Func<bool,bool> select,
        Action<bool> frames,Func<string[]> snapshot,Func<string,bool> emit,Func<long> drops,string experimentName="vertex_binding") {
        this.experimentName=experimentName;
        this.clock=clock;this.ready=ready;this.supported=supported;this.select=select;this.frames=frames;
        this.snapshot=snapshot;this.emit=emit;this.drops=drops;
    }
    internal string Start(int duration,string sequence,int connection) {
        if(Active)return "A capture is already running";
        if(duration<10||duration>120||(sequence!="ABBA"&&sequence!="BAAB"))return "Use 10-120 seconds and ABBA or BAAB";
        if(!ready())return "Resume a running ARM JIT game first";
        if(!supported())return "The requested experiment is unavailable";
        id=Guid.NewGuid().ToString("N");order=sequence;seconds=duration;phase=0;generation=connection;
        if(!emit("AB_PLAN id="+id+" order="+order+" seconds="+seconds+" warmup=3 experiment="+experimentName)){Cancel("send_queue_full");return "Connection queue full";}
        Warmup();return Active?null:"Could not select the experiment";
    }
    void Warmup(){measuring=false;frames(false);if(!select(order[phase]=='B')){Cancel("variant_unavailable");return;}deadline=clock()+3;}
    bool Endpoint(string kind) {
        string line="AB_"+kind+" id="+id+" phase="+phase+" variant="+order[phase]+" mono="+
            clock().ToString("F6",CultureInfo.InvariantCulture)+" dropped="+drops()+" generation="+generation;
        return emit(line+"\n"+string.Join("\n",snapshot()));
    }
    internal void Tick(bool connected,int connection) {
        if(!Active)return;
        if(!connected||connection!=generation){Cancel("connection_lost");return;}
        if(!ready()||!supported()){Cancel("game_paused_or_unavailable");return;}
        if(clock()<deadline)return;
        if(!measuring){frames(true);if(!Endpoint("START")){Cancel("send_queue_full");return;}measuring=true;deadline=clock()+seconds;return;}
        frames(false);if(!Endpoint("END")){Cancel("send_queue_full");return;}
        phase++;
        if(phase==order.Length){string completed=id;id=null;select(true);emit("AB_DONE id="+completed+" restored=B");return;}
        Warmup();
    }
    internal void Cancel(string reason) {
        if(!Active)return;string cancelled=id;id=null;frames(false);select(true);
        emit("AB_ABORT id="+cancelled+" reason="+reason+" restored=B");
    }
}

// Per-phase native retro_run durations/spacing. These are not GPU completion times.
internal sealed class LiveFrameWindow
{
    const int Capacity=8192;readonly double[] runs=new double[Capacity],spacing=new double[Capacity];
    int count,spaces,overflow;double start=-1,previous=-1;bool active;
    internal void Record(bool enabled){active=enabled;start=previous=-1;if(enabled){count=spaces=overflow=0;}}
    internal void Observe(uint phase,double now) {
        if(!active)return;
        if(phase==0){if(previous>=0){if(spaces<Capacity)spacing[spaces++]=Math.Max(0,now-previous);else overflow++;}start=previous=now;}
        else if(start>=0){if(count<Capacity)runs[count++]=Math.Max(0,now-start);else overflow++;start=-1;}
    }
    static string P95(double[] data,int n){if(n==0)return "unavailable";var copy=new double[n];Array.Copy(data,copy,n);Array.Sort(copy);return (copy[(int)Math.Ceiling(n*.95)-1]*1000).ToString("F3",CultureInfo.InvariantCulture);}
    internal string Summary=>"AB_FRAME runs="+count+" spacing_samples="+spaces+" overflow="+overflow+
        " run_ms_p95="+P95(runs,count)+" spacing_ms_p95="+P95(spacing,spaces);
}
