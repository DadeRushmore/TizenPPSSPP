using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
internal static partial class NativeGraphics
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate IntPtr VersionLookup(IntPtr handle,[MarshalAs(UnmanagedType.LPStr)]string name,[MarshalAs(UnmanagedType.LPStr)]string version);
    internal static string CheckCoreRuntime(string resources,Action<string> log)
    {
        if(IntPtr.Size!=4||RuntimeInformation.ProcessArchitecture!=Architecture.Arm)throw new Exception("ARM32 TV required");
        string path=Path.Combine(resources,"core-runtime-imports.tsv");
        if(Hash(File.ReadAllBytes(path))!=File.ReadAllText(Path.Combine(resources,"core-runtime-imports.sha256")).Trim())throw new Exception("Core import manifest checksum mismatch");
        var handles=new Dictionary<string,IntPtr>();
        try {
            foreach(string library in new[]{"libc.so.6","libm.so.6","libpthread.so.0","libdl.so.2","librt.so.1","libEGL.so.1","libGLESv2.so.2"}) {
                var h=dlopen(library,2);handles.Add(library,h);
                log("CORE_LIBRARY "+library+" "+(h!=IntPtr.Zero?"available":"MISSING"));
            }
            var proc=handles["libdl.so.2"]==IntPtr.Zero?IntPtr.Zero:dlsym(handles["libdl.so.2"],"dlvsym");
            if(proc==IntPtr.Zero)throw new Exception("dlvsym unavailable; cannot verify required symbol versions");
            var versionLookup=Marshal.GetDelegateForFunctionPointer<VersionLookup>(proc);
            string[] lines=File.ReadAllLines(path);
            foreach(string line in lines)if(line.StartsWith("#"))log(line);
            string result=CoreImportCheck.Run(lines,(name,version,library)=> {
                if(version.Length!=0) {
                    IntPtr handle;
                    return handles.TryGetValue(library,out handle)&&handle!=IntPtr.Zero&&versionLookup(handle,name,version)!=IntPtr.Zero;
                }
                foreach(var handle in handles.Values)if(handle!=IntPtr.Zero&&dlsym(handle,name)!=IntPtr.Zero)return true;
                return false;
            },log);
            GC.KeepAlive(versionLookup);
            return result;
        } finally {foreach(var h in handles.Values)if(h!=IntPtr.Zero)dlclose(h);}
    }
}
