using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// Legacy scalar ABI checked against upstream EFL v1.20.7 headers and TV exports.
// Opaque handles only; no Evas/GL structs or GL function tables are dereferenced.
internal sealed class GlViewApi : IGlViewApi
{
    [DllImport("libdl.so.2")] static extern IntPtr dlopen(string name,int flags);
    [DllImport("libdl.so.2")] static extern IntPtr dlsym(IntPtr handle,string name);
    [DllImport("libdl.so.2")] static extern int dlclose(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr AddCall(IntPtr parent,int version);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate byte ModeCall(IntPtr view,int mode);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void CallbackCall(IntPtr view,IntPtr callback);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void PairCall(IntPtr view,int a,int b);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void ObjectCall(IntPtr view);
    IntPtr elm,evas;
    AddCall add; ModeCall mode; PairCall size,move,resize;
    ObjectCall show,hide,changed,delete;
    readonly Dictionary<string,CallbackCall> callbacks=new Dictionary<string,CallbackCall>();
    static T Bind<T>(IntPtr library,string name) where T:Delegate {
        var p=dlsym(library,name); if(p==IntPtr.Zero) throw new Exception("Missing GLView symbol: "+name);
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }
    internal GlViewApi()
    {
        try {
            elm=dlopen("libelementary.so.1",2); evas=dlopen("libevas.so.1",2);
            if(elm==IntPtr.Zero || evas==IntPtr.Zero) throw new Exception("GLView EFL library unavailable");
            add=Bind<AddCall>(elm,"elm_glview_version_add"); mode=Bind<ModeCall>(elm,"elm_glview_mode_set");
            size=Bind<PairCall>(elm,"elm_glview_size_set"); changed=Bind<ObjectCall>(elm,"elm_glview_changed_set");
            foreach(var kind in new[]{"init","render","resize","del"}) callbacks[kind]=Bind<CallbackCall>(elm,"elm_glview_"+kind+"_func_set");
            move=Bind<PairCall>(evas,"evas_object_move"); resize=Bind<PairCall>(evas,"evas_object_resize");
            show=Bind<ObjectCall>(evas,"evas_object_show"); hide=Bind<ObjectCall>(evas,"evas_object_hide");
            delete=Bind<ObjectCall>(evas,"evas_object_del");
        } catch { Dispose(); throw; }
    }
    public IntPtr Add(IntPtr parent) => add(parent,3); // EVAS_GL_GLES_3_X
    public bool Mode(IntPtr view,int value) => mode(view,value)!=0; // Eina_Bool is unsigned char.
    public void Callback(IntPtr view,string kind,GlViewCallback cb) => callbacks[kind](view,cb==null?IntPtr.Zero:Marshal.GetFunctionPointerForDelegate(cb));
    public void Size(IntPtr view,int w,int h) => size(view,w,h);
    public void Place(IntPtr view,int x,int y,int w,int h) { move(view,x,y); resize(view,w,h); }
    public void Visible(IntPtr view,bool visible) { if(visible) show(view); else hide(view); }
    public void Changed(IntPtr view) => changed(view);
    public void Delete(IntPtr view) => delete(view);
    public void Dispose() { if(elm!=IntPtr.Zero) { dlclose(elm); elm=IntPtr.Zero; } if(evas!=IntPtr.Zero) { dlclose(evas); evas=IntPtr.Zero; } }
}
