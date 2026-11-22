using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;

internal static class ThreadCpuInfo
{
    static readonly object roleLock=new object();
    static readonly Dictionary<string,string> roles=new Dictionary<string,string>(StringComparer.Ordinal);
    internal static void RegisterCurrent(string role) {
        string stat=null;
        try {stat=File.ReadAllText("/proc/thread-self/stat");} catch { }
        RecordRole(role,stat);
    }
    internal static void RecordRole(string role,string stat) {
        string entry=Parse(stat);
        lock(roleLock)roles[role]=entry==null?"unavailable":entry.Substring(0,entry.IndexOf(':'));
    }
    internal static string Roles {get {lock(roleLock)return string.Join(",",roles.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>x.Key+":"+x.Value));}}

    // stat's comm can contain spaces and ')'; fields begin after its last ')'.
    internal static string Parse(string text) {
        if(text==null)return null;
        int left=text.IndexOf('('),right=text.LastIndexOf(')');
        uint tid;ulong user,system,start;
        if(left<1 || right<=left || !uint.TryParse(text.Substring(0,left).Trim(),out tid))return null;
        string[] fields=text.Substring(right+1).Split((char[])null,StringSplitOptions.RemoveEmptyEntries);
        if(fields.Length<20 || !Number(fields[11],out user) || !Number(fields[12],out system) || !Number(fields[19],out start))return null;
        return tid+":"+start+":"+user+":"+system+":"+Uri.EscapeDataString(text.Substring(left+1,right-left-1));
    }
    static bool Number(string text,out ulong value)=>ulong.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out value);
    internal static string Snapshot() {
        var entries=new List<string>();int errors=0;
        try {
            foreach(string path in Directory.GetDirectories("/proc/self/task")) {
                try {
                    string entry=Parse(File.ReadAllText(Path.Combine(path,"stat")));
                    if(entry==null)++errors;else entries.Add(entry);
                } catch { ++errors; } // Threads may exit between enumeration and read.
            }
        } catch { ++errors; }
        return "HOST_THREAD_CPU format=tid:start:user:system:name units=clock_ticks roles="+Roles+" errors="+errors+" tasks="+string.Join(",",entries);
    }
}
