using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void GlViewCallback(IntPtr view);
internal interface IGlViewRenderer : IDisposable
{
    int Clear(bool diagnose,out uint error,out int framebuffer);
}
internal interface IGlViewApi : IDisposable
{
    IntPtr Add(IntPtr parent);
    bool Mode(IntPtr view,int mode);
    void Callback(IntPtr view,string kind,GlViewCallback callback);
    void Size(IntPtr view,int w,int h);
    void Place(IntPtr view,int x,int y,int w,int h);
    void Visible(IntPtr view,bool visible);
    void Changed(IntPtr view);
    void Delete(IntPtr view);
}

// All entry points, callbacks and destruction belong to the UI thread.
// Native code is not unmapped until callbacks are detached and deletion requested.
internal sealed class GlViewBridge : IDisposable
{
    readonly IGlViewApi api;
    readonly IGlViewRenderer renderer;
    readonly Func<bool> isUiThread;
    readonly Action<string> log;
    readonly GlViewCallback init,render,resize,deleted;
    IntPtr view;
    bool disposed,disposing,active,insideCallback;
    int width,height;
    double submitMs;
    internal bool Faulted { get; private set; }
    internal long Frames { get; private set; }
    internal long Callbacks { get; private set; }
    internal int Errors { get; private set; }
    internal int Framebuffer { get; private set; }=-1;
    internal string Summary => "backend=glview-hw-shader-test callbacks="+Callbacks+" shader_frames="+Frames+
        " errors="+Errors+" framebuffer="+Framebuffer+" average_cpu_frame_ms="+
        (Frames==0?"n/a":(submitMs/Frames).ToString("F3",CultureInfo.InvariantCulture))+" readbacks=0";
    internal GlViewBridge(IGlViewApi api,IGlViewRenderer renderer,Func<bool> isUiThread,Action<string> log)
    {
        this.api=api; this.renderer=renderer; this.isUiThread=isUiThread; this.log=log;
        init=o=>Guard(()=>Log("GLVIEW init callback; UI thread=true"));
        resize=o=>Guard(()=>Log("GLVIEW resize callback"));
        render=o=>Guard(Draw);
        deleted=o=>Guard(()=> { view=IntPtr.Zero; Fail("Widget destroyed by parent"); });
    }
    void Log(string text) { try { log(text); } catch { } }
    void RequireUi() { if(!isUiThread()) throw new InvalidOperationException("GLView operation requires UI thread"); }
    void Guard(Action action)
    {
        // Exceptions must never cross the reverse-P/Invoke boundary.
        if(disposed || disposing) return;
        if(insideCallback) { Fail("Reentrant GLView callback"); return; }
        insideCallback=true;
        try { RequireUi(); action(); }
        catch(Exception e) { Fail(e.Message); }
        finally { insideCallback=false; }
    }
    void Fail(string message) { if(!Faulted) { Errors++; Log("GLVIEW_ERROR "+message); } Faulted=true; active=false; }
    internal void Start(IntPtr parent)
    {
        RequireUi();
        if(disposed || view!=IntPtr.Zero) throw new InvalidOperationException("GLView cannot be started twice");
        try {
            view=api.Add(parent);
            if(view==IntPtr.Zero) throw new Exception("elm_glview_add returned NULL");
            // Explicitly disable direct-window rendering; clear only its bound FBO.
            if(!api.Mode(view,0)) throw new Exception("elm_glview_mode_set(ELM_GLVIEW_NONE) failed");
            api.Callback(view,"init",init); api.Callback(view,"resize",resize);
            api.Callback(view,"del",deleted); api.Callback(view,"render",render);
            Log("GLVIEW created; callbacks registered; mode=NONE; default on-demand policy.");
        } catch { Dispose(); throw; }
    }
    internal void Layout(int x,int y,int w,int h,bool visible)
    {
        RequireUi();
        if(disposed || view==IntPtr.Zero) return;
        bool changed=w!=width || h!=height;
        api.Place(view,x,y,w,h);
        if(changed) { api.Size(view,w,h); width=w; height=h; }
        active=visible && !Faulted;
        api.Visible(view,active);
        if(active && changed) api.Changed(view);
    }
    internal void Tick()
    {
        RequireUi();
        if(!disposed && active && !Faulted && view!=IntPtr.Zero) api.Changed(view);
    }
    void Draw()
    {
        if(!active || Faulted) return;
        Callbacks++;
        var timer=Stopwatch.StartNew(); uint error; int framebuffer;
        int stage=renderer.Clear(Frames==0,out error,out framebuffer);
        timer.Stop(); Framebuffer=framebuffer;
        if(stage!=0) { Fail("clear stage="+stage+" gl_error=0x"+error.ToString("X")+" framebuffer="+framebuffer); return; }
        Frames++; submitMs+=timer.Elapsed.TotalMilliseconds;
        if(Frames==1) Log("GLVIEW_FIRST_SHADER_OK framebuffer="+framebuffer+"; confirm animated RGB triangle on green viewport; synthetic core, no PPSSPP.");
    }
    internal void NoCallbacks() { RequireUi(); Fail("No successful shader frame within 5 active seconds; inspect GLView/context support"); }
    public void Dispose()
    {
        RequireUi();
        if(disposed) return;
        if(insideCallback) throw new InvalidOperationException("Dispose must run outside a render callback");
        disposing=true; active=false;
        // If detachment throws, retain this owner and mapped module: caller must not
        // discard it. Unmapping while native code holds a callback would be unsafe.
        if(view!=IntPtr.Zero) {
            foreach(var kind in new[]{"render","init","resize","del"}) api.Callback(view,kind,null);
            api.Delete(view); view=IntPtr.Zero;
        }
        renderer.Dispose(); api.Dispose(); disposed=true;
        GC.KeepAlive(init); GC.KeepAlive(render); GC.KeepAlive(resize); GC.KeepAlive(deleted);
        Log("GLVIEW disposed: callbacks detached; widget delete requested; native module released.");
    }
}
