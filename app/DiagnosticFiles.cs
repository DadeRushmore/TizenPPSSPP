using System;
using System.IO;
using System.Text;

internal static class DiagnosticFiles
{
    internal static string ReadTail(string path,int maxBytes=32768) {
        try {
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) {
                long start=Math.Max(0,stream.Length-maxBytes);stream.Position=start;
                using(var reader=new StreamReader(stream,Encoding.UTF8)) {
                    if(start>0)reader.ReadLine();
                    return reader.ReadToEnd();
                }
            }
        }catch(FileNotFoundException){return "";}catch(DirectoryNotFoundException){return "";}
        catch(Exception e){return "Log unavailable: "+e.Message;}
    }
    internal static void DurableWrite(string path,string value) {
        byte[] bytes=Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O")+" "+value+Environment.NewLine);
        using(var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.Read)) {
            stream.Write(bytes,0,bytes.Length);stream.Flush(true);
        }
    }
    internal static string Export(string data,string pspFolder,string currentMetrics) {
        ReportExportSummary summary;
        return Export(data,pspFolder,currentMetrics,out summary);
    }
    internal static string Export(string data,string pspFolder,string currentMetrics,out ReportExportSummary summary) {
        // Capture the entire report before touching removable storage.
        byte[] report=ComposeReport(data,currentMetrics);
        return new ReportExportStore().Export(data,pspFolder,report,out summary);
    }
    internal static byte[] ComposeReport(string data,string currentMetrics) {
        var contents=new StringBuilder();
        using(var writer=new StringWriter(contents)) {
            writer.WriteLine("TizenPSP diagnostics UTC "+DateTime.UtcNow.ToString("O"));writer.WriteLine(currentMetrics);
            writer.WriteLine("Historical files below can describe earlier sessions. Match session IDs and timestamps before comparing CPU modes.");
            foreach(string name in new[]{"session-context.txt","graphics-startup.log","core-first-errors.log","core-first-warnings.log",
                "session-context.previous.txt","graphics-startup.previous.log","core-first-errors.previous.log","core-first-warnings.previous.log",
                "psp-controller-map.txt","psp-controller-sticks.txt","dualsense-faces.txt","button-identity-check.txt","library-v1.tsv","jit-fast-memory-v1.txt","performance-overlay.txt",
                "jit-attempt.pending","jit-attempt.txt","jit-attempt.previous.txt","jit-native.log","jit-native.previous.log",
                "graphics-test.part2.log","graphics-test.part1.log","graphics-test.log","graphics-test.previous.part2.log","graphics-test.previous.part1.log","graphics-test.previous.log"}) {
                string file=Path.Combine(data,name);if(!File.Exists(file))continue;
                writer.WriteLine("\n--- "+name+" (bounded tail) ---");writer.WriteLine(ReadTail(file,256*1024));
            }
        }
        return Encoding.UTF8.GetBytes(contents.ToString());
    }
}
