using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Diagnostics;

// UI-thread owned; no platform dependencies, so diagnostics can be tested on PC.
internal sealed class GraphicsCounters
{
    internal string Backend="egl-pbuffer-readback-diagnostics";
    internal int Attempts, FramesRendered, FramesPresented, RenderingErrors, PresentationErrors;
    internal int ContextCreationFailures, DiagnosticErrors, CompletedCalls;
    internal double SuccessfulCallMs;
    internal void Observe(string message)
    {
        if(message=="FRAME_RENDERED") FramesRendered++;
        else if(message=="CONTEXT_CREATE_FAILED") ContextCreationFailures++;
        else if(message=="DIAGNOSTIC_ERROR") DiagnosticErrors++;
    }
    internal void Complete(bool success,double elapsedMs)
    {
        Attempts++;
        if(success) { CompletedCalls++; SuccessfulCallMs+=elapsedMs; }
        else RenderingErrors++;
    }
    internal string Summary => "backend="+Backend+" attempts="+Attempts+" frames_rendered="+FramesRendered+
        " frames_submitted="+FramesPresented+" rendering_errors="+RenderingErrors+
        " presentation_errors="+PresentationErrors+" context_creation_failures="+ContextCreationFailures+
        " diagnostic_errors="+DiagnosticErrors+" average_frame_call_ms="+
        (CompletedCalls==0?"n/a":(SuccessfulCallMs/CompletedCalls).ToString("F2",CultureInfo.InvariantCulture));
}

