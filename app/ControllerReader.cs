using System;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using Tizen.TV.Accessory;

// Adapted from TizenPS EventPadReader. Use the TV SDK's actual struct declaration,
// never a guessed P/Invoke. One background owner; a blocked driver cannot block UI.
internal sealed class ControllerReader
{
    internal struct Event {internal int Type,Code,Value;internal double Time;internal string Extra;}
    static readonly string[] ReadPhases={"read_slot_1","read_slot_2","read_slot_3","read_slot_4"};
    internal volatile bool CaptureAllEvents;
    volatile string schema="not inspected";
    readonly object sync=new object();
    readonly Queue<Event> queue=new Queue<Event>();
    volatile bool stop,enabled=true;
    volatile string detail="Connecting to controller...";
    volatile bool connected;
    volatile int requestedSlot=-1;
    int selectionGeneration;
    internal readonly ControllerActivity Activity=new ControllerActivity();
    volatile bool finished;
    internal bool Finished=>finished;
    long heartbeat;
    internal string Detail=>connected && !Connected?"Controller reader stalled; TV remote remains available":detail+" | "+Activity.Summary;
    internal int RequestedSlot=>requestedSlot;
    internal string SourceLabel=>requestedSlot<0?"Input: Auto":"Input: Slot "+(requestedSlot+1);
    internal string Diagnostics=>"INPUT_READER mode="+(requestedSlot<0?"auto":(requestedSlot+1).ToString())+" connected="+Connected+" status="+detail+Environment.NewLine+"INPUT_SDK_SCHEMA "+schema+Environment.NewLine+Activity.Snapshot(Now);
    internal void SelectSlot(int slot) {
        if(slot< -1||slot>3)throw new ArgumentOutOfRangeException(nameof(slot));
        requestedSlot=slot;Interlocked.Increment(ref selectionGeneration);connected=false;
        lock(sync)queue.Clear();
    }
    internal bool Connected=>connected && Now-Interlocked.Read(ref heartbeat)/(double)Stopwatch.Frequency<2;
    internal static double Now=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
    internal void Start()=>new Thread(ReadLoop){IsBackground=true,Name="TizenPSP controller"}.Start();
    internal void Enable(bool value){enabled=value;if(!value){connected=false;lock(sync)queue.Clear();}}
    internal void Stop(){stop=true;enabled=false;}
    // Caller-owned UI batch; never hold the reader lock while mapping events.
    internal void DrainTo(List<Event> batch) {
        batch.Clear();lock(sync){while(queue.Count!=0)batch.Add(queue.Dequeue());}
    }
    void ReadLoop()
    {
        ThreadCpuInfo.RegisterCurrent("controller");
        try {
            const BindingFlags f=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
            var init=typeof(GamePad).GetMethod("Initialise",f,null,Type.EmptyTypes,null);
            var interop=typeof(GamePad).Assembly.GetType("Interop+GamepadClient",true);
            var read=interop.GetMethod("gamepad_get_input_event",f);
            var args=read?.GetParameters();
            if(init==null||init.ReturnType!=typeof(bool)||read==null||read.ReturnType!=typeof(int)||args.Length!=2||args[0].ParameterType!=typeof(int)||!args[1].ParameterType.IsByRef)
                throw new NotSupportedException("Controller SDK signature differs; key-event capture remains available");
            var eventType=args[1].ParameterType.GetElementType();
            const BindingFlags fields=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
            var type=eventType.GetField("type",fields);var code=eventType.GetField("code",fields);var value=eventType.GetField("value",fields);
            if(!eventType.IsValueType||type?.FieldType!=typeof(int)||code?.FieldType!=typeof(int)||value?.FieldType!=typeof(int))throw new NotSupportedException("Controller SDK event fields differ");
            var allFields=eventType.GetFields(fields);
            schema=eventType.FullName+" "+string.Join(",",Array.ConvertAll(allFields,x=>x.Name+":"+x.FieldType.Name));
            var sdkReader=SdkControllerEventReader.Bind(read);schema+=" reader="+sdkReader.Mode;
            bool ready=false;int slot=-1,generation=-1;double nextSearch=0;
            while(!stop) {
                if(!enabled){Thread.Sleep(25);continue;}
                if(generation!=Volatile.Read(ref selectionGeneration)){generation=Volatile.Read(ref selectionGeneration);nextSearch=0;slot=-1;}
                if(!ready) {Activity.Phase("initialise",Now);ready=(bool)init.Invoke(null,null);if(!ready){detail="Controller initialization pending";Thread.Sleep(500);continue;}}
                if(Now>=nextSearch) {
                    nextSearch=Now+1;int oldSlot=slot,mask=0;slot=-1;
                    for(int i=0;i<4;i++){Activity.Phase("capabilities_"+(i+1),Now);if(GamePad.GetCapabilities((PlayerIndex)i).IsConnected){mask|=1<<i;if(slot<0)slot=i;}}
                    int selected=requestedSlot;if(selected>=0)slot=(mask&(1<<selected))!=0?selected:-1;
                    if(generation!=Volatile.Read(ref selectionGeneration))continue;
                    Activity.Slots(mask,slot);
                    if(oldSlot>=0 && oldSlot!=slot)lock(sync){queue.Clear();queue.Enqueue(new Event{Type=-1,Time=Now});}
                    connected=slot>=0;detail=connected?"Connected; event counts below":"Selected controller unavailable; check TV pairing/Input slot";
                    if(!connected)lock(sync)queue.Clear();
                }
                if(slot>=0) {
                    double begin=Now;int count=0;
                    while(!stop && enabled && count++<128 && Now-begin<.008) {
                        Activity.Phase(ReadPhases[slot],Now);
                        bool capture=CaptureAllEvents;
                        var sample=sdkReader.Read(slot,capture);int result=sample.ReturnCode;
                        if(!enabled || stop || generation!=Volatile.Read(ref selectionGeneration))break;
                        int t=sample.Type,c=sample.Code,v=sample.Value;
                        Activity.Record(result,t,c,v,Now);
                        if(result<0)throw new InvalidOperationException("Controller read rc="+result);
                        if(t==0&&result==0)break;
                        if(t==1||t==3||capture) lock(sync) {
                            if(queue.Count>=2048) {queue.Clear();queue.Enqueue(new Event{Type=-1,Time=Now});}
                            string extra=null;
                            if(capture && t!=3) {
                                var parts=new List<string>();
                                foreach(var field in allFields)if(field.Name!="type"&&field.Name!="code"&&field.Name!="value" && field.FieldType==typeof(int))parts.Add(field.Name+"="+field.GetValue(sample.Raw));
                                extra=string.Join(",",parts);
                            }
                            queue.Enqueue(new Event{Type=t,Code=c,Value=v,Time=Now,Extra=extra});
                        }
                    }
                }
                Activity.Phase("waiting",Now);Interlocked.Exchange(ref heartbeat,Stopwatch.GetTimestamp());Thread.Sleep(slot<0?100:8);
            }
        } catch(Exception e) {detail="Raw input unavailable: "+(e.InnerException?.Message??e.Message);Activity.Phase("failed",Now);connected=false;lock(sync)queue.Clear();}
        finally{finished=true;}
    }
}
