using System;
using System.Runtime.InteropServices;

// Interleaved S16 stereo, in frames throughout. The caller holds AudioPipeline's
// short managed lock; neither native audio calls nor waits occur under that lock.
internal sealed class PcmQueue
{
    readonly short[] samples;
    int read,count;
    bool starving;
    internal readonly int Rate,Capacity;
    internal long Produced,Dequeued,Dropped,Missing,Underruns,Flushed;
    internal int HighWater { get; private set; }
    internal int Count=>count;
    internal PcmQueue(int rate,int capacity) {
        if(rate<8000 || rate>192000 || capacity<1 || capacity>rate)throw new ArgumentOutOfRangeException();
        Rate=rate;Capacity=capacity;samples=new short[capacity*2];
    }
    internal void Push(IntPtr source,int frames) {
        if(frames<0 || frames>Rate || (frames>0 && source==IntPtr.Zero))throw new ArgumentOutOfRangeException(nameof(frames));
        if(frames==0)return;
        Produced+=frames;
        if(frames>Capacity) {int skip=frames-Capacity;source=IntPtr.Add(source,skip*4);frames=Capacity;Dropped+=skip;}
        int overflow=Math.Max(0,count+frames-Capacity);
        read=(read+overflow)%Capacity;count-=overflow;Dropped+=overflow;
        int write=(read+count)%Capacity,first=Math.Min(frames,Capacity-write);
        Marshal.Copy(source,samples,write*2,first*2);
        if(first<frames)Marshal.Copy(IntPtr.Add(source,first*4),samples,0,(frames-first)*2);
        count+=frames;HighWater=Math.Max(HighWater,count);
    }
    internal int Read(short[] destination,int frames) {
        if(destination==null || frames<0 || frames>destination.Length/2)throw new ArgumentOutOfRangeException(nameof(frames));
        int available=Math.Min(count,frames),first=Math.Min(available,Capacity-read);
        Array.Copy(samples,read*2,destination,0,first*2);
        if(first<available)Array.Copy(samples,0,destination,first*2,(available-first)*2);
        read=(read+available)%Capacity;count-=available;Dequeued+=available;
        if(available<frames) {
            Array.Clear(destination,available*2,(frames-available)*2);
            Missing+=frames-available;if(!starving)Underruns++;starving=true;
        } else starving=false;
        return available;
    }
    internal void Clear() {Flushed+=count;count=read=0;starving=false;}
}
