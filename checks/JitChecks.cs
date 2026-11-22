using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
internal static class JitChecks
{
    sealed class Memory : IJitMemory
    {
        internal int Page=4096,Permissions=3,Released,Executions,Protects,FailProtect;
        internal bool FailAllocate,FailFlush,BadResult;
        uint instruction;
        public int PageSize=>Page;
        public int Error=>13;
        public IntPtr Allocate(int bytes)=>FailAllocate?new IntPtr(-1):new IntPtr(4096);
        public bool Protect(IntPtr pointer,int bytes,int flags) {
            if(flags!=3 && flags!=5)throw new Exception("Probe requested RWX");
            if(++Protects==FailProtect)return false;Permissions=flags;return true;
        }
        public bool Flush(IntPtr pointer,int bytes)=>!FailFlush;
        public void Write(IntPtr pointer,uint word) {
            if(Permissions!=3)throw new Exception("Writing executable page");
            if(pointer.ToInt64()==4096)instruction=word;else if(word!=0xe12fff1e)throw new Exception("Invalid ARM return");
        }
        public int Execute(IntPtr pointer) {if(Permissions!=5)throw new Exception("Executing writable page");Executions++;return BadResult?0:(int)(instruction&255);}
        public void Release(IntPtr pointer,int bytes){Released++;}
    }
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    internal static void Run() {
        var phases=new List<string>();var memory=new Memory();
        Need(JitMemoryProbe.Run(memory,phases.Add) && memory.Executions==2 && memory.Released==1,"Probe did not verify cache rewrite and release");
        Need(phases.Any(x=>x.Contains("execute_return result=43")),"Probe execution evidence missing");
        foreach(int fail in new[]{1,2,3}) {
            memory=new Memory{FailProtect=fail};phases.Clear();
            Need(!JitMemoryProbe.Run(memory,phases.Add) && memory.Released==1,"Denied transition must stop and release");
            Need(memory.Executions==(fail==1?0:1) && phases.Any(x=>x.Contains("errno=13")),"Execution continued after protection denial");
        }
        memory=new Memory{FailFlush=true};Need(!JitMemoryProbe.Run(memory,_=>{}) && memory.Executions==0 && memory.Released==1,"Cache failure executed code");
        memory=new Memory{FailAllocate=true};Need(!JitMemoryProbe.Run(memory,_=>{}) && memory.Released==0,"Allocation failure cleanup");
        memory=new Memory{BadResult=true};Need(!JitMemoryProbe.Run(memory,_=>{}) && memory.Executions==1 && memory.Released==1,"Wrong return ignored");
        memory=new Memory{Page=16384};Need(!JitMemoryProbe.Run(memory,_=>{}) && memory.Executions==0,"Unsupported page size allowed");

        string directory=Path.Combine(Directory.GetCurrentDirectory(),"build","jit-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        string psp=Path.Combine(directory,"PSP");Directory.CreateDirectory(psp);
        DiagnosticFiles.DurableWrite(Path.Combine(directory,"jit-attempt.pending"),"execute_begin");
        File.WriteAllText(Path.Combine(directory,"jit-native.log"),"dispatcher_ready_rx\nblock_compiled\ndispatch_return\n");
        File.WriteAllText(Path.Combine(directory,"unrelated.txt"),"DO NOT EXPORT");
        File.WriteAllText(Path.Combine(directory,"button-identity-check.txt"),"BUTTON_IDENTITY_CHECK retained scan evidence");
        File.WriteAllText(Path.Combine(directory,"psp-controller-sticks.txt"),"TizenPSP-sticks-v1 calibration evidence");
        string export=DiagnosticFiles.Export(directory,psp,"JIT_NATIVE compiled_blocks=1");
        string text=File.ReadAllText(export);
        Need(text.Contains("execute_begin") && text.Contains("dispatch_return") && !text.Contains("DO NOT EXPORT"),"Diagnostic export whitelist/recovery evidence");
        Need(text.Contains("retained scan evidence")&&text.Contains("calibration evidence"),"Button check/calibration missing from USB report");
        Need(DiagnosticFiles.ReadTail(Path.Combine(directory,"jit-native.log"),18).Contains("dispatch_return"),"Tail discarded final evidence");
        Console.WriteLine("PASS: W^X probe sequencing, rewrite results, denial/failure gates, cleanup, durable recovery record and bounded USB export.");
    }
}
