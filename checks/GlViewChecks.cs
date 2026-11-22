using System;
using System.Collections.Generic;
internal static class GlViewChecks
{
    sealed class Api : IGlViewApi
    {
        internal readonly Dictionary<string,GlViewCallback> callbacks=new Dictionary<string,GlViewCallback>();
        internal readonly List<string> events;
        internal bool modeOk=true,detachFail;
        internal int changes;
        internal Api(List<string> events) { this.events=events; }
        public IntPtr Add(IntPtr p) { events.Add("add"); return new IntPtr(1); }
        public bool Mode(IntPtr p,int mode) { Need(mode==0,"Direct-window rendering must be disabled"); return modeOk; }
        public void Callback(IntPtr p,string kind,GlViewCallback cb) {
            if(detachFail && cb==null) throw new Exception("injected detach failure");
            callbacks[kind]=cb;
        }
        public void Size(IntPtr p,int w,int h) { }
        public void Place(IntPtr p,int x,int y,int w,int h) { }
        public void Visible(IntPtr p,bool visible) { }
        public void Changed(IntPtr p) { changes++; }
        public void Delete(IntPtr p) {
            foreach(var cb in callbacks.Values) Need(cb==null,"Callback still registered at deletion");
            events.Add("delete");
        }
        public void Dispose() { events.Add("api-dispose"); }
        internal void Render() { callbacks["render"]?.Invoke(new IntPtr(1)); }
    }
    sealed class Renderer : IGlViewRenderer
    {
        internal int calls,stage;
        readonly List<string> events;
        internal Renderer(List<string> events) { this.events=events; }
        public int Clear(bool first,out uint error,out int framebuffer) {
            calls++; error=stage==0?0u:0x502; framebuffer=7; return stage;
        }
        public void Dispose() { events.Add("module-dispose"); }
    }
    static void Need(bool value,string message) { if(!value) throw new Exception(message); }
    internal static void Run()
    {
        var events=new List<string>(); var api=new Api(events); var renderer=new Renderer(events);
        bool ui=true;
        var bridge=new GlViewBridge(api,renderer,()=>ui,s=>{});
        bridge.Start(new IntPtr(9)); bridge.Layout(0,0,640,360,true);
        api.Render(); api.Render(); Need(bridge.Frames==2 && renderer.calls==2,"Callback did not reach native renderer");
        bridge.Layout(0,0,640,360,false); int changes=api.changes;
        bridge.Tick(); api.Render(); Need(changes==api.changes && renderer.calls==2,"Hidden/paused view still rendering");
        bridge.Layout(0,0,640,360,true); renderer.stage=4; api.Render();
        Need(bridge.Faulted && bridge.Frames==2 && bridge.Errors==1,"Failed clear falsely counted as success");
        bridge.Tick(); api.Render(); Need(renderer.calls==3,"Faulted view kept calling renderer");
        api.detachFail=true;
        try { bridge.Dispose(); throw new InvalidOperationException("Detach failure swallowed"); }
        catch(InvalidOperationException) { throw; } catch(Exception) { }
        Need(!events.Contains("module-dispose"),"Code unmapped despite registered callbacks");
        api.Render(); Need(renderer.calls==3,"Callback executed during failed disposal");
        api.detachFail=false; bridge.Dispose(); bridge.Dispose();
        Need(string.Join(",",events)=="add,delete,module-dispose,api-dispose","Incorrect callback/module teardown order");

        events=new List<string>(); api=new Api(events); renderer=new Renderer(events);
        bridge=new GlViewBridge(api,renderer,()=>ui,s=>{}); bridge.Start(new IntPtr(9)); bridge.Layout(0,0,4,4,true);
        ui=false; api.Render(); ui=true;
        Need(bridge.Faulted && renderer.calls==0,"Off-UI callback entered native renderer"); bridge.Dispose();

        events=new List<string>(); api=new Api(events){modeOk=false}; renderer=new Renderer(events);
        bridge=new GlViewBridge(api,renderer,()=>true,s=>{});
        try { bridge.Start(new IntPtr(9)); throw new InvalidOperationException("Rejected mode ignored"); }
        catch(InvalidOperationException) { throw; } catch(Exception) { }
        Need(events.Contains("module-dispose"),"Creation failure leaked module");
        Console.WriteLine("PASS: GLView callback gating, pause/hide, error stop, UI ownership and detach-before-unmap lifetime.");
    }
}