internal sealed class GraphicsSessionLog
{
    internal const int MaxBytes=256*1024;
    readonly object sync=new object();
    readonly List<string> lines=new List<string>();
    readonly string path;
    readonly string directory;
    readonly Func<double> now;
    readonly CoreLogAggregator core=new CoreLogAggregator();
    readonly HashSet<string> firstErrors=new HashSet<string>(StringComparer.Ordinal);
    readonly HashSet<string> firstWarnings=new HashSet<string>(StringComparer.Ordinal);
    internal const int MaxEvidenceBytes=64*1024;
    int startupBytes,errorBytes,warningBytes;
    bool startupFull,errorFull,warningFull;
    string evidenceError,context="Session context not set";
    internal readonly string SessionId=Guid.NewGuid().ToString("N");
    int bytes;
    int rotations;
    internal string WriteError { get; private set; }
    internal volatile Action<string> LiveLine;
    internal GraphicsSessionLog(string directory,Func<double> clock=null)
    {
        this.directory=directory;now=clock??(()=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency);
        path=Path.Combine(directory,"graphics-test.log");
        foreach(string name in new[]{"session-context.txt","graphics-startup.log","core-first-errors.log","core-first-warnings.log"}) {
            try {
                string current=Path.Combine(directory,name),previous=Path.Combine(directory,Path.GetFileNameWithoutExtension(name)+".previous"+Path.GetExtension(name));
                if(File.Exists(current)){File.Copy(current,previous,true);File.Delete(current);}else if(File.Exists(previous))File.Delete(previous);
            }catch(Exception e){evidenceError=e.Message;}
        }
        try {
            if(File.Exists(path)) File.Copy(path,Path.Combine(directory,"graphics-test.previous.log"),true);
            for(int i=1;i<=2;i++) {
                string part=Path.Combine(directory,"graphics-test.part"+i+".log"),previous=Path.Combine(directory,"graphics-test.previous.part"+i+".log");
                if(File.Exists(part)){File.Copy(part,previous,true);File.Delete(part);}else if(File.Exists(previous))File.Delete(previous);
            }
            File.WriteAllText(path,"");
        } catch(Exception e) { WriteError=e.Message; }
    }
    internal void Add(string message)
    {
        lock(sync) {
        message=(message??"(null)").TrimEnd('\r','\n');
        if(message.Length>16384)message=message.Substring(0,16384)+" [message truncated]";
        if(CoreLogAggregator.IsCore(message)) {
            if(CoreLogAggregator.IsError(message) && !errorFull && firstErrors.Count<CoreLogAggregator.Capacity && firstErrors.Add(message))
                Evidence("core-first-errors.log",Stamp(message),ref errorBytes,ref errorFull);
            else if(!CoreLogAggregator.IsError(message) && CoreLogAggregator.IsWarningOrError(message) && !warningFull && firstWarnings.Count<32 && firstWarnings.Add(message))
                Evidence("core-first-warnings.log",Stamp(message),ref warningBytes,ref warningFull);
            core.Offer(message,now(),WriteLine);return;
        }
        WriteLine(message);
        }
    }
    static string Stamp(string message)=>DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture)+" "+message;
    void Evidence(string name,string line,ref int count,ref bool full) {
        if(full)return;
        int size=Encoding.UTF8.GetByteCount(line+Environment.NewLine);
        if(count+size>MaxEvidenceBytes-128){line="EVIDENCE_LIMIT reached; later text remains subject to normal log aggregation/rotation";full=true;size=Encoding.UTF8.GetByteCount(line+Environment.NewLine);}
        count+=size;
        try{File.AppendAllText(Path.Combine(directory,name),line+Environment.NewLine);}catch(Exception e){evidenceError=e.Message;full=true;}
    }
    void WriteLine(string message) {
        string line=Stamp(message);
        Evidence("graphics-startup.log",line,ref startupBytes,ref startupFull);
        int size=Encoding.UTF8.GetByteCount(line+Environment.NewLine);
        if(bytes+size>MaxBytes-256) {
            rotations++;lines.Clear();bytes=0;
            string notice="LOG_ROTATED part="+rotations+"; older chunks retained on disk";
            lines.Add(notice);bytes=Encoding.UTF8.GetByteCount(notice+Environment.NewLine);
            try {
                if(WriteError==null) {
                    string directory=Path.GetDirectoryName(path),part1=Path.Combine(directory,"graphics-test.part1.log");
                    if(File.Exists(part1))File.Copy(part1,Path.Combine(directory,"graphics-test.part2.log"),true);
                    if(File.Exists(path))File.Copy(path,part1,true);
                    File.WriteAllText(path,notice+Environment.NewLine);
                }
            }catch(Exception e){WriteError=e.Message;}
        }
        bytes+=size; lines.Add(line);
        try { if(WriteError==null) File.AppendAllText(path,line+Environment.NewLine); }
        catch(Exception e) { WriteError=e.Message; }
        LiveLine?.Invoke(line); // Enqueue only; never perform network I/O under this lock.
        foreach(string chunk in Wrap(new[]{line},400)) Console.WriteLine("TizenPSP: "+chunk);
    }
    internal void SetContext(string value) {
        lock(sync) {
            context="SESSION id="+SessionId+" "+value;
            try{DiagnosticFiles.DurableWrite(Path.Combine(directory,"session-context.txt"),context);}catch(Exception e){evidenceError=e.Message;}
            WriteLine(context);
        }
    }
    internal void FlushCore(){lock(sync)core.Flush(now(),WriteLine);}
    internal string Context {get{lock(sync)return context;}}
    internal string CoreSummary {get{lock(sync)return core.Summary+" first_error_samples="+firstErrors.Count+" first_error_capture_full="+(errorFull||firstErrors.Count>=CoreLogAggregator.Capacity)+" first_warning_samples="+firstWarnings.Count+" first_warning_capture_full="+(warningFull||firstWarnings.Count>=32)+(evidenceError==null?"":" evidence_write_error="+evidenceError);}}
    internal string[] Snapshot(int columns) {lock(sync){core.Flush(now(),WriteLine);return Wrap(lines,columns).ToArray();}}
    internal static List<string> Wrap(IEnumerable<string> source,int columns)
    {
        if(columns<1) throw new ArgumentOutOfRangeException(nameof(columns));
        var result=new List<string>();
        foreach(string text in source) foreach(string line in text.Replace("\r","").Split('\n')) {
            if(line.Length==0) result.Add("");
            for(int i=0;i<line.Length;i+=columns) result.Add(line.Substring(i,Math.Min(columns,line.Length-i)));
        }
        return result;
    }
}
