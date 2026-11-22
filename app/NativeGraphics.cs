// Adapted from TizenPS 0.3.33 Native.cs, 2026-11-08. GPL-2.0.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal static partial class NativeGraphics
{
    [DllImport("libdl.so.2")] static extern IntPtr dlopen(string name, int flags);
    [DllImport("libdl.so.2")] static extern IntPtr dlsym(IntPtr handle, string name);
    [DllImport("libdl.so.2")] static extern int dlclose(IntPtr handle);
    [DllImport("libc.so.6", SetLastError=true)] static extern IntPtr mmap(IntPtr a, UIntPtr s, int p, int f, int fd, IntPtr o);
    [DllImport("libc.so.6", SetLastError=true)] static extern int mprotect(IntPtr a, UIntPtr s, int p);
    [DllImport("libc.so.6")] static extern int munmap(IntPtr a, UIntPtr s);
    [DllImport("libc.so.6")] static extern int getpagesize();
    [DllImport("libc.so.6", EntryPoint="syscall")] static extern int Flush(int n, IntPtr a, IntPtr b, int flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Report(IntPtr message);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Triangle(IntPtr pixels, int w, int h, Report report, out uint error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DiagnosticTriangle(IntPtr pixels, int w, int h, Report report, out uint error, IntPtr queryString);

    internal static string Hash(byte[] bytes)
    {
        using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    static IntPtr Open(List<IntPtr> handles, Action<string> log, params string[] names)
    {
        foreach(var name in names) {
            var h=dlopen(name,2);
            if(h!=IntPtr.Zero) { handles.Add(h); log("Opened " + name); return h; }
        }
        throw new Exception("Unavailable native library: " + string.Join(" / ",names));
    }
    internal static void Probe(Action<string> log)
    {
        foreach(var pair in new[] {
            new[]{"libevas.so.1","evas_object_evas_get","evas_gl_new","evas_gl_free","evas_gl_config_new","evas_gl_config_free","evas_gl_context_create","evas_gl_context_destroy","evas_gl_surface_create","evas_gl_surface_destroy","evas_gl_make_current","evas_gl_api_get","evas_gl_native_surface_get","evas_object_image_native_surface_set","evas_gl_error_get"},
            new[]{"libelementary.so.1","elm_glview_add","elm_glview_version_add","elm_glview_gl_api_get","elm_glview_evas_gl_get","elm_glview_mode_set","elm_glview_size_set","elm_glview_render_policy_set","elm_glview_resize_policy_set","elm_glview_init_func_set","elm_glview_del_func_set","elm_glview_resize_func_set","elm_glview_render_func_set","elm_glview_changed_set"}
        }) {
            var h=dlopen(pair[0],2);
            if(h==IntPtr.Zero) { log("Probe unavailable: "+pair[0]); continue; }
            try { for(int i=1;i<pair.Length;i++) log(pair[i]+": "+(dlsym(h,pair[i])!=IntPtr.Zero?"export present":"missing")); }
            finally { dlclose(h); }
        }
        log("Export presence does not prove usable EvasGL contexts.");
    }
    internal static void Render(string resources, IntPtr pixels, int w, int h, Action<string> log, bool baseline=false)
    {
        if(IntPtr.Size!=4 || RuntimeInformation.ProcessArchitecture!=Architecture.Arm)
            throw new Exception("ARM32 TV required; this is not a desktop graphics test.");
        var bytes=File.ReadAllBytes(Path.Combine(resources,baseline?"libtizenpsp_triangle_v010.so":"libtizenpsp_triangle.so"));
        if(Hash(bytes)!=File.ReadAllText(Path.Combine(resources,baseline?"triangle-v010.sha256":"triangle.sha256")).Trim())
            throw new Exception("Native triangle checksum mismatch");
        int page=getpagesize();
        var plan=DynamicElf.Parse(bytes,page,baseline?"tizenpsp_triangle":"tizenpsp_triangle_diagnostics");
        var handles=new List<IntPtr>();
        IntPtr mem=new IntPtr(-1); var size=new UIntPtr((uint)plan.Image.Length);
        Report report=p=> { try { log(Marshal.PtrToStringAnsi(p) ?? "(null GL string)"); } catch { /* Never unwind through C. */ } };
        try {
            var egl=Open(handles,log,"libEGL.so.1","libEGL.so");
            Open(handles,log,"libGLESv2.so.2","libGLESv2.so");
            var query=dlsym(egl,"eglQueryString");
            log("eglQueryString: "+(query==IntPtr.Zero?"missing; EGL strings unavailable":"export present"));
            var imports=new Dictionary<string,uint>();
            foreach(var symbol in plan.Symbols) {
                if(symbol.Section!=0 || symbol.Name.Length==0 || imports.ContainsKey(symbol.Name)) continue;
                IntPtr address=IntPtr.Zero;
                foreach(var handle in handles) { address=dlsym(handle,symbol.Name); if(address!=IntPtr.Zero) break; }
                if(address==IntPtr.Zero) throw new Exception("Unresolved graphics import: "+symbol.Name);
                imports[symbol.Name]=unchecked((uint)address.ToInt32());
            }
            mem=mmap(IntPtr.Zero,size,3,0x22,-1,IntPtr.Zero);
            if(mem==new IntPtr(-1)) throw new Exception("Native allocation failed");
            var relocated=plan.Relocate(unchecked((uint)mem.ToInt32()),n=>imports[n]);
            Marshal.Copy(relocated,0,mem,relocated.Length);
            if(Flush(0x0f0002,mem,IntPtr.Add(mem,relocated.Length),0)!=0) throw new Exception("Instruction cache flush failed");
            for(int i=0;i<plan.Protections.Length;i++)
                if(mprotect(IntPtr.Add(mem,i*page),new UIntPtr((uint)page),plan.Protections[i])!=0)
                    throw new Exception("Native segment protection failed");
            uint error; int result;
            if(baseline) {
                var draw=Marshal.GetDelegateForFunctionPointer<Triangle>(IntPtr.Add(mem,plan.Entry));
                result=draw(pixels,w,h,report,out error);
                if(result==0) log("FRAME_RENDERED");
                log("Baseline v0.1.0: context creation failure counter unavailable (original ABI).");
            } else {
                var draw=Marshal.GetDelegateForFunctionPointer<DiagnosticTriangle>(IntPtr.Add(mem,plan.Entry));
                result=draw(pixels,w,h,report,out error,query);
            }
            if(result!=0) throw new Exception("Graphics stage "+result+", EGL/GLES error 0x"+error.ToString("X"));
            log("Native GLES triangle readback completed.");
        } finally {
            GC.KeepAlive(report);
            if(mem!=new IntPtr(-1)) munmap(mem,size);
            for(int i=handles.Count-1;i>=0;i--) dlclose(handles[i]);
        }
    }
}
