using System;
using System.IO;
using System.Linq;
class Checks
{
    static void Main(string[] args)
    {
        if(args.Length==2&&args[0]=="--live-fixture"){LiveChecks.Integration(args[1]);return;}
        LiveChecks.Run();
        DiagnosticsChecks.Run();
        ReportExportChecks.Run();
        HostPerformanceChecks.Run();
        ThreadCpuChecks.Run();
        UsbGameChecks.Run();
        InputMapChecks.Run();
        StickInputChecks.Run();
        InputHotPathChecks.Run();
        ButtonIdentityChecks.Run();
        DualSenseChecks.Run();
        SdkControllerEventChecks.Run();
        ControllerReaderChecks.Run();
        ControllerNavigationChecks.Run();
        CoreImportChecks.Run();
        GlViewChecks.Run();
        AudioChecks.Run();
        FrameTimingChecks.Run();
        JitChecks.Run();
        LibraryChecks.Run();
        FrontendMenuChecks.Run();
        CoreLoggingChecks.Run();
        var bytes=File.ReadAllBytes(args[0]);
        var entry=args.Length>1?args[1]:"tizenpsp_triangle_diagnostics";
        if(entry=="--core") {CoreLoaderChecks.Run(bytes);return;}
        if(entry=="--arm-jit-core") {CoreLoaderChecks.Run(bytes,true);return;}
        if(entry=="--expect-initializer-rejection") {
            try { DynamicElf.Parse(bytes,4096,"tizenpsp_cpp_probe"); }
            catch(Exception e) when(e.Message.Contains("initializers")) {
                Console.WriteLine("CONFIRMED BLOCKER: current loader rejects C++ module initialization: "+e.Message);
                return;
            }
            throw new Exception("Expected constructor fixture rejection did not occur");
        }
        var plan=DynamicElf.Parse(bytes,4096,entry,allowInitializers:entry=="tizenpsp_glview_clear");
        if(entry=="tizenpsp_glview_clear") InitializerChecks.Run(bytes);
        if(entry=="tizenpsp_glview_clear" || entry=="psp_bridge_begin") {
            string[] forbidden={"glReadPixels","eglCreateContext","eglCreatePbufferSurface","eglMakeCurrent","eglSwapBuffers"};
            if(plan.Symbols.Any(s=>forbidden.Contains(s.Name))) throw new Exception("GLView module imports readback/context/FBO ownership functions");
        }
        if((DynamicElf.U32(bytes,36)&0x400)!=0) throw new Exception("Hard-float ABI is not compatible");
        if(entry!="psp_bridge_begin" && plan.Symbols.Any(s=>s.Section==0 && s.Name.Length>0 && !s.Name.StartsWith("egl") && !s.Name.StartsWith("gl")))
            throw new Exception("Unexpected non-graphics import");
        foreach(uint bias in new[]{0x10000000u,0x70000000u}) {
            var relocated=plan.Relocate(bias,n=>0x12340000u);
            foreach(var r in plan.Relocations) {
                var s=plan.Symbols[r.Symbol];
                uint expected=r.Type==23?bias+DynamicElf.U32(plan.Image,r.Offset):
                    (s.Section==0?0x12340000u:bias+s.Value)+(r.Type==21?DynamicElf.U32(plan.Image,r.Offset):0);
                if(DynamicElf.U32(relocated,r.Offset)!=expected) throw new Exception("Relocation mismatch");
            }
        }
        var bad=(byte[])bytes.Clone(); bad[4]=2;
        try { DynamicElf.Parse(bad,4096,entry); throw new InvalidOperationException("ELF64 accepted"); }
        catch(InvalidOperationException) { throw; } catch(Exception) { }
        try { plan.Relocate(0x10000000u,n=>0); throw new InvalidOperationException("Missing import accepted"); }
        catch(InvalidOperationException) { throw; } catch(Exception) { }
        Console.WriteLine("PASS: ARM32 soft-float ELF; supported relocations at two bases; ELF64 and unresolved imports rejected.");
        Console.WriteLine("Image bytes="+plan.Image.Length+"; imports="+plan.Symbols.Count(s=>s.Section==0 && s.Name.Length>0)+"; relocations="+plan.Relocations.Count);
    }
}
