using System;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
namespace Tizen.TV.Accessory {
    public enum PlayerIndex { One,Two,Three,Four }
    public struct GamePadCapabilities {public bool IsConnected;}
    public static class GamePad {
        internal static volatile int ConnectedMask=2;
        static bool Initialise()=>true;
        public static GamePadCapabilities GetCapabilities(PlayerIndex index)=>new GamePadCapabilities{IsConnected=(ConnectedMask&(1<<(int)index))!=0};
    }
}
internal static class Interop {
    internal static class GamepadClient {
        internal struct InputEvent {public int type,code,value;}
        internal static readonly ConcurrentQueue<InputEvent> Events=new ConcurrentQueue<InputEvent>();
        internal static int LastSlot=-1;
        internal static volatile bool BlockRead;
        internal static readonly ManualResetEventSlim Entered=new ManualResetEventSlim(false),Release=new ManualResetEventSlim(false);
        internal static int gamepad_get_input_event(int slot,out InputEvent input) {
            if(BlockRead){Entered.Set();Release.Wait();}
            LastSlot=slot;if(Events.TryDequeue(out input))return 1;input=default(InputEvent);return 0;
        }
    }
}
internal static class ControllerReaderChecks {
    static void Need(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Until(Func<bool> predicate,string message) {
        double end=ControllerReader.Now+3;
        while(ControllerReader.Now<end && !predicate())Thread.Sleep(5);
        Need(predicate(),message);
    }
    static ControllerReader.Event[] Drain(ControllerReader reader) {var batch=new List<ControllerReader.Event>();reader.DrainTo(batch);return batch.ToArray();}
    internal static void Run() {
        Tizen.TV.Accessory.GamePad.ConnectedMask=2;
        Interop.GamepadClient.Events.Enqueue(new Interop.GamepadClient.InputEvent{type=1,code=9,value=1});
        Interop.GamepadClient.Events.Enqueue(new Interop.GamepadClient.InputEvent{type=1,code=9,value=0});
        var reader=new ControllerReader();var batch=new List<ControllerReader.Event>();reader.Start();
        try {
            double end=ControllerReader.Now+2;
            var reusable=new List<ControllerReader.Event>(128);
            while(ControllerReader.Now<end && batch.Count<2){Thread.Sleep(10);reader.DrainTo(reusable);batch.AddRange(reusable);}
            if(batch.Count!=2||batch[0].Code!=9||batch[0].Value!=1||batch[1].Value!=0||Interop.GamepadClient.LastSlot!=1)
                throw new Exception("SDK event adapter lost press/release or selected wrong slot");
            Need(reader.Diagnostics.Contains("button_events=2") && reader.Diagnostics.Contains("raw=1/9/0"),"Input boundary counters/recent events lost");
            reader.DrainTo(reusable);Need(reusable.Count==0,"Reusable drain replayed old button edges");
            Interop.GamepadClient.Events.Enqueue(new Interop.GamepadClient.InputEvent{type=3,code=0,value=1900});
            Interop.GamepadClient.Events.Enqueue(new Interop.GamepadClient.InputEvent{type=7,code=99,value=1});
            Until(()=>reader.Diagnostics.Contains("axis_events=1") && reader.Diagnostics.Contains("other_events=1"),"Ignored types must remain diagnosable");
            Need(Array.TrueForAll(Drain(reader),e=>e.Type==1||e.Type==3),"Normal gameplay received diagnostic-only types");
            reader.CaptureAllEvents=true;
            Interop.GamepadClient.Events.Enqueue(new Interop.GamepadClient.InputEvent{type=4,code=4,value=900});
            Until(()=>reader.Diagnostics.Contains("raw=4/4/900"),"Scan event not seen");
            var capture=new List<ControllerReader.Event>();
            Until(()=>{capture.AddRange(Drain(reader));return capture.Exists(e=>e.Type==4&&e.Value==900);},"Diagnostic mode discarded scan event");
            Need(reader.Diagnostics.Contains("INPUT_SDK_SCHEMA")&&reader.Diagnostics.Contains("code:Int32"),"Runtime SDK field schema missing");
            reader.CaptureAllEvents=false;
            Tizen.TV.Accessory.GamePad.ConnectedMask=3;reader.SelectSlot(1);
            Until(()=>reader.Connected && reader.Diagnostics.Contains("connected_mask=0x3"),"Manual selection did not discover all connected slots");
            Need(Interop.GamepadClient.LastSlot==1,"Manual second slot selected the first pad");
            reader.SelectSlot(0);Until(()=>reader.Connected && Interop.GamepadClient.LastSlot==0,"Manual first slot not selected");
            reader.SelectSlot(3);Until(()=>reader.Diagnostics.Contains("selected_slot=0") && !reader.Connected,"Unavailable manual slot incorrectly fell back");
            reader.SelectSlot(-1);Until(()=>reader.Connected && Interop.GamepadClient.LastSlot==0,"Auto did not choose first connected slot");
            Need(Drain(reader).Length==0,"Source change retained old input");
            Interop.GamepadClient.BlockRead=true;
            Need(Interop.GamepadClient.Entered.Wait(1000),"Blocked SDK test did not enter read");
            Need(reader.Diagnostics.Contains("phase=read_slot_1"),"Blocked read boundary missing from diagnostics");
            // These calls must complete while the SDK read remains blocked.
            reader.SelectSlot(1);Need(Drain(reader).Length==0,"Blocked read locked the UI queue");
            Interop.GamepadClient.BlockRead=false;Interop.GamepadClient.Release.Set();
            Until(()=>reader.Connected && Interop.GamepadClient.LastSlot==1,"Reader failed to apply selection after blocked read returned");
            reader.Enable(false);if(reader.Connected)throw new Exception("Paused reader still connected");
            if(Drain(reader).Length!=0)throw new Exception("Pause retained queued input");
        } finally {Interop.GamepadClient.BlockRead=false;Interop.GamepadClient.Release.Set();reader.Stop();Until(()=>reader.Finished,"Controller worker did not exit");}
        var activity=new ControllerActivity();activity.Slots(3,1);activity.Record(0,0,0,0,1);
        Need(activity.Events==0 && activity.Snapshot(2).Contains("event_age_s=never"),"Empty reads were presented as button activity");
        for(int i=0;i<100;i++)activity.Record(1,1,i,1,i+2);
        Need(activity.Snapshot(103).Split(new[]{Environment.NewLine},StringSplitOptions.None).Length==17,"Recent event trace is not bounded");
        var watch=new InputLearningWatch();watch.Start(0,0);
        Need(watch.Check(7,false,"key:Return",true,0)==null,"Learning warning too early");
        Need(watch.Check(8,false,"key:Return",true,0).Contains("key:Return"),"Stuck remote release not explained");
        Need(watch.Check(9,false,"key:Return",true,0)==null,"Learning warning floods");
        Need(watch.Check(16,true,"none",true,0).Contains("no controller events"),"Connected without events not distinguished");
        Need(watch.Check(24,true,"none",true,1).Contains("no supported press"),"Unsupported events not distinguished");
        Need(watch.Check(32,true,"none",false,1).Contains("No active controller"),"Reader failure not explained");
        Console.WriteLine("PASS: SDK tap edges, actual activity counters, unsupported events, manual/auto slots, blocked native read isolation, bounded traces and learning-wait diagnostics.");
    }
}
