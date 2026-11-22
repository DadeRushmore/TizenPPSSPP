using System;
using System.IO;
using System.Linq;
internal static class DiagnosticsChecks
{
    static void Need(bool value,string message) { if(!value) throw new Exception(message); }
    internal static void Run()
    {
        var c=new GraphicsCounters();
        Need(c.Summary.Contains("average_frame_call_ms=n/a"),"No frames must not imply zero latency");
        c.Observe("FRAME_RENDERED"); c.Complete(true,20);
        c.FramesPresented++;
        c.Observe("CONTEXT_CREATE_FAILED"); c.Complete(false,90);
        c.Observe("DIAGNOSTIC_ERROR"); c.Complete(true,40);
        c.PresentationErrors++;
        Need(c.FramesPresented==1 && c.PresentationErrors==1,"Presentation counts must be independent from rendering");
        Need(c.FramesRendered==1 && c.ContextCreationFailures==1 && c.RenderingErrors==1 && c.DiagnosticErrors==1,"Error counters conflated");
        Need(c.Summary.Contains("average_frame_call_ms=30.00"),"Failed attempts must not skew successful-call average");
        var wrapped=GraphicsSessionLog.Wrap(new[]{"ABCDEFGHI","","one\ntwo"},4);
        Need(string.Join("|",wrapped)=="ABCD|EFGH|I||one|two","Long extension pagination loses content");
        string dir=Path.Combine(Directory.GetCurrentDirectory(),"build","log-check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var console=Console.Out;
        try {
            Console.SetOut(TextWriter.Null);
            var log=new GraphicsSessionLog(dir); log.Add("first launch shutdown");
            var next=new GraphicsSessionLog(dir); next.Add("second launch");
            string forwarded=null;next.LiveLine=line=>forwarded=line;
            next.Add("live forwarding fixture");Need(forwarded!=null&&forwarded.EndsWith("live forwarding fixture"),"Live stream lost the stamped log line");
            next.LiveLine=null;next.Add("local only fixture");Need(forwarded.EndsWith("live forwarding fixture"),"Disabled live sink still receives logs");
            Need(File.ReadAllText(Path.Combine(dir,"graphics-test.previous.log")).Contains("first launch shutdown"),"Relaunch lost previous log");
            Need(!File.ReadAllText(Path.Combine(dir,"graphics-test.log")).Contains("first launch shutdown"),"Current/previous logs mixed");
            for(int i=0;i<400;i++) next.Add(new string('x',1000));
            string current=File.ReadAllText(Path.Combine(dir,"graphics-test.log"));
            Need(new FileInfo(Path.Combine(dir,"graphics-test.log")).Length<=GraphicsSessionLog.MaxBytes,"Log cap exceeded");
            Need(current.Contains("LOG_ROTATED") && File.Exists(Path.Combine(dir,"graphics-test.part1.log")),"Log rollover missing");
            next.Add("late failure still recorded");
            Need(File.ReadAllText(Path.Combine(dir,"graphics-test.log")).Contains("late failure still recorded"),"Logging stopped after reaching its first cap");
            var unavailable=new GraphicsSessionLog(Path.Combine(dir,"missing"));
            unavailable.Add("memory report survives disk failure");
            Need(unavailable.WriteError!=null && unavailable.Snapshot(100).Any(s=>s.Contains("memory report")),"File failure lost on-screen diagnostics");
        } finally { Console.SetOut(console); }
        Console.WriteLine("PASS: counters, timing semantics, log pagination, session/chunk rotation, bounded continued logging and file-failure report.");
    }
}
