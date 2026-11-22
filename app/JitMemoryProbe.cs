using System;

internal interface IJitMemory
{
    int PageSize { get; }
    int Error { get; }
    IntPtr Allocate(int bytes);
    bool Protect(IntPtr pointer,int bytes,int flags);
    bool Flush(IntPtr pointer,int bytes);
    void Write(IntPtr pointer,uint instruction);
    int Execute(IntPtr pointer);
    void Release(IntPtr pointer,int bytes);
}

internal static class JitMemoryProbe
{
    // ARM-state MOV r0, #result; BX lr. No PSP/game data is executed here.
    internal static bool Run(IJitMemory memory,Action<string> log) {
        int page=memory.PageSize;log("JIT_PROBE page_size="+page);
        if(page!=4096){log("JIT_PROBE_BLOCKED: ARM emitter currently aligns code to 4096-byte pages");return false;}
        log("JIT_PROBE mmap_rw_begin");IntPtr pointer=memory.Allocate(page);
        if(pointer==new IntPtr(-1) || pointer==IntPtr.Zero){log("JIT_PROBE_BLOCKED mmap_rw errno="+memory.Error);return false;}
        try {
            for(int pass=0;pass<2;pass++) {
                if(pass>0) {
                    log("JIT_PROBE rx_to_rw_begin");
                    if(!memory.Protect(pointer,page,3)){log("JIT_PROBE_BLOCKED rx_to_rw errno="+memory.Error);return false;}
                }
                memory.Write(pointer,0xe3a0002au+(uint)pass);memory.Write(IntPtr.Add(pointer,4),0xe12fff1e);
                log("JIT_PROBE cacheflush pass="+pass);
                if(!memory.Flush(pointer,8)){log("JIT_PROBE_BLOCKED cacheflush errno="+memory.Error);return false;}
                log("JIT_PROBE rw_to_rx_begin pass="+pass);
                if(!memory.Protect(pointer,page,5)){log("JIT_PROBE_BLOCKED rw_to_rx errno="+memory.Error);return false;}
                log("JIT_PROBE execute_begin expected="+(42+pass));
                int result=memory.Execute(pointer);log("JIT_PROBE execute_return result="+result);
                if(result!=42+pass){log("JIT_PROBE_BLOCKED unexpected result");return false;}
            }
            log("JIT_PROBE_PASS: RW/RX transitions, code execution and instruction-cache rewrite observed");return true;
        } finally {memory.Release(pointer,page);}
    }
}
