using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

internal static partial class NativeGraphics
{
    // Permanent owner: glibc retains C++ destructor callbacks into this mapping.
    // Do not unmap it or close its dependency handles, even after retro_deinit.
    static CoreStartup coreStartup;
    internal static string StartPpsspp(string resources,string data,Action<string> log)
    {
        if(coreStartup!=null)return coreStartup.Result;
        coreStartup=new CoreStartup(resources,data,log);
        return coreStartup.Run();
    }
    internal sealed partial class CoreStartup
    {
        const string ExpectedHash="43af35a686c48473549aa6d76f49831c41b317be9d6e5cb835577c20da018bd8";
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void VoidCall();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint ApiVersionCall();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void PointerCall(IntPtr argument);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate byte EnvironmentCall(uint command,IntPtr argument);
        readonly string resources,data,marker;
        readonly Action<string> log;
        readonly Dictionary<string,IntPtr> handles=new Dictionary<string,IntPtr>();
        readonly Dictionary<string,IntPtr> strings=new Dictionary<string,IntPtr>();
        readonly Dictionary<string,string> options=new Dictionary<string,string>();
        IntPtr memory=new IntPtr(-1);
        DynamicElf plan;
        EnvironmentCall environment;
        VersionLookup versions;
        volatile string callbackError;
        readonly object environmentLock=new object();
        internal string Result="PPSSPP startup already attempted; relaunch before retry";
        internal CoreStartup(string resources,string data,Action<string> log) {
            this.resources=resources;this.data=data;this.log=log;marker=Path.Combine(data,"core-startup.pending");
        }
        void Stage(string stage) {File.WriteAllText(marker,stage);JitStage(stage);log("CORE_STARTUP "+stage);}
        IntPtr Text(string value) {
            IntPtr p;if(!strings.TryGetValue(value,out p)) {p=Marshal.StringToHGlobalAnsi(value);strings.Add(value,p);}return p;
        }
        T Function<T>(string name) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(IntPtr.Add(memory,plan.Export(name)));
        void Initialize(int offset) {var f=Marshal.GetDelegateForFunctionPointer<VoidCall>(IntPtr.Add(memory,offset));f();GC.KeepAlive(f);}
        uint Resolve(DynamicElf.Symbol symbol) {
            IntPtr address=IntPtr.Zero;
            if(symbol.Version.Length>0)address=versions(handles[symbol.Library],symbol.Name,symbol.Version);
            else foreach(var h in handles.Values) {address=dlsym(h,symbol.Name);if(address!=IntPtr.Zero)break;}
            if(address==IntPtr.Zero&&symbol.Binding!=2)throw new Exception("Core import missing: "+symbol.Name+" "+symbol.Version);
            return unchecked((uint)address.ToInt32());
        }
        internal string Run() {
            try {
                if(IntPtr.Size!=4||RuntimeInformation.ProcessArchitecture!=Architecture.Arm)throw new Exception("ARM32 TV required");
                if(gamePath!=null) {
                    HostPerformanceInfo.LogPlacement(log);
                    ProbeRuntime(log);
                    log("GAME_AFFINITY policy=inherited; native compute pool follows effective CPU access");
                }
                jitActive=PrepareJitAttempt();
                Stage("verify cached core hash and ELF");
                byte[] bytes=File.ReadAllBytes(Path.Combine(resources,jitActive?"ppsspp_libretro-arm32-jit.so":"ppsspp_libretro-arm32.so"));
                if(Hash(bytes)!=(jitActive?ExpectedJitHash:ExpectedHash))throw new Exception("Unreviewed core checksum; refusing runtime loader policy");
                log("CORE_PROFILE "+CpuSummary+" sha256="+Hash(bytes));
                plan=DynamicElf.Parse(bytes,getpagesize(),"retro_init",true,true);
                foreach(string library in new[]{"libc.so.6","libm.so.6","libpthread.so.0","libdl.so.2","librt.so.1","libEGL.so.1","libGLESv2.so.2"}) {
                    var h=dlopen(library,2);if(h==IntPtr.Zero)throw new Exception("Core library unavailable: "+library);handles.Add(library,h);
                }
                var lookup=dlsym(handles["libdl.so.2"],"dlvsym");if(lookup==IntPtr.Zero)throw new Exception("dlvsym unavailable");
                versions=Marshal.GetDelegateForFunctionPointer<VersionLookup>(lookup);
                var resolved=new Dictionary<DynamicElf.Symbol,uint>();
                foreach(var symbol in plan.Symbols)if(symbol.Section==0&&symbol.Name.Length>0)resolved.Add(symbol,Resolve(symbol));
                Stage("imports resolved; map and relocate "+plan.Image.Length+" bytes");
                memory=mmap(IntPtr.Zero,new UIntPtr((uint)plan.Image.Length),3,0x22,-1,IntPtr.Zero);
                if(memory==new IntPtr(-1))throw new Exception("Core mmap failed");
                var image=plan.RelocateSymbols(unchecked((uint)memory.ToInt32()),s=>resolved[s]);
                Marshal.Copy(image,0,memory,image.Length);
                if(Flush(0x0f0002,memory,IntPtr.Add(memory,image.Length),0)!=0)throw new Exception("Core cache flush failed");
                for(int i=0;i<plan.Protections.Length;i++)if(mprotect(IntPtr.Add(memory,i*4096),new UIntPtr(4096),plan.Protections[i])!=0)throw new Exception("Core page protection failed");
                Stage("W^X applied; DT_INIT");
                if(plan.InitFunction!=0)Initialize(plan.InitFunction);
                Stage("C++ constructors: "+plan.Initializers.Count);
                int count=0;
                new ModuleInitializers().Run(plan.Initializers,offset=> {
                    File.WriteAllText(marker,"C++ constructor "+(++count)+"/"+plan.Initializers.Count);
                    Initialize(offset);
                });
                Stage("C++ constructors complete; query Libretro API");
                StartJitDiagnostics();
                if(Function<ApiVersionCall>("retro_api_version")()!=1)throw new Exception("Unsupported Libretro API version");
                IntPtr info=Marshal.AllocHGlobal(32);string name,version;
                try {
                    Function<PointerCall>("retro_get_system_info")(info);
                    name=Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(info))??"unknown";
                    version=Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(info,4))??"unknown";
                } finally {Marshal.FreeHGlobal(info);}
                log("CORE_IDENTITY "+name+" "+version);
                Directory.CreateDirectory(Path.Combine(data,"PPSSPP"));
                environment=Environment;
                if(gamePath!=null) PrepareGameCallbacks();
                else Function<PointerCall>("retro_set_environment")(Marshal.GetFunctionPointerForDelegate(environment));
                if(callbackError!=null)throw new Exception(callbackError);
                Stage("retro_init: configuration, TLS and worker startup");
                Function<VoidCall>("retro_init")();
                if(gamePath!=null) {
                    LoadContent();
                    Result="PPSSPP content accepted; waiting for GLView";
                    return Result;
                }
                Stage("retro_init returned; retro_deinit worker shutdown");
                Function<VoidCall>("retro_deinit")();
                if(callbackError!=null)throw new Exception(callbackError);
                Stage("CORE_STARTUP_PASS: init and shutdown returned; mapping retained");
                File.Delete(marker);
                Result="PPSSPP startup PASS - core initialized and shut down; no game loaded";
            } catch(Exception e) {
                audio?.Stop();
                Result="PPSSPP startup failed: "+e.Message;
                log("CORE_STARTUP_ERROR "+e);
                // Keep all native addresses and callback roots alive after partial initialization.
            }
            return Result;
        }
        byte Environment(uint command,IntPtr argument) {
            lock(environmentLock) {
            try {
                switch(command) {
                case 9: Marshal.WriteIntPtr(argument,Text(resources));return 1;
                case 31: Marshal.WriteIntPtr(argument,Text(data));return 1;
                case 38: Marshal.WriteIntPtr(argument,Text("TizenPSP"));return 1;
                case 39: case 52: case 59: Marshal.WriteInt32(argument,0);return 1;
                case 17: case 74: Marshal.WriteByte(argument,0);return 1;
                case 37: // SET_GEOMETRY: lock the selected render size for this launch.
                    if(Marshal.ReadInt32(argument)!=(int)RenderWidth || Marshal.ReadInt32(argument,4)!=(int)RenderHeight) {
                        callbackError="Unsupported core geometry; expected "+RenderWidth+"x"+RenderHeight;return 0;
                    }
                    return 1;
                case 11: case 35: return 1; // input descriptions/controller metadata
                case 6: log("CORE_MESSAGE "+Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(argument)));return 1;
                case 16:
                    for(int i=0;i<512;i++) {
                        var entry=IntPtr.Add(argument,i*2*IntPtr.Size);var keyPtr=Marshal.ReadIntPtr(entry);
                        if(keyPtr==IntPtr.Zero)return 1;
                        string key=Marshal.PtrToStringAnsi(keyPtr),definition=Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(entry,IntPtr.Size));
                        int separator=definition.IndexOf(';');if(separator<0)throw new FormatException("Core option definition");
                        options[key]=definition.Substring(separator+1).TrimStart().Split('|')[0];
                    }
                    throw new Exception("Too many core options");
                case 15:
                    string option=Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(argument)),value;
                    if(!CoreOptionPolicy.TryOverride(option,jitActive,fastMemory,resolutionScale,frameSkip,skipBufferEffects,out value) &&
                        !options.TryGetValue(option,out value)) {Marshal.WriteIntPtr(argument,IntPtr.Size,IntPtr.Zero);return 0;}
                    Marshal.WriteIntPtr(argument,IntPtr.Size,Text(value));return 1;
                default:return 0; // No pretend video/audio/content services in startup-only test.
                }
            } catch(Exception e) {callbackError="Core environment: "+e.Message;return 0;}
            }
        }
    }
}
