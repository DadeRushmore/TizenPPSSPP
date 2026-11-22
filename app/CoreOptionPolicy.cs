// Launch-time overrides only. Unknown options retain the core's declared default.
internal static class CoreOptionPolicy
{
    internal static bool TryOverride(string option,bool jitActive,bool fastMemory,int resolutionScale,int frameSkip,bool skipBufferEffects,out string value) {
        value=null;
        if(option=="ppsspp_cpu_core")value=jitActive?"JIT":"IR JIT";
        else if(jitActive && option=="ppsspp_fast_memory")value=fastMemory?"enabled":"disabled";
        // ARM checked-address failures redispatch the same PC. Stop the guest
        // instead of ignoring the fault and retrying the same instruction forever.
        else if(jitActive && !fastMemory && option=="ppsspp_ignore_bad_memory_access")value="disabled";
        else if(option=="ppsspp_backend")value="opengl";
        else if(option=="ppsspp_internal_resolution")value=resolutionScale==2?"960x544":"480x272";
        else if(option=="ppsspp_inflight_frames")value=jitActive?"Up to 1":"No buffer";
        else if(option=="ppsspp_software_rendering" || option=="ppsspp_cropto16x9" || option=="ppsspp_auto_frameskip" || option=="ppsspp_detect_vsync_swap_interval")value="disabled";
        else if(option=="ppsspp_frameskip")value=frameSkip==1?"1":"disabled";
        else if(option=="ppsspp_frameskiptype")value="Number of frames";
        else if((jitActive || frameSkip!=0) && option=="ppsspp_frame_duplication")value="disabled";
        // The preserved interpreter artifact predates the libretro backbuffer-pass fix.
        else if(option=="ppsspp_skip_buffer_effects")value=jitActive&&skipBufferEffects?"enabled":"disabled";
        return value!=null;
    }
}
