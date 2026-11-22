using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

internal static class InputHotPathChecks
{
    static void Need(bool ok,string message){if(!ok)throw new Exception("Input hot path: "+message);}
    // Model the existing bridge latch: a short press survives release until poll.
    sealed class Bridge {
        internal uint Held,Pending,Frame;
        internal int X,Y,Z,W;
        internal void Buttons(uint value){Pending|=value&~Held;Held=value;}
        internal void Sticks(int x,int y,int z,int w){X=x;Y=y;Z=z;W=w;}
        internal void Poll(){Frame=Held|Pending;Pending=0;}
        internal void Reset(){Held=Pending=Frame=0;X=Y=Z=W=0;}
    }
    internal static void Run() {
        var state=new CoreInputState();var original=new Bridge();var filtered=new Bridge();
        Action<uint> send=value=>{original.Buttons(value);if(state.ButtonsChanged(value))filtered.Buttons(value);};
        Action poll=()=>{original.Poll();filtered.Poll();Need(original.Frame==filtered.Frame,"poll lost a held button or short tap");};
        send(0);send(1);send(1);send(0);poll();Need(filtered.Frame==1,"tap latch");poll();Need(filtered.Frame==0,"tap release");
        send(1);poll();original.Reset();filtered.Reset();state.Forget();send(1);poll();Need(filtered.Frame==1,"held value not resent after reset");
        var rng=new Random(239);uint held=0;
        for(int i=0;i<100000;i++) {
            if(i%137==0){original.Reset();filtered.Reset();state.Forget();}
            if(i%3==0)held^=1u<<rng.Next(16);
            send(held);
            int x=i%5==0?rng.Next(-32767,32768):original.X,y=original.Y,z=i%9==0?-x:original.Z,w=i%11==0?x:original.W;
            original.Sticks(x,y,z,w);if(state.SticksChanged(x,y,z,w))filtered.Sticks(x,y,z,w);
            if(i%7==0)poll();
            Need(original.Held==filtered.Held&&original.Pending==filtered.Pending&&original.X==filtered.X&&original.Y==filtered.Y&&original.Z==filtered.Z&&original.W==filtered.W,"state differs between polls/reset");
        }
        Need(state.ButtonWrites<state.ButtonRequests/2&&state.StickWrites<state.StickRequests/2,"unchanged values still forwarded");
        Need(state.Summary.Contains("INPUT_FORWARD"),"forward counters unavailable");

        var s=new StickInput();var buffer=new short[4];
        for(int mode=0;mode<4;mode++)for(int i=0;i<500;i++) {
            s.RightMode=mode;s.DeadZone=5+i%31;
            for(int a=0;a<4;a++)s.Observe(3,s.Axes[a].Code,rng.Next(256));
            // Values remains the public allocating path used by mapper diagnostics.
            var expected=s.Values;uint bits=s.Route(expected);
            s.WriteValues(buffer);Need(s.Route(buffer)==bits&&buffer.SequenceEqual(expected),"reused buffer routing drift");
            s.Clear();s.WriteValues(buffer);Need(s.Route(buffer)==0&&buffer.All(x=>x==0),"routed values survived clear/dead zone");
        }
        // Compare the diagnostic record against a separate chronological reference,
        // including wrap, slot changes, empty reads and errors after the last event.
        var activity=new ControllerActivity();var recent=new Queue<string>();
        Need(activity.Snapshot(0).Contains("last_raw=none"),"initial raw state");
        long buttons=0,hats=0,axes=0,other=0;
        for(int i=0;i<100;i++) {
            int slot=i%4,type=i%3==0?1:i%3==1?3:7,code=i%2==0?16:0,value=i-30;
            double time=i*.125;activity.Slots(15,slot);activity.Record(1,type,code,value,time);
            if(type==1)buttons++;else if(type==3&&code==16)hats++;else if(type==3)axes++;else other++;
            string raw=type+"/"+code+"/"+value;
            recent.Enqueue("slot="+(slot+1)+" t="+time.ToString("F3",CultureInfo.InvariantCulture)+" rc=1 raw="+raw);
            if(recent.Count>16)recent.Dequeue();
            string[] lines=activity.Snapshot(time+1).Split(new[]{Environment.NewLine},StringSplitOptions.None);
            Need(lines.Skip(1).SequenceEqual(recent),"ring chronology, captured slot or formatting changed");
            Need(lines[0].Contains("last_raw="+raw+" event_age_s=1.00"),"last event/age changed");
        }
        activity.Record(0,0,0,0,20);activity.Record(-1,0,0,0,21);
        string snapshot=activity.Snapshot(22);
        Need(snapshot.Contains("read_calls=102 empty_reads=1 button_events="+buttons+" hat_events="+hats+" axis_events="+axes+" other_events="+other+" last_rc=-1"),"diagnostic counts or error rc changed");
        Need(activity.Events==100,"empty/error counted as events");
        for(int i=0;i<10000;i++){activity.Record(1,3,0,i,0);s.WriteValues(buffer);state.ButtonsChanged(0);state.SticksChanged(0,0,0,0);}
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100000;i++){activity.Record(1,3,0,i,0);s.WriteValues(buffer);state.ButtonsChanged(0);state.SticksChanged(0,0,0,0);}
        Need(GC.GetAllocatedBytesForCurrentThread()==before,"per-event allocations returned");
        Console.WriteLine("PASS: 100,000 input transitions match bridge latch/reset semantics; all stick routes clear reused buffers; diagnostic ring preserves evidence; hot-path allocation = 0 bytes.");
    }
}
