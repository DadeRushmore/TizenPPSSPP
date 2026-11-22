using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

internal static class AudioChecks
{
    static void Need(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Wait(Func<bool> condition,string message) {
        var watch=Stopwatch.StartNew();while(!condition() && watch.ElapsedMilliseconds<3000)Thread.Sleep(1);Need(condition(),message);
    }
    static void Push(PcmQueue queue,params short[] samples) {
        var pin=GCHandle.Alloc(samples,GCHandleType.Pinned);try{queue.Push(pin.AddrOfPinnedObject(),samples.Length/2);}finally{pin.Free();}
    }
    static void Push(AudioPipeline pipeline,int frames,short sample=123) {
        var samples=new short[frames*2];for(int i=0;i<samples.Length;i++)samples[i]=sample;
        var pin=GCHandle.Alloc(samples,GCHandleType.Pinned);try{pipeline.Push(pin.AddrOfPinnedObject(),(uint)frames);}finally{pin.Free();}
    }
    sealed class FakeOutput : IAudioOutput
    {
        internal readonly ManualResetEventSlim Entered=new ManualResetEventSlim(),Release=new ManualResetEventSlim(),Flushed=new ManualResetEventSlim();
        internal int Owner,Writes,Flushes,Freed,WrongOwner;
        internal bool FailWrite,FailFlush;
        void Check(){if(Thread.CurrentThread.ManagedThreadId!=Owner)Interlocked.Increment(ref WrongOwner);}
        public void Write(IntPtr data,int frames){Check();Interlocked.Increment(ref Writes);Entered.Set();Release.Wait();if(FailWrite)throw new Exception("injected write failure");Thread.Sleep(2);}
        public void Flush(){Check();Interlocked.Increment(ref Flushes);Flushed.Set();if(FailFlush)throw new Exception("injected flush failure");}
        public double LatencyMs(){Check();return 75;}
        public void Dispose(){Check();Interlocked.Increment(ref Freed);}
    }
    static AudioPipeline Pipeline(FakeOutput output,int rate=8000,int buffer=60) {
        return new AudioPipeline(rate,buffer,()=>{output.Owner=Thread.CurrentThread.ManagedThreadId;return output;});
    }
    internal static void Run() {
        var queue=new PcmQueue(8000,4);var output=new short[8];
        Push(queue,1,-1,2,-2,3,-3);queue.Read(output,2);
        Need(output[0]==1 && output[3]==-2,"Stereo order");
        Push(queue,4,-4,5,-5,6,-6);queue.Read(output,4);
        Need(string.Join(",",output)=="3,-3,4,-4,5,-5,6,-6","Ring wrapping lost stereo frames");
        Push(queue,7,-7,8,-8,9,-9,10,-10,11,-11);queue.Read(output,4);
        Need(queue.Dropped==1 && output[0]==8 && output[7]==-11,"Oversized batch must retain bounded newest audio");
        Push(queue,1,1,2,2,3,3);Push(queue,4,4,5,5);
        Need(queue.Count==4 && queue.Dropped==2,"Queued overflow accounting");queue.Clear();
        queue.Read(output,4);queue.Read(output,4);
        Need(queue.Underruns==1 && queue.Missing==8 && Array.TrueForAll(output,x=>x==0),"Starvation must zero output and count episodes");
        Push(queue,2,2,2,2,2,2,2,2);queue.Read(output,4);queue.Read(output,1);
        Need(queue.Underruns==2,"Recovered queue must rearm underruns");
        bool rejected=false;try{queue.Push(IntPtr.Zero,1);}catch(ArgumentOutOfRangeException){rejected=true;}Need(rejected,"Null PCM accepted");

        var backend=new FakeOutput();var pipeline=Pipeline(backend);
        try {
            Wait(()=>Volatile.Read(ref backend.Owner)!=0,"Worker did not open");
            Push(pipeline,799);Need(backend.Writes==0,"Playback must prime before first write");Push(pipeline,1);
            Wait(()=>backend.Entered.IsSet,"Primed worker did not write");
            var clock=Stopwatch.StartNew();Push(pipeline,400);pipeline.Pause(true);pipeline.Pause(false);pipeline.Stop();
            Need(clock.ElapsedMilliseconds<200,"UI operations waited for a blocked native write");
            Need(backend.Freed==0,"Stream freed under blocked native write");
            backend.Release.Set();Wait(()=>pipeline.Finished,"Stopped worker did not finish");
            Need(backend.Freed==1 && backend.WrongOwner==0,"Audio stream lifetime crossed threads");
        } finally {pipeline.Stop();backend.Release.Set();}

        backend=new FakeOutput();pipeline=Pipeline(backend,48000,120);
        try {
            Push(pipeline,7680);Wait(()=>backend.Entered.IsSet,"48k audio was not primed");
            pipeline.Pause(true);pipeline.Pause(false);backend.Release.Set();
            Wait(()=>backend.Flushed.IsSet,"Fast pause/resume lost the required flush");
            Need(backend.Writes==1,"Old samples played after resume instead of re-priming");
            Push(pipeline,7680);Wait(()=>Volatile.Read(ref backend.Writes)>1,"Resume did not accept fresh PCM");
            pipeline.Stop();Wait(()=>pipeline.Finished,"Resume worker cleanup");
            Need(backend.WrongOwner==0 && backend.Freed==1,"Resume cleanup owner");
        } finally {pipeline.Stop();backend.Release.Set();}

        backend=new FakeOutput{FailWrite=true};pipeline=Pipeline(backend);
        try {
            Push(pipeline,800);backend.Release.Set();Wait(()=>pipeline.Finished,"Write error did not close worker");
            Need(pipeline.Failure.Contains("injected write failure") && backend.Freed==1,"Write error missing or stream leaked");
            Push(pipeline,800); // Playback failure must not escape into native core callbacks.
        } finally {pipeline.Stop();backend.Release.Set();}
        pipeline=new AudioPipeline(44100,120,()=>throw new Exception("injected open failure"));
        Wait(()=>pipeline.Finished,"Open error did not settle");Need(pipeline.Summary.Contains("unavailable"),"Open error did not reach diagnostics");
        backend=new FakeOutput{FailFlush=true};pipeline=Pipeline(backend);
        try {
            Push(pipeline,800);Wait(()=>backend.Entered.IsSet,"Flush test start");pipeline.Pause(true);backend.Release.Set();
            Wait(()=>pipeline.Finished,"Flush error did not settle");Need(pipeline.Failure.Contains("injected flush failure") && backend.Freed==1,"Flush error cleanup");
        }finally{pipeline.Stop();backend.Release.Set();}
        Console.WriteLine("PASS: stereo queue/wrap/overflow/underruns, 48k format, priming, blocked-write ownership, pause/resume flush, audio failure fallback.");
    }
}
