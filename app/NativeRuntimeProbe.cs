using System;

internal static partial class NativeGraphics
{
    static bool runtimeProbeDone;
    // First feasibility gate only: loader exports do not prove a usable GPU or surface.
    static void ProbeRuntime(Action<string> log) {
        if(runtimeProbeDone)return;
        runtimeProbeDone=true;
        try {
            foreach(string library in new[]{"libvulkan.so.1","libvulkan.so"}) {
                IntPtr handle=dlopen(library,2);
                if(handle==IntPtr.Zero) {log("RUNTIME_VULKAN library="+library+" loadable=0");continue;}
                try {
                    log("RUNTIME_VULKAN library="+library+" loadable=1 get_instance_proc_addr="+
                        (dlsym(handle,"vkGetInstanceProcAddr")!=IntPtr.Zero?"1":"0")+
                        " create_instance="+(dlsym(handle,"vkCreateInstance")!=IntPtr.Zero?"1":"0")+
                        " physical_device_and_presentation=untested");
                } finally {dlclose(handle);}
                break;
            }
        } catch(Exception e) {log("RUNTIME_VULKAN unavailable="+e.GetType().Name);}
    }
}
