using System;
using System.Runtime.InteropServices;

internal interface IAudioOutput : IDisposable
{
    void Write(IntPtr data,int frames);
    void Flush();
    double LatencyMs();
}

// PulseAudio ABI adapted from the read-only TizenPS v0.3.33 AudioOutput.cs
// reference (GPL-2.0). One feeder owns creation, all calls and destruction.
internal sealed class PulseAudioOutput : IAudioOutput
{
    [StructLayout(LayoutKind.Sequential)] struct Spec {public int format;public uint rate;public byte channels;}
    [StructLayout(LayoutKind.Sequential)] struct Attributes {public uint maxlength,tlength,prebuf,minreq,fragsize;}
    [DllImport("libpulse-simple.so.0",CallingConvention=CallingConvention.Cdecl)]
    static extern IntPtr pa_simple_new(IntPtr server,string name,int direction,IntPtr device,string stream,ref Spec spec,IntPtr map,ref Attributes attrs,out int error);
    [DllImport("libpulse-simple.so.0",CallingConvention=CallingConvention.Cdecl)]
    static extern int pa_simple_write(IntPtr stream,IntPtr data,UIntPtr bytes,out int error);
    [DllImport("libpulse-simple.so.0",CallingConvention=CallingConvention.Cdecl)]
    static extern int pa_simple_flush(IntPtr stream,out int error);
    [DllImport("libpulse-simple.so.0",CallingConvention=CallingConvention.Cdecl)]
    static extern ulong pa_simple_get_latency(IntPtr stream,out int error);
    [DllImport("libpulse-simple.so.0",CallingConvention=CallingConvention.Cdecl)]
    static extern void pa_simple_free(IntPtr stream);
    IntPtr stream;
    internal PulseAudioOutput(int rate,int bufferMs) {
        if(rate<8000 || rate>192000 || bufferMs<40 || bufferMs>250)throw new ArgumentOutOfRangeException();
        // Public PA_SAMPLE_S16LE=3, PA_STREAM_PLAYBACK=1. ARM32 TV is LE.
        var spec=new Spec{format=3,rate=(uint)rate,channels=2};
        uint target=(uint)(rate*bufferMs/1000)*4;
        var attributes=new Attributes{maxlength=target*2,tlength=target,prebuf=target/3*2/4*4,minreq=(uint)(rate/100)*4,fragsize=uint.MaxValue};
        int error;stream=pa_simple_new(IntPtr.Zero,"TizenPSP",1,IntPtr.Zero,"PSP game",ref spec,IntPtr.Zero,ref attributes,out error);
        if(stream==IntPtr.Zero)throw new InvalidOperationException("PulseAudio open error "+error);
    }
    public void Write(IntPtr data,int frames) {
        int error;if(pa_simple_write(stream,data,new UIntPtr((uint)(frames*4)),out error)<0)throw new InvalidOperationException("PulseAudio write error "+error);
    }
    public void Flush() {
        int error;if(pa_simple_flush(stream,out error)<0)throw new InvalidOperationException("PulseAudio flush error "+error);
    }
    public double LatencyMs() {
        int error;ulong usec=pa_simple_get_latency(stream,out error);
        return usec==ulong.MaxValue?-1:usec/1000.0;
    }
    public void Dispose() {if(stream!=IntPtr.Zero){pa_simple_free(stream);stream=IntPtr.Zero;}}
}
