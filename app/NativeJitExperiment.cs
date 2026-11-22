using System;
using System.IO;
using System.Runtime.InteropServices;

internal static partial class NativeGraphics
{
    sealed class JitMemory : IJitMemory
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ProbeCall();
        [DllImport("libc.so.6",EntryPoint="syscall",SetLastError=true)] static extern int ProbeFlush(int number,IntPtr start,IntPtr end,int flags);
        public int PageSize=>getpagesize();
        public int Error {get;private set;}
        public IntPtr Allocate(int bytes) {var p=mmap(IntPtr.Zero,new UIntPtr((uint)bytes),3,0x22,-1,IntPtr.Zero);Error=Marshal.GetLastWin32Error();return p;}
        public bool Protect(IntPtr pointer,int bytes,int flags) {int result=mprotect(pointer,new UIntPtr((uint)bytes),flags);Error=Marshal.GetLastWin32Error();return result==0;}
        public bool Flush(IntPtr pointer,int bytes) {int result=ProbeFlush(0x0f0002,pointer,IntPtr.Add(pointer,bytes),0);Error=Marshal.GetLastWin32Error();return result==0;}
        public void Write(IntPtr pointer,uint instruction) {Marshal.WriteInt32(pointer,unchecked((int)instruction));}
        public int Execute(IntPtr pointer) {var call=Marshal.GetDelegateForFunctionPointer<ProbeCall>(pointer);int result=call();GC.KeepAlive(call);return result;}
        public void Release(IntPtr pointer,int bytes) {munmap(pointer,new UIntPtr((uint)bytes));}
    }
    internal sealed partial class CoreStartup
    {
        internal const string ExpectedJitHash="9be6c183a0550703102b36dc3ea435b1a8102a968c3baa891b6aa5aa6c3b876b";
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int JitInit(IntPtr path);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint JitSnapshot(IntPtr buffer,uint size);
        internal bool requestJit;
        bool jitActive,jitAttempt;
        JitSnapshot jitSnapshot;
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint ExperimentSelect(uint value);
        ExperimentSelect vertexSelect;ApiVersionCall vertexStatus;
        internal bool LiveReady=>jitActive&&!closed&&!gamePaused&&!Faulted&&Frames>0;
        internal bool LiveSupported=>vertexStatus!=null&&(vertexStatus()&2)!=0;
        internal bool SelectLiveVariant(bool optimized){if(vertexSelect==null||closed)return false;uint status=vertexSelect(optimized?1u:0u);return status!=uint.MaxValue&&(status&1)==(optimized?1u:0u);}
        internal void RecordLiveFrames(bool enabled){if(enabled&&liveFrames==null)liveFrames=new LiveFrameWindow();liveFrames?.Record(enabled);}
        internal string LiveFramesSummary=>liveFrames?.Summary??"AB_FRAME runs=0 spacing_samples=0 overflow=0";
        IntPtr jitSummaryBuffer;
        string JitMarker=>Path.Combine(data,"jit-attempt.pending");
        string JitRecord=>Path.Combine(data,"jit-attempt.txt");
        internal string CpuSummary=>jitActive?(fastMemory?"ARM JIT / fast memory":"ARM JIT / checked memory"):"IR interpreter";
        void JitStage(string stage) {
            if(!jitAttempt)return;
            DiagnosticFiles.DurableWrite(JitRecord,stage);
            DiagnosticFiles.DurableWrite(JitMarker,stage);
        }
        bool PrepareJitAttempt() {
            if(!requestJit)return false;
            jitAttempt=true;
            try {
                if(File.Exists(JitRecord))File.Copy(JitRecord,Path.Combine(data,"jit-attempt.previous.txt"),true);
                string native=Path.Combine(data,"jit-native.log");
                if(File.Exists(native))File.Copy(native,Path.Combine(data,"jit-native.previous.log"),true);
                File.WriteAllText(native,"");
                JitStage("JIT_ATTEMPT_BEGIN: saved game CPU preference; ARM JIT is the default");
                log("JIT_REQUEST core_sha256="+ExpectedJitHash+"; W^X; existing-block links enabled; memory="+(fastMemory?"fast":"checked")+"; vertex JIT enabled; sampled execution profile");
                log("JIT_CPUINFO "+DiagnosticFiles.ReadTail("/proc/cpuinfo",4096));
                log("JIT_CGROUP "+DiagnosticFiles.ReadTail("/proc/self/cgroup",4096));
                string probeStatus="not started";
                if(!JitMemoryProbe.Run(new JitMemory(),message=>{probeStatus=message;JitStage(message);log(message);})) {
                    JitStage(probeStatus+"; using bundled interpreter");
                    File.Delete(JitMarker);jitAttempt=false;return false;
                }
                JitStage("JIT_PROBE_PASSED: loading separately pinned ARM core");return true;
            }catch(Exception e) {
                log("JIT_PREFLIGHT_ERROR "+e.Message+"; using bundled interpreter");
                try{DiagnosticFiles.DurableWrite(JitRecord,"JIT_PREFLIGHT_ERROR "+e.Message);File.Delete(JitMarker);}catch{}
                jitAttempt=false;return false;
            }
        }
        void StartJitDiagnostics() {
            if(!jitActive)return;
            int error=Function<JitInit>("tizenpsp_jit_init")(Text(Path.Combine(data,"jit-native.log")));
            if(error!=0)throw new Exception("JIT diagnostic file could not open/sync: errno="+error+"; select IR interpreter in game details to retry");
            jitSnapshot=Function<JitSnapshot>("tizenpsp_jit_snapshot");jitSummaryBuffer=Marshal.AllocHGlobal(16384);
            vertexSelect=Function<ExperimentSelect>("tizenpsp_set_command_queue");vertexStatus=Function<ApiVersionCall>("tizenpsp_command_queue_status");
            log("JIT_DIAGNOSTICS_READY; native events survive managed log failure; no process signal handlers installed");
        }
        string JitSummary {
            get {
                if(!jitActive)return "CPU backend=IR_INTERPRETER native_jit=0";
                if(jitSnapshot==null)return "JIT requested; native initialization incomplete";
                uint count=jitSnapshot(jitSummaryBuffer,16384);
                return count<16384?(Marshal.PtrToStringAnsi(jitSummaryBuffer)??"JIT snapshot unavailable"):"JIT snapshot rejected";
            }
        }
        void FinishJitAttempt() {
            if(!jitAttempt)return;
            DiagnosticFiles.DurableWrite(JitRecord,"JIT_CLEAN_CLOSE "+JitSummary);
            File.Delete(JitMarker);jitAttempt=false;
        }
    }
}
