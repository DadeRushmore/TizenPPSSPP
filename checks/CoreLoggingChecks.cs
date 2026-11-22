using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

internal static class CoreLoggingChecks
{
    const string Wait="CORE_BRIDGE CORE_LOG E [sceKernel] UNKNOWN_SEMID=sceKernelWaitSema(-2147352575, 1, 00000000)";
    const string Signal="CORE_BRIDGE CORE_LOG E [sceKernel] UNKNOWN_SEMID=sceKernelSignalSema(-2147352575, 1): bad sema id";
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    internal static void Run() {
        var gate=new CoreLogAggregator();var output=new List<string>();
        for(int i=0;i<10000;i++)gate.Offer(i%2==0?Wait:Signal,1,output.Add);
        Need(output.SequenceEqual(new[]{Wait,Signal}),"Alternating repeated errors were written individually");
        gate.Flush(4,output.Add);
        Need(output.Count==4 && output.Count(x=>x.Contains("total=5000 suppressed=4999"))==2,"Repeat counts lost alternating events");
        Need(gate.Summary.Contains("received=10000 originals_emitted=2 suppressed=9998"),"Cumulative log accounting");
        gate.Offer(Wait,5,output.Add);gate.Offer(Wait,6,output.Add);gate.Offer(Wait,10,output.Add);
        Need(output.Count==7 && output[5].Contains("total=2"),"Timed window rollover failed");
        gate.Flush(10,output.Add);int count=output.Count;gate.Flush(10,output.Add);
        Need(count==output.Count,"Repeated flush duplicated summaries");
        gate=new CoreLogAggregator();output.Clear();
        for(int i=0;i<100;i++)gate.Offer("CORE_BRIDGE CORE_LOG E unique="+i,1,output.Add);
        Need(output.Count==64,"Unique-message flood is not bounded");
        gate.Flush(2,output.Add);
        Need(output.Last().Contains("OVERFLOW events=36") && gate.Summary.Contains("overflow_events=36"),"Capacity loss must be explicit");
        Need(!CoreLogAggregator.IsCore("CORE_BRIDGE CORE_STAGE context_reset") && !CoreLogAggregator.IsCore("FRAME_TIMING runs=1"),"Host stages or metrics would be throttled");

        string dir=Path.Combine(Directory.GetCurrentDirectory(),"build","core-log-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        double now=0;var savedConsole=Console.Out;
        try {
            Console.SetOut(TextWriter.Null);
            var log=new GraphicsSessionLog(dir,()=>now);log.SetContext("app_version=test game_file=Mortal Kombat.iso selected_core=IR_INTERPRETER");
            log.Add("CORE_STARTUP initial loading boundary");
            log.Add("CORE_BRIDGE CORE_LOG I ordinary info");
            log.Add("CORE_BRIDGE CORE_LOG W [sceKernel] sceKernelCreateSema invalid name");
            for(int i=0;i<10000;i++)log.Add((i%2==0?Wait:Signal)+"\n");
            log.FlushCore();
            Need(new FileInfo(Path.Combine(dir,"graphics-test.log")).Length<5000,"Repeated errors still flood the file");
            string first=File.ReadAllText(Path.Combine(dir,"core-first-errors.log"));
            Need(first.Contains("WaitSema") && first.Contains("SignalSema") && !first.Contains("ordinary info") && !first.Contains("invalid name"),"Error evidence lost or confused with INFO/warnings");
            Need(File.ReadAllText(Path.Combine(dir,"core-first-warnings.log")).Contains("invalid name"),"Separate warning evidence lost");
            for(int i=0;i<100;i++)log.Add("CORE_BRIDGE CORE_LOG W variable warning "+i);
            const string probe="CORE_BRIDGE CORE_LOG I [JIT] INV_PROBE source=sceKernelIcacheInvalidateAll guest_pc=08800000 address=00000000 bytes=1073741823 blocks=16 same=1 changed=0 unknown=15";
            log.Add(probe);
            Need(File.ReadAllText(Path.Combine(dir,"graphics-test.log")).Contains(probe),"Bounded invalidation evidence lost during core-log overflow");
            log.Add("CORE_BRIDGE CORE_LOG E initiating failure after warnings");
            first=File.ReadAllText(Path.Combine(dir,"core-first-errors.log"));
            Need(first.Contains("initiating failure after warnings") && log.CoreSummary.Contains("first_warning_samples=32"),"Warnings exhausted error capture capacity");
            for(int i=0;i<1200;i++)log.Add("HOST_DATA "+new string('x',1000));
            Need(!File.ReadAllText(Path.Combine(dir,"graphics-test.log")).Contains("initial loading boundary"),"Test did not force log rotation");
            Need(File.ReadAllText(Path.Combine(dir,"graphics-startup.log")).Contains("initial loading boundary"),"Rotation erased startup evidence");
            Need(File.ReadAllText(Path.Combine(dir,"core-first-errors.log"))==first,"Rotation altered first errors");
            Need(new FileInfo(Path.Combine(dir,"graphics-startup.log")).Length<=GraphicsSessionLog.MaxEvidenceBytes,"Startup evidence exceeds bound");
            string psp=Path.Combine(dir,"PSP");Directory.CreateDirectory(psp);
            File.WriteAllText(Path.Combine(dir,"private-unrelated.txt"),"DO NOT EXPORT");
            string exported=File.ReadAllText(DiagnosticFiles.Export(dir,psp,log.Context+"\n"+log.CoreSummary));
            Need(exported.Contains(probe),"Export lost invalidation probe evidence after rotation");
            Need(exported.Contains("Mortal Kombat.iso") && exported.Contains("invalid name") && exported.Contains("initial loading boundary") && !exported.Contains("DO NOT EXPORT"),"Export lost protected evidence or leaked unrelated file");
            var next=new GraphicsSessionLog(dir,()=>now);next.SetContext("app_version=test game=none");
            Need(File.ReadAllText(Path.Combine(dir,"session-context.previous.txt")).Contains("Mortal Kombat.iso"),"Relaunch lost previous game context");
            Need(File.ReadAllText(Path.Combine(dir,"core-first-errors.previous.log"))==first && !File.Exists(Path.Combine(dir,"core-first-errors.log")),"Relaunch mixed current and previous first errors");
            Parallel.For(0,4000,_=>next.Add(Wait));next.FlushCore();
            Need(next.CoreSummary.Contains("received=4000 originals_emitted=1 suppressed=3999"),"Concurrent callback counting lost events");
            var unavailable=new GraphicsSessionLog(Path.Combine(dir,"missing"),()=>0);unavailable.SetContext("game_file=test.iso");unavailable.Add(Wait);
            Need(unavailable.CoreSummary.Contains("evidence_write_error=") && unavailable.Snapshot(200).Any(x=>x.Contains("UNKNOWN_SEMID")),"Disk failure lost memory diagnostics");
        }finally{Console.SetOut(savedConsole);}
        Console.WriteLine("PASS: alternating core-log flood aggregation, exact counts, timed/forced flush, explicit overflow, concurrent callbacks, preserved first errors/startup/context and USB export after rotation/relaunch.");
    }
}
