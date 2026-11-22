using System;
using System.Reflection;
internal static class SdkControllerEventChecks
{
    // Private SDK-shaped type with an extra field and ref (not out) reads.
    struct NativeEvent {internal int type,code,value;internal uint time;}
    static int calls,lastSlot;
    static int Read(int slot,ref NativeEvent value) {
        lastSlot=slot;
        switch(calls++) {
        case 0:value=new NativeEvent{type=1,code=9,value=1,time=123};return 1;
        case 1:return 0; // Do not write anything: reader must reset the struct.
        case 2:value=new NativeEvent{type=1,code=9,value=0,time=456};return 1;
        case 3:return -7;
        default:throw new InvalidOperationException("driver failed");
        }
    }
    static int ReadOut(int slot,out NativeEvent value) {value=new NativeEvent{type=3,code=slot,value=17};return 1;}
    static int Empty(int slot,ref NativeEvent value)=>0;
    static void BadRead(int slot,out NativeEvent value){value=default(NativeEvent);}
    static MethodInfo Method(string name)=>typeof(SdkControllerEventChecks).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static);
    static void Need(bool ok,string message){if(!ok)throw new Exception(message);}
    internal static void Run() {
        foreach(bool compile in new[]{true,false}) {
            calls=0;var reader=SdkControllerEventReader.Bind(Method("Read"),compile);
            Need(reader.Mode==(compile?"compiled":"reflection_disabled"),"SDK binding mode");
            var a=reader.Read(2,false);Need(a.ReturnCode==1&&a.Type==1&&a.Code==9&&a.Value==1&&a.Raw==null&&lastSlot==2,"press read");
            var b=reader.Read(3,false);Need(b.ReturnCode==0&&b.Type==0&&b.Code==0&&b.Value==0&&lastSlot==3,"empty read replayed stale event");
            var c=reader.Read(0,true);Need(c.ReturnCode==1&&c.Value==0&&c.Raw is NativeEvent&&((NativeEvent)c.Raw).time==456,"release/capture field");
            Need(reader.Read(0,false).ReturnCode==-7,"native error code lost");
            bool failed=false;
            try {reader.Read(0,false);}catch(Exception e){failed=(e.InnerException??e).Message=="driver failed";}
            Need(failed,"driver exception swallowed");
            var o=SdkControllerEventReader.Bind(Method("ReadOut"),compile).Read(7,false);
            Need(o.Type==3&&o.Code==7&&o.Value==17,"out SDK binding");
        }
        bool rejected=false;try{SdkControllerEventReader.Bind(Method("BadRead"));}catch(NotSupportedException){rejected=true;}
        Need(rejected,"changed SDK signature accepted");
        var empty=SdkControllerEventReader.Bind(Method("Empty"));
        for(int i=0;i<100;i++)empty.Read(0,false);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)empty.Read(0,false);
        Need(GC.GetAllocatedBytesForCurrentThread()==before,"compiled idle reads allocate");
        Console.WriteLine("PASS: compiled/reflection SDK readers preserve ref/out, empty reset, taps, slot, capture, native failures; compiled idle reads allocate zero bytes.");
    }
}
