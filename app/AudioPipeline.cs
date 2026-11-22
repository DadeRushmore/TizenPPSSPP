using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

internal sealed class AudioPipeline
{
    readonly object gate=new object();
    readonly PcmQueue queue;
    readonly Thread worker;
    readonly Func<IAudioOutput> createOutput;
    readonly int rate,bufferMs,chunkFrames,primeFrames;
    bool stopping,paused,opened,finished;
    int generation;
    string failure;
    long writes,submitted,writeTicks,maxWriteTicks,slowWrites;
    double serverMs=-1;
    long productionStart,productionTicks;
    double ProductionSeconds=>(productionTicks+(productionStart==0?0:Stopwatch.GetTimestamp()-productionStart))/(double)Stopwatch.Frequency;
    void EndProductionWindow() {if(productionStart!=0){productionTicks+=Stopwatch.GetTimestamp()-productionStart;productionStart=0;}}
    internal AudioPipeline(int rate,int bufferMs):this(rate,bufferMs,()=>new PulseAudioOutput(rate,bufferMs)){}
    internal AudioPipeline(int rate,int bufferMs,Func<IAudioOutput> factory) {
        if(rate<8000 || rate>192000 || bufferMs<40 || bufferMs>250 || factory==null)throw new ArgumentOutOfRangeException();
        this.rate=rate;this.bufferMs=bufferMs;createOutput=factory;
        chunkFrames=Math.Max(1,rate/100);
        // Prime both the requested server buffer and a 40 ms application cushion.
        // No additional emulation throttle and no pitch/speed changes in this baseline.
        primeFrames=rate*(bufferMs+40)/1000;
        queue=new PcmQueue(rate,rate*(bufferMs*2+80)/1000);
        worker=new Thread(Feed){IsBackground=true,Name="TizenPSP audio"};worker.Start();
    }
    internal string Failure {get{lock(gate)return failure;}}
    internal bool Finished {get{lock(gate)return finished;}}
    internal void Reject(string message) {lock(gate){failure=message;stopping=true;EndProductionWindow();queue.Clear();Monitor.PulseAll(gate);}}
    internal void Push(IntPtr samples,uint frames) {
        lock(gate) {
            if(stopping || paused || failure!=null)return;
            if(frames>(uint)rate)throw new ArgumentOutOfRangeException(nameof(frames));
            if(frames>0 && productionStart==0)productionStart=Stopwatch.GetTimestamp();
            queue.Push(samples,(int)frames);Monitor.PulseAll(gate);
        }
    }
    internal void Pause(bool value) {
        lock(gate) {
            if(stopping || paused==value)return;
            paused=value;generation++;EndProductionWindow();queue.Clear();Monitor.PulseAll(gate);
        }
    }
    // Never join, flush or free a stream on the UI thread. A blocked writer keeps
    // its stream and pinned buffer alive until it returns (or the process exits).
    internal void Stop() {lock(gate){if(stopping)return;stopping=true;generation++;EndProductionWindow();queue.Clear();Monitor.PulseAll(gate);}}
    void Feed() {
        IAudioOutput output=null;
        var samples=new short[chunkFrames*2];var pin=GCHandle.Alloc(samples,GCHandleType.Pinned);
        int observed=0;bool primed=false;
        try {
            lock(gate){if(stopping)return;}
            output=createOutput();if(output==null)throw new InvalidOperationException("Audio backend unavailable");
            lock(gate)opened=true;
            while(true) {
                bool flush=false;int batchGeneration;
                lock(gate) {
                    while(!stopping && observed==generation && (paused || (!primed && queue.Count<primeFrames)))Monitor.Wait(gate);
                    if(stopping)break;
                    if(observed!=generation){observed=generation;primed=false;flush=true;}
                    batchGeneration=generation;
                    if(!flush){primed=true;queue.Read(samples,chunkFrames);}
                }
                if(flush){output.Flush();lock(gate)serverMs=-1;continue;}
                lock(gate){if(stopping)break;if(paused || generation!=batchGeneration)continue;}
                long start=Stopwatch.GetTimestamp();output.Write(pin.AddrOfPinnedObject(),chunkFrames);
                long elapsed=Stopwatch.GetTimestamp()-start;
                bool query;
                lock(gate) {
                    submitted+=chunkFrames;writes++;writeTicks+=elapsed;maxWriteTicks=Math.Max(maxWriteTicks,elapsed);
                    if(elapsed>Stopwatch.Frequency/20)slowWrites++;
                    query=writes%50==0 && !stopping && generation==batchGeneration;
                }
                // Optional observation only: failure to query latency doesn't mute playback.
                if(query){double latency;try{latency=output.LatencyMs();}catch{latency=-1;}lock(gate)serverMs=latency;}
            }
            output.Flush();
        } catch(Exception e) {lock(gate){failure=e.Message;stopping=true;EndProductionWindow();queue.Clear();}}
        finally {
            try {output?.Dispose();}catch(Exception e){lock(gate)failure="Audio close: "+e.Message;}
            pin.Free();lock(gate){finished=true;Monitor.PulseAll(gate);}
        }
    }
    static string F(double value)=>value.ToString("F1",CultureInfo.InvariantCulture);
    internal string Summary {
        get {lock(gate) {
            string state=failure!=null?"unavailable: "+failure:stopping?"stopping":paused?"paused":!opened?"opening":writes==0?"buffering":"playing";
            return "Audio "+state+" | queue "+F(queue.Count*1000.0/rate)+" ms | gaps "+queue.Underruns;
        }}
    }
    internal string Diagnostics {
        get {lock(gate)return "AUDIO rate="+rate+" requested_buffer_ms="+bufferMs+" "+Summary+
            " produced="+queue.Produced+" dequeued="+queue.Dequeued+" submitted="+submitted+
            " production_active_s="+F(ProductionSeconds)+" pcm_production_pct="+(ProductionSeconds>=1?F(100*queue.Produced/(rate*ProductionSeconds)):"n/a")+
            " queue_high_ms="+F(queue.HighWater*1000.0/rate)+" queue_underruns="+queue.Underruns+
            " silence_frames="+queue.Missing+" overflow_drops="+queue.Dropped+" pause_stop_discards="+queue.Flushed+
            " server_latency_ms="+(serverMs<0?"unavailable":F(serverMs))+" writes="+writes+
            " write_total_ms="+F(writeTicks*1000.0/Stopwatch.Frequency)+" write_max_ms="+F(maxWriteTicks*1000.0/Stopwatch.Frequency)+" writes_over_50ms="+slowWrites;}
    }
}
