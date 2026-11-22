using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

internal static class InputAccessProbe
{
    [DllImport("libc.so.6",SetLastError=true)]static extern int open(string path,int flags);
    [DllImport("libc.so.6",SetLastError=true)]static extern int close(int fd);
    [DllImport("libc.so.6",SetLastError=true)]static extern int ioctl(int fd,uint request,IntPtr data);
    // Linux UAPI EVIOCGID = _IOR('E',2,struct input_id), four unsigned 16-bit fields.
    // Metadata only: no read(), writes, EVIOCGRAB, permission changes or input capture.
    const uint GetId=0x80084502;
    internal static string Run()
    {
        var report=new StringBuilder("CONTROLLER DEVICE ACCESS CHECK\nRead-only metadata; no button events read.\n");
        if(IntPtr.Size!=4 || RuntimeInformation.ProcessArchitecture!=Architecture.Arm)return report+"ARM32 TV required.";
        string[] paths;
        try {paths=Directory.EnumerateFiles("/dev/input","event*").OrderBy(p=>p,StringComparer.Ordinal).Take(64).ToArray();}
        catch(Exception e){report.AppendLine("Cannot enumerate /dev/input: "+e.Message);paths=Array.Empty<string>();}
        int readable=0,denied=0,sony=0;
        IntPtr id=Marshal.AllocHGlobal(8);
        try {
            foreach(string path in paths) {
                int fd=open(path,0x800); // O_RDONLY | O_NONBLOCK on ARM Linux.
                if(fd<0){int error=Marshal.GetLastWin32Error();if(error==13||error==1)denied++;report.AppendLine(Path.GetFileName(path)+": open failed errno="+error);continue;}
                try {
                    for(int i=0;i<8;i++)Marshal.WriteByte(id,i,0);
                    if(ioctl(fd,GetId,id)!=0){report.AppendLine(Path.GetFileName(path)+": opened; EVIOCGID failed errno="+Marshal.GetLastWin32Error());continue;}
                    readable++;
                    ushort bus=unchecked((ushort)Marshal.ReadInt16(id)),vendor=unchecked((ushort)Marshal.ReadInt16(id,2)),product=unchecked((ushort)Marshal.ReadInt16(id,4));
                    if(vendor==0x054c)sony++;
                    report.AppendLine(Path.GetFileName(path)+": readable bus="+bus.ToString("X4")+" vendor="+vendor.ToString("X4")+" product="+product.ToString("X4")+(vendor==0x054c?" (Sony)":""));
                } finally {close(fd);}
            }
        } finally {Marshal.FreeHGlobal(id);}
        report.AppendLine("Nodes="+paths.Length+" identified="+readable+" denied="+denied+" Sony="+sony);
        report.AppendLine(sony>0?"Candidate direct Sony device access exists. Distinct button events are NOT yet verified.":"No accessible Sony node identified. A direct controller reader is NOT established.");
        HidMetadata(report);
        report.AppendLine("This does not change your mappings. Send this report.\nOK/Back returns; Up opens controls.");
        return report.ToString();
    }
    static void HidMetadata(StringBuilder report) {
        // Linux UAPI HIDIOCGRAWINFO: u32 bus, s16 vendor, s16 product, 8 bytes.
        const uint GetRawInfo=0x80084803;
        report.AppendLine("HIDRAW METADATA (no reports read, no feature/output writes)");
        string[] paths;
        try{paths=Directory.EnumerateFiles("/dev","hidraw*").OrderBy(p=>p,StringComparer.Ordinal).Take(64).ToArray();}
        catch(Exception e){report.AppendLine("Cannot enumerate hidraw: "+e.Message);return;}
        int sony=0;IntPtr info=Marshal.AllocHGlobal(8);
        try {
            foreach(var path in paths) {
                int fd=open(path,0x800);
                if(fd<0){report.AppendLine(Path.GetFileName(path)+": open failed errno="+Marshal.GetLastWin32Error());continue;}
                try {
                    for(int i=0;i<8;i++)Marshal.WriteByte(info,i,0);
                    if(ioctl(fd,GetRawInfo,info)!=0){report.AppendLine(Path.GetFileName(path)+": HIDIOCGRAWINFO failed errno="+Marshal.GetLastWin32Error());continue;}
                    ushort vendor=unchecked((ushort)Marshal.ReadInt16(info,4)),product=unchecked((ushort)Marshal.ReadInt16(info,6));
                    if(vendor==0x054c)sony++;
                    report.AppendLine(Path.GetFileName(path)+": readable bus="+Marshal.ReadInt32(info)+" vendor="+vendor.ToString("X4")+" product="+product.ToString("X4"));
                }finally{close(fd);}
            }
        }finally{Marshal.FreeHGlobal(info);}
        report.AppendLine("Hidraw nodes="+paths.Length+" accessible Sony="+sony+". Distinct HID button reports not yet verified.");
    }
}
