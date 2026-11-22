using System;
using System.Linq;

internal static class HostPerformanceInfo
{
    static string Read(string path)=>DiagnosticFiles.ReadTail(path,8192).Trim();
    static string Field(string text,string name) {
        string line=text.Split('\n').FirstOrDefault(x=>x.StartsWith(name+":",StringComparison.Ordinal));
        return line==null?"unavailable":line.Substring(name.Length+1).Trim().Replace(' ','_').Replace('\t','_');
    }
    static string Frequency(string name,int cpu=0) {
        string value=Read("/sys/devices/system/cpu/cpu"+cpu+"/cpufreq/"+name);
        uint number;return uint.TryParse(value,out number)?number.ToString():"unavailable";
    }
    internal static string Snapshot() {
        string status=Read("/proc/thread-self/status");
        return "HOST_RUNTIME ui_cpus_allowed="+Field(status,"Cpus_allowed_list")+" vmrss="+Field(status,"VmRSS")+
            " threads="+Field(status,"Threads")+" cpu0_current_khz="+Frequency("scaling_cur_freq")+" cpu0_max_khz="+Frequency("scaling_max_freq")+
            " cpu_current_khz="+string.Join(",",Enumerable.Range(0,4).Select(i=>i+":"+Frequency("scaling_cur_freq",i)))+
            " cpu_online="+Read("/sys/devices/system/cpu/online");
    }
    // Resolve the process's cpuset inside its mount namespace (the mount can
    // expose a subtree). Report constraints; never write cgroup files.
    internal static string[] CpuSetDirectories(string cgroup,string mountinfo) {
        var paths=new System.Collections.Generic.List<string>();
        foreach(string groupLine in cgroup.Split('\n')) {
            string[] group=groupLine.Trim().Split(new[]{':'},3);
            if(group.Length!=3 || !(group[1].Split(',').Contains("cpuset") || group[0]=="0"&&group[1]==""))continue;
            bool unified=group[1]=="";
            foreach(string line in mountinfo.Split('\n')) {
                string[] halves=line.Split(new[]{" - "},StringSplitOptions.None);
                if(halves.Length!=2)continue;
                string[] before=halves[0].Split(' '),after=halves[1].Trim().Split(' ');
                if(before.Length<6 || after.Length<3)continue;
                if(unified?after[0]!="cgroup2":after[0]!="cgroup"||!after[2].Split(',').Contains("cpuset"))continue;
                string root=Unescape(before[3]),mount=Unescape(before[4]).TrimEnd('/'),path=group[2];
                if(path==root)paths.Add(mount);
                else if(root=="/" && path.StartsWith("/",StringComparison.Ordinal))paths.Add(mount+path);
                else if(path.StartsWith(root+"/",StringComparison.Ordinal))paths.Add(mount+path.Substring(root.Length));
            }
        }
        return paths.Distinct().ToArray();
    }
    static string Unescape(string path)=>path.Replace("\\040"," ").Replace("\\011","\t").Replace("\\012","\n").Replace("\\134","\\");
    internal static void LogPlacement(Action<string> log) {
        string[] directories=CpuSetDirectories(Read("/proc/self/cgroup"),Read("/proc/self/mountinfo"));
        if(directories.Length==0)log("HOST_CPUSET status=unavailable");
        foreach(string directory in directories)foreach(string name in new[]{"cpuset.cpus","cpuset.effective_cpus","cpuset.cpus.effective"}) {
            string value=Read(directory+"/"+name);
            log("HOST_CPUSET path="+directory+"/"+name+" value="+(value.Length==0?"empty_or_unavailable":value));
        }
    }
}
