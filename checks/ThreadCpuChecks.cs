using System;
using System.Linq;
internal static class ThreadCpuChecks
{
    internal static void Run() {
        string[] fields=Enumerable.Repeat("0",40).ToArray();fields[0]="R";
        fields[11]="1234";fields[12]="56";fields[19]="9876543210";
        string stat="42 (a ) worker) "+string.Join(" ",fields);
        if(ThreadCpuInfo.Parse(stat)!="42:9876543210:1234:56:a%20%29%20worker")throw new Exception("thread stat fields/name");
        ThreadCpuInfo.RecordRole("controller",stat);
        if(!ThreadCpuInfo.Roles.Contains("controller:42"))throw new Exception("worker role ID");
        ThreadCpuInfo.RecordRole("hid",null);
        if(!ThreadCpuInfo.Roles.Contains("hid:unavailable"))throw new Exception("unavailable worker role");
        ThreadCpuInfo.RecordRole("controller","malformed");
        if(ThreadCpuInfo.Roles.Contains("controller:42") || !ThreadCpuInfo.Roles.Contains("controller:unavailable"))throw new Exception("stale worker role ID");
        fields[12]="-1";
        if(ThreadCpuInfo.Parse("42 (worker) "+string.Join(" ",fields))!=null)throw new Exception("negative task time");
        if(ThreadCpuInfo.Parse(null)!=null || ThreadCpuInfo.Parse("42 (short) R 0")!=null)throw new Exception("truncated task stat");
        Console.WriteLine("PASS: thread CPU fields, parentheses, 64-bit start identity and malformed input.");
    }
}
