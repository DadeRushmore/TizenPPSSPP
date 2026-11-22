using System;
using System.Linq;

internal static class HostPerformanceChecks
{
    static void Need(bool value) {if(!value)throw new Exception("Cpuset mount namespace resolution");}
    internal static void Run() {
        string groups="3:cpuset:/bb-mini/lxc.payload/dotnetzone\n2:cpu,cpuacct:/\n";
        string mount="32 20 0:1 / /sys/fs/cgroup/cpuset rw - cgroup cgroup rw,cpuset\n";
        Need(HostPerformanceInfo.CpuSetDirectories(groups,mount).Single()=="/sys/fs/cgroup/cpuset/bb-mini/lxc.payload/dotnetzone");
        mount="32 20 0:1 /bb-mini/lxc.payload/dotnetzone /sys/fs/cgroup/cpuset rw - cgroup cpuset rw,cpuset\n";
        Need(HostPerformanceInfo.CpuSetDirectories(groups,mount).Single()=="/sys/fs/cgroup/cpuset");
        mount="32 20 0:1 /bb-mini /sys/fs/cgroup/cpuset rw - cgroup cpuset rw,cpuset";
        Need(HostPerformanceInfo.CpuSetDirectories(groups,mount).Single()=="/sys/fs/cgroup/cpuset/lxc.payload/dotnetzone");
        Need(HostPerformanceInfo.CpuSetDirectories("3:cpuset:/bb-mini-other",mount).Length==0);
        Need(HostPerformanceInfo.CpuSetDirectories("0::/app", "32 20 0:1 / /sys/fs/cgroup rw - cgroup2 cgroup rw").Single()=="/sys/fs/cgroup/app");
        Need(HostPerformanceInfo.CpuSetDirectories(groups,"bad").Length==0);
        Need(HostPerformanceInfo.CpuSetDirectories("2:cpu:/",mount).Length==0);
        Need(HostPerformanceInfo.CpuSetDirectories("3:cpuset:/bb-mini","32 20 0:1 /bb-mini /group\\040space rw - cgroup cpuset rw,cpuset").Single()=="/group space");
        Console.WriteLine("PASS: cpuset v1/v2, subtree mounts, controller selection and escaped paths.");
    }
}
