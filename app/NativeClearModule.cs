using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

internal static partial class NativeGraphics
{
    // Same ELF mapping/relocation/cacheflush/protection sequence as the tested
    // one-shot loader. Lifetime now spans GLView callbacks, with explicit Dispose.
    internal sealed class ClearModule : IGlViewRenderer
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int ClearCall(int diagnose,Report report,out uint error,out int framebuffer);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void InitCall();
        readonly List<IntPtr> handles=new List<IntPtr>();
        IntPtr memory=new IntPtr(-1);
        UIntPtr size;
        ClearCall clear;
        Report report;
        bool disposed;
        internal ClearModule(string resources,Action<string> log)
        {
            try {
                if(IntPtr.Size!=4 || RuntimeInformation.ProcessArchitecture!=Architecture.Arm)
                    throw new Exception("ARM32 TV required");
                var bytes=File.ReadAllBytes(Path.Combine(resources,"libtizenpsp_glview.so"));
                if(Hash(bytes)!=File.ReadAllText(Path.Combine(resources,"glview.sha256")).Trim())
                    throw new Exception("GLView native checksum mismatch");
                int page=getpagesize();
                var plan=DynamicElf.Parse(bytes,page,"tizenpsp_glview_clear",allowInitializers:true);
                Open(handles,log,"libEGL.so.1","libEGL.so");
                Open(handles,log,"libGLESv2.so.2","libGLESv2.so");
                var imports=new Dictionary<string,uint>();
                foreach(var symbol in plan.Symbols) {
                    if(symbol.Section!=0 || symbol.Name.Length==0 || imports.ContainsKey(symbol.Name)) continue;
                    IntPtr address=IntPtr.Zero;
                    foreach(var h in handles) { address=dlsym(h,symbol.Name); if(address!=IntPtr.Zero) break; }
                    if(address==IntPtr.Zero) throw new Exception("Missing GLView graphics import: "+symbol.Name);
                    imports[symbol.Name]=unchecked((uint)address.ToInt32());
                }
                size=new UIntPtr((uint)plan.Image.Length);
                memory=mmap(IntPtr.Zero,size,3,0x22,-1,IntPtr.Zero);
                if(memory==new IntPtr(-1)) throw new Exception("Native allocation failed");
                var relocated=plan.Relocate(unchecked((uint)memory.ToInt32()),n=>imports[n]);
                Marshal.Copy(relocated,0,memory,relocated.Length);
                if(Flush(0x0f0002,memory,IntPtr.Add(memory,relocated.Length),0)!=0) throw new Exception("Instruction cache flush failed");
                for(int i=0;i<plan.Protections.Length;i++)
                    if(mprotect(IntPtr.Add(memory,i*page),new UIntPtr((uint)page),plan.Protections[i])!=0)
                        throw new Exception("Native segment protection failed");
                new ModuleInitializers().Run(plan.Initializers,offset=> {
                    var initialize=Marshal.GetDelegateForFunctionPointer<InitCall>(IntPtr.Add(memory,offset));
                    initialize(); GC.KeepAlive(initialize);
                });
                log("CPP_INITIALIZERS_EXECUTED count="+plan.Initializers.Count+"; after relocation/cache flush/W^X");
                clear=Marshal.GetDelegateForFunctionPointer<ClearCall>(IntPtr.Add(memory,plan.Entry));
                report=p=> { try { log(Marshal.PtrToStringAnsi(p)??"(null)"); } catch { } };
                log("GLVIEW_NATIVE mapped persistently; W^X segments retained; no pixel-readback imports.");
            } catch { Dispose(); throw; }
        }
        public int Clear(bool diagnose,out uint error,out int framebuffer)
        {
            if(disposed) throw new ObjectDisposedException(nameof(ClearModule));
            return clear(diagnose?1:0,report,out error,out framebuffer);
        }
        public void Dispose()
        {
            if(disposed) return;
            disposed=true;
            GC.KeepAlive(report); GC.KeepAlive(clear);
            if(memory!=new IntPtr(-1)) { munmap(memory,size); memory=new IntPtr(-1); }
            for(int i=handles.Count-1;i>=0;i--) dlclose(handles[i]);
            handles.Clear(); clear=null; report=null;
        }
    }
}
