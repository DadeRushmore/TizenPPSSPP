using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;

internal static partial class NativeGraphics
{
    internal static CoreStartup StartPpssppGame(string resources,string data,string path,IntPtr parent,Action<string> log,int audioBufferMs=120,bool attemptArmJit=true,int resolutionScale=1,int frameSkip=0,bool fastMemory=false,bool skipBufferEffects=false)
    {
        if(!LibraryCatalog.ValidResolution(resolutionScale)||!LibraryCatalog.ValidFrameSkip(frameSkip))throw new ArgumentOutOfRangeException("Invalid game graphics settings");
        if(coreStartup!=null)throw new InvalidOperationException("One core session per launch; exit and reopen to change games");
        coreStartup=new CoreStartup(resources,data,log);
        coreStartup.gamePath=path;coreStartup.parent=parent;
        coreStartup.audioBufferMs=audioBufferMs;
        coreStartup.requestJit=attemptArmJit;
        coreStartup.fastMemory=attemptArmJit&&fastMemory;
        coreStartup.resolutionScale=resolutionScale;coreStartup.frameSkip=frameSkip;coreStartup.skipBufferEffects=skipBufferEffects;
        coreStartup.Run();
        return coreStartup;
    }
    internal sealed partial class CoreStartup
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate byte BeginCall(IntPtr run,IntPtr unload,IntPtr env,IntPtr ui,IntPtr log);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate byte BoolCall();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate byte LoadCall(IntPtr info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint RateCall(IntPtr getInfo);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr ViewCall(IntPtr parent,uint w,uint h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void BoolArg(byte value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void UintArg(uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void StickArgs(int lx,int ly,int rx,int ry);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int AxisCall(uint axis);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate UIntPtr AudioBatch(IntPtr samples,UIntPtr frames);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void HostServices(IntPtr audio,IntPtr observer);
        internal string gamePath;
        internal IntPtr parent;
        internal IntPtr View { get; private set; }
        internal double FrameSeconds { get; private set; }=1.001/60;
        IntPtr bridgeMemory;
        DynamicElf bridgePlan;
        BoolCall ui;
        Report bridgeReport;
        VoidCall tick,close,resetInput;
        BoolArg pause;
        UintArg buttons;
        StickArgs sticks;
        readonly CoreInputState inputState=new CoreInputState();
        AxisCall inputAxis;
        ApiVersionCall frames,failed,fbo,inputFrame,inputLast,inputPolls,duplicates,tickRequests,coalesced,unscheduled;
        ApiVersionCall guestMillis,actualFlips,foldedFlips,nativeSuppressed;
        AudioBatch audioCallback;
        UintArg frameObserver;
        AudioPipeline audio;
        readonly FrameMetrics timing=new FrameMetrics();
        LiveFrameWindow liveFrames;
        internal Action LivePause;
        internal int audioBufferMs=120;
        internal int resolutionScale=1,frameSkip;
        internal bool fastMemory,skipBufferEffects;
        internal bool EffectiveSkipBufferEffects=>jitActive&&skipBufferEffects;
        internal uint RenderWidth=>(uint)(480*resolutionScale);
        internal uint RenderHeight=>(uint)(272*resolutionScale);
        internal string GraphicsSummary=>resolutionScale+"x | "+(frameSkip==0?"Full frames":"Skip 1")+(EffectiveSkipBufferEffects?" | Effects skipped":"");
        long lastDiagnostics;
        bool gamePaused;
        BoolCall release;
        bool closed,reportedFrame;
        internal uint Frames => frames==null?0:frames();
        internal bool Faulted => callbackError!=null || (failed!=null && failed()!=0) || View==IntPtr.Zero;
        internal string InputSummary=>inputFrame==null?"No core input poll":"Core held: "+PspInputMap.Describe(inputFrame())+" | last: "+PspInputMap.Describe(inputLast())+" | polls="+inputPolls()+
            " | left="+inputAxis(0)+","+inputAxis(1)+" right="+inputAxis(2)+","+inputAxis(3);
        internal string Summary => callbackError??(Faulted?Result:CpuSummary+" | HW submissions="+Frames+" | "+(audio?.Summary??"Audio muted")+" | "+InputSummary);
        internal string DiagnosticContext=>"game_file="+Path.GetFileName(gamePath)+" selected_core="+(jitActive?"ARM_IR_JIT":"IR_INTERPRETER")+
            " core_sha256="+(jitActive?ExpectedJitHash:ExpectedHash)+" requested_audio_ms="+audioBufferMs+
            " resolution="+RenderWidth+"x"+RenderHeight+" frameskip="+frameSkip+" memory="+(jitActive?(fastMemory?"fast":"checked"):"core_default")+
            " memory_errors="+(jitActive&&!fastMemory?"stop":"core_default")+" buffer_effects="+(EffectiveSkipBufferEffects?"skipped":"normal")+" buffer_effects_requested="+(skipBufferEffects?"skipped":"normal")+
            " pacing="+(jitActive?"psp_vblank":"legacy_flip")+" inflight="+(jitActive?2:1)+" startup_faulted="+Faulted;
        IntPtr CoreAddress(string name) => IntPtr.Add(memory,plan.Export(name));
        IntPtr BridgeAddress(string name) => IntPtr.Add(bridgeMemory,bridgePlan.Export(name));
        T BridgeFunction<T>(string name) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(BridgeAddress(name));
        void PrepareGameCallbacks()
        {
            Stage("load native hardware bridge");
            foreach(string name in new[]{"libelementary.so.1","libevas.so.1"}) {
                var handle=dlopen(name,2);if(handle==IntPtr.Zero)throw new Exception("Missing "+name);handles.Add(name,handle);
            }
            byte[] bytes=File.ReadAllBytes(Path.Combine(resources,"libtizenpsp_corebridge.so"));
            if(Hash(bytes)!=File.ReadAllText(Path.Combine(resources,"corebridge.sha256")).Trim())throw new Exception("Core bridge checksum mismatch");
            int page=getpagesize();
            bridgePlan=DynamicElf.Parse(bytes,page,"psp_bridge_begin");
            bridgeMemory=mmap(IntPtr.Zero,new UIntPtr((uint)bridgePlan.Image.Length),3,0x22,-1,IntPtr.Zero);
            if(bridgeMemory==new IntPtr(-1))throw new Exception("Bridge mmap failed");
            var relocated=bridgePlan.RelocateSymbols(unchecked((uint)bridgeMemory.ToInt32()),Resolve);
            Marshal.Copy(relocated,0,bridgeMemory,relocated.Length);
            if(Flush(0x0f0002,bridgeMemory,IntPtr.Add(bridgeMemory,relocated.Length),0)!=0)throw new Exception("Bridge cache flush failed");
            for(int i=0;i<bridgePlan.Protections.Length;i++)
                if(mprotect(IntPtr.Add(bridgeMemory,i*page),new UIntPtr((uint)page),bridgePlan.Protections[i])!=0)throw new Exception("Bridge protection failed");
            ui=()=>ElmSharp.EcoreMainloop.IsMainThread?(byte)1:(byte)0;
            bridgeReport=p=> {
                try {
                    string message=Marshal.PtrToStringAnsi(p)??"(null)";
                    log("CORE_BRIDGE "+message);
                    if(message.StartsWith("CORE_STAGE")){File.WriteAllText(marker,message);JitStage(message);}
                } catch(Exception e) {callbackError="Bridge log: "+e.Message;}
            };
            if(BridgeFunction<BeginCall>("psp_bridge_begin")(CoreAddress("retro_run"),CoreAddress("retro_unload_game"),
                Marshal.GetFunctionPointerForDelegate(environment),Marshal.GetFunctionPointerForDelegate(ui),Marshal.GetFunctionPointerForDelegate(bridgeReport))==0)
                throw new Exception("Bridge initialization failed");
            BridgeFunction<BoolArg>("psp_bridge_set_jit_capable")(jitActive?(byte)1:(byte)0);
            audioCallback=(samples,count)=> {
                try {
                    ulong n=count.ToUInt64();if(n>192000)throw new ArgumentOutOfRangeException("audio batch");
                    audio?.Push(samples,(uint)n);
                } catch(Exception e){audio?.Reject("Audio callback: "+e.Message);}
                return count; // Queue overflow/mute is explicitly counted/discarded; no native retry.
            };
            frameObserver=phase=> {
                try {double now=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;if(phase==0)timing.Begin(now);else timing.End(now);liveFrames?.Observe(phase,now);}
                catch(Exception e){callbackError="Frame observer: "+e.Message;}
            };
            BridgeFunction<HostServices>("psp_bridge_set_host_services")(Marshal.GetFunctionPointerForDelegate(audioCallback),Marshal.GetFunctionPointerForDelegate(frameObserver));
            Function<PointerCall>("retro_set_environment")(BridgeAddress("psp_bridge_environment"));
            foreach(var pair in new[]{new[]{"video_refresh","video"},new[]{"audio_sample","audio"},new[]{"audio_sample_batch","audio_batch"},new[]{"input_poll","input_poll"},new[]{"input_state","input"}})
                Function<PointerCall>("retro_set_"+pair[0])(BridgeAddress("psp_bridge_"+pair[1]));
            tick=BridgeFunction<VoidCall>("psp_bridge_tick");close=BridgeFunction<VoidCall>("psp_bridge_close");
            pause=BridgeFunction<BoolArg>("psp_bridge_set_paused");buttons=BridgeFunction<UintArg>("psp_bridge_buttons");
            resetInput=BridgeFunction<VoidCall>("psp_bridge_reset_input");
            sticks=BridgeFunction<StickArgs>("psp_bridge_sticks");inputAxis=BridgeFunction<AxisCall>("psp_bridge_input_axis");
            frames=BridgeFunction<ApiVersionCall>("psp_bridge_frames");failed=BridgeFunction<ApiVersionCall>("psp_bridge_failed");
            duplicates=BridgeFunction<ApiVersionCall>("psp_bridge_duplicates");tickRequests=BridgeFunction<ApiVersionCall>("psp_bridge_tick_requests");
            coalesced=BridgeFunction<ApiVersionCall>("psp_bridge_coalesced");unscheduled=BridgeFunction<ApiVersionCall>("psp_bridge_unscheduled");
            nativeSuppressed=BridgeFunction<ApiVersionCall>("psp_bridge_log_suppressed");
            if(jitActive) {
                guestMillis=Function<ApiVersionCall>("retro_tizenpsp_guest_millis");
                actualFlips=Function<ApiVersionCall>("retro_tizenpsp_actual_flips");
                foldedFlips=Function<ApiVersionCall>("retro_tizenpsp_folded_flips");
            }
            fbo=BridgeFunction<ApiVersionCall>("psp_bridge_fbo");
            inputFrame=BridgeFunction<ApiVersionCall>("psp_bridge_input_frame");inputLast=BridgeFunction<ApiVersionCall>("psp_bridge_input_last");inputPolls=BridgeFunction<ApiVersionCall>("psp_bridge_input_polls");release=BridgeFunction<BoolCall>("psp_bridge_release");
        }
        void LoadContent()
        {
            Stage("retro_load_game: "+Path.GetFileName(gamePath));
            IntPtr info=Marshal.AllocHGlobal(16);
            try {
                for(int i=0;i<16;i+=4)Marshal.WriteInt32(info,i,0);
                Marshal.WriteIntPtr(info,Text(gamePath));
                if(Function<LoadCall>("retro_load_game")(info)==0)throw new Exception("retro_load_game rejected content; see report");
            } finally {Marshal.FreeHGlobal(info);}
            if(callbackError!=null)throw new Exception(callbackError);
            uint usec=BridgeFunction<RateCall>("psp_bridge_frame_usec")(CoreAddress("retro_get_system_av_info"));
            if(usec==0)throw new Exception("Unsupported AV geometry/timing");
            FrameSeconds=usec/1000000.0;
            Stage("retro_load_game returned; create GLES3 GLView "+RenderWidth+"x"+RenderHeight+"; frame_us="+usec);
            View=BridgeFunction<ViewCall>("psp_bridge_create_view")(parent,RenderWidth,RenderHeight);
            if(View==IntPtr.Zero)throw new Exception("Hardware request or GLView setup rejected; see report");
            uint rate=BridgeFunction<ApiVersionCall>("psp_bridge_sample_rate")();
            log("CORE_AV sample_rate="+rate+" frame_us="+usec+" resolution="+RenderWidth+"x"+RenderHeight+" frameskip="+frameSkip+" buffered_effects="+(EffectiveSkipBufferEffects?"disabled":"enabled")+" auto_frameskip=disabled");
            if(audioBufferMs>0)audio=new AudioPipeline((int)rate,audioBufferMs);
            log("AUDIO_CONFIG requested_buffer_ms="+audioBufferMs+"; PCM S16 stereo; frame timer remains the only emulation scheduler");
            lastDiagnostics=Stopwatch.GetTimestamp();
        }
        internal void Tick()
        {
            if(closed)return;
            if(Faulted){audio?.Stop();LogDiagnostics();return;}
            if(Frames>0 && !reportedFrame) {
                reportedFrame=true; File.WriteAllText(marker,"HW video callbacks observed for "+Path.GetFileName(gamePath)+"; game session not yet closed");
                log("CORE_HW_SUBMISSION observed; requires TV visual confirmation, not proof of game boot");
            }
            tick();
            if(Stopwatch.GetTimestamp()-lastDiagnostics>=Stopwatch.Frequency*5)LogDiagnostics();
        }
        internal void LogDiagnostics() {
            lastDiagnostics=Stopwatch.GetTimestamp();
            foreach(string line in Diagnostics)log(line);
        }
        internal string[] Diagnostics=>new[]{
            DiagnosticContext,
            inputState.Summary,
            JitSummary,
            HostPerformanceInfo.Snapshot(),
            ThreadCpuInfo.Snapshot(),
            "CORE_PROGRESS guest_ms="+(guestMillis?.Invoke().ToString()??"unavailable")+
                " actual_flips="+(actualFlips?.Invoke().ToString()??"unavailable")+
                " folded_early_flips="+(foldedFlips?.Invoke().ToString()??"unavailable")+
                " native_log_suppressed="+(nativeSuppressed?.Invoke()??0),
            timing.Summary(FrameSeconds,Frames,duplicates?.Invoke()??0)+" tick_requests="+(tickRequests?.Invoke()??0)+" coalesced="+(coalesced?.Invoke()??0)+" unscheduled_callbacks="+(unscheduled?.Invoke()??0),
            audio?.Diagnostics??"AUDIO muted by selection"};
        internal void Pause(bool value) {
            if(closed)return;
            if(value)LivePause?.Invoke();
            if(gamePaused!=value){if(value)LogDiagnostics();timing.Pause();gamePaused=value;}
            inputState.Forget(); // Native pause also resets input, even if audio pause fails.
            pause?.Invoke(value?(byte)1:(byte)0);audio?.Pause(value);ResetInput();
        }
        internal void ResetInput() {inputState.Forget();resetInput?.Invoke();}
        internal void Buttons(uint mask) {if(!closed && buttons!=null && inputState.ButtonsChanged(mask))buttons(mask);}
        internal void Sticks(int lx,int ly,int rx,int ry) {if(!closed && sticks!=null && inputState.SticksChanged(lx,ly,rx,ry))sticks(lx,ly,rx,ry);}
        internal void Close()
        {
            if(closed)return;
            closed=true;
            LogDiagnostics();audio?.Stop();
            if(View==IntPtr.Zero)return; // Partial init retained until process exit, no speculative cleanup.
            Stage("close core GLView");close();View=IntPtr.Zero;
            if(release()==0) {log("Core cleanup incomplete; permanent mapping retained");return;}
            Stage("retro_deinit after GLView cleanup");Function<VoidCall>("retro_deinit")();
            File.Delete(marker);log("CORE_GAME_CLOSED; mapping retained until process exit");
            FinishJitAttempt();
        }
    }
}
