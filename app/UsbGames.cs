using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;

internal static class UsbGames
{
    internal static string UnescapeMount(string path) => path.Replace("\\040"," ").Replace("\\011","\t").Replace("\\134","\\");
    internal static bool Supported(string path) => new[]{".iso",".cso",".pbp",".elf"}.Contains(Path.GetExtension(path).ToLowerInvariant());
    internal static string[] Find(Action<string> log)
    {
        var scan=Stopwatch.StartNew();int probeErrors=0;
        var roots=new HashSet<string>(StringComparer.Ordinal);
        // Mount table is evidence of the device path; Windows drive letters never enter this path.
        try {
            foreach(var line in File.ReadLines("/proc/mounts")) {
                var fields=line.Split(' ');
                if(fields.Length<3)continue;
                string mount=UnescapeMount(fields[1]);
                if(mount.StartsWith("/dtv/usb/") || mount.StartsWith("/opt/storage/") ||
                   mount.StartsWith("/media/") || mount.StartsWith("/run/media/") || mount.StartsWith("/mnt/")) roots.Add(mount);
            }
        } catch(Exception e) {log("USB mount table: "+e.Message);}
        // Some TV firmware hides mount entries; inspect bounded known USB containers only.
        foreach(string container in new[]{"/dtv/usb","/opt/storage/usb","/media","/run/media","/mnt"}) {
            if(!Directory.Exists(container))continue;
            roots.Add(container);
            try {foreach(var child in Directory.EnumerateDirectories(container).Take(64)) roots.Add(child);}
            catch(Exception e) {log("USB enumeration "+container+": "+e.Message);}
        }
        var games=new SortedSet<string>(StringComparer.Ordinal);
        foreach(var root in roots.OrderBy(x=>x)) {
            string folder=Path.Combine(root,"PSP");
            log("USB_PROBE "+folder);
            var probe=Stopwatch.StartNew();
            try {
                // Enumerate rather than silently swallowing access denial through Directory.Exists.
                int count=0;
                foreach(var path in Directory.EnumerateFiles(folder).Take(1024)) if(Supported(path)) {games.Add(path);count++;}
                log("USB_PSP "+folder+" supported_files="+count+" elapsed_ms="+probe.ElapsedMilliseconds);
            } catch(DirectoryNotFoundException) { }
            catch(Exception e) {probeErrors++;log("USB_PSP "+folder+": "+e.Message+" type="+e.GetType().Name+" elapsed_ms="+probe.ElapsedMilliseconds);}
        }
        log("USB_SCAN roots="+roots.Count+" games="+games.Count+" elapsed_ms="+scan.ElapsedMilliseconds+" probe_errors="+probeErrors+
            "; library discovery only, not gameplay polling; top-level PSP folder; archives excluded");
        return games.ToArray();
    }
}
