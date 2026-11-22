using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ElmSharp;
using Tizen.Applications;

internal sealed partial class TizenPspApp : CoreUIApplication
{
    const int Width=640, Height=360;
    Window window;
    Label label;
    IntPtr image, startupTimer, glviewTimer;
    GlViewBridge glview;
    NativeGraphics.CoreStartup game;
    string[] games=new string[0];
    int selected;
    bool selecting;
    IntPtr gameTimer;
    int glviewTicks, glviewWaitingTicks;
    GraphicsSessionLog session;
    string glviewPendingPath;
    readonly GraphicsCounters counters=new GraphicsCounters();
    readonly HashSet<string> held=new HashSet<string>();
    bool tested,paused,terminating,reportVisible;
    string status="Preparing graphics diagnostics...";
    string[] reportLines=new string[0];
    int reportPage, reportRows=20, reportColumns=100;
    static void Main(string[] args)
    {
        Elementary.Initialize(); Elementary.ThemeOverlay(); new TizenPspApp().Run(args);
    }
    void Log(string text) {
        if(session==null)return;
        if(text.StartsWith("FRAME_TIMING",StringComparison.Ordinal)){session.FlushCore();session.Add(session.CoreSummary);}
        session.Add(text);
    }
    protected override void OnCreate()
    {
        base.OnCreate();
        session=new GraphicsSessionLog(Application.Current.DirectoryInfo.Data);
        session.SetContext("app_version="+AppVersion.Current+" game=none");
        glviewPendingPath=Path.Combine(Application.Current.DirectoryInfo.Data,"glview-session.pending");
        Log("TizenPPSSPP "+AppVersion.Current+" | library preferences / pause menu | UTC timestamps");
        LoadAudioChoice();
        LoadLibraryPreferences();
        Log("Log directory: "+Application.Current.DirectoryInfo.Data);
        Log("RUNTIME_MANAGED "+RuntimeInformation.FrameworkDescription+" os="+RuntimeInformation.OSDescription);
        Log("GLView prototype: scalar legacy API only; no Evas struct/table access. Driver/FBO validation required in callback.");
        Log("Metrics: average_frame_call_ms includes ELF load, context creation, diagnostics, drawing, readback and teardown; not steady-state frame time/FPS.");
        Log("context_creation_failures counts explicit diagnostic-backend failures only; original baseline ABI cannot distinguish these.");
        Log("COUNTERS tracks readback tests; GLVIEW_COUNTERS separately tracks native shader callbacks and CPU submission time, not GPU frame time.");
        window=new Window("TizenPPSSPP"); window.Show(); window.Active();
        var background=new Background(window){Color=Color.FromRgb(8,10,22)};
        background.Show(); window.AddResizeObject(background);
        label=new Label(window){LineWrapType=WrapType.Mixed}; label.Layer=10; label.Show();
        window.Resized+=(s,e)=> { Log("LIFECYCLE resize "+window.Geometry.Width+"x"+window.Geometry.Height); Layout(); };
        window.KeyDown+=(s,e)=>OnInputKey(e.KeyName,true);
        window.KeyUp+=(s,e)=>OnInputKey(e.KeyName,false);
        InitializeInput();
        InitializePerformanceOverlay();
        InitializeLiveConnection();
        Layout();
        startupTimer=EcoreMainloop.AddTimer(0.1,()=> {
            startupTimer=IntPtr.Zero;
            if(!tested && !paused && !terminating) {StartDefault();if(!File.Exists(mappingPath))OpenMapper();}
            return false;
        });
        Log("LIFECYCLE create; UI thread="+EcoreMainloop.IsMainThread);
    }
    void Key(string key)
    {
        if(terminating || paused) return;
        if(optionsMenu.Page!=MenuPage.None) {OptionsKey(key);return;}
        if(reportVisible && (key=="Down" || key=="e" || key=="E")) {ExportDiagnostics();return;}
        if(mapperVisible) {MapperKey(key);return;}
        if(key=="m" || key=="M" || key=="F2") {OpenMapper();return;}
        if(reportVisible && key=="Up") {OpenMapper();return;}
        if(selecting && !reportVisible) {LibraryKey(key);return;}
        if(game!=null && !reportVisible) {
            if(key=="XF86Back" || key=="Escape")OpenOptions();
            else if(key=="F3" || key=="XF86Yellow")TogglePerformanceOverlay();
            else if(key=="XF86Info" || key=="i" || key=="F1")OpenReport(false);
            return;
        }
        if(key=="XF86Back" || key=="Escape") {
            if(reportVisible)CloseReport();else AskExit();
        } else if(key=="Return" || key=="KP_Enter") {
            if(reportVisible)CloseReport();else OpenReport(false);
        } else if(reportVisible) {
            if(key=="Left") reportPage=Math.Max(0,reportPage-1);
            if(key=="Right") reportPage=Math.Min(PageCount-1,reportPage+1);
            ShowText();
        } else if(key=="Left" || key=="Right") Test(key=="Left");
        else if(key=="Up") OpenMapper();
        else if(key=="Down") ScanGames();
    }
    int PageCount => Math.Max(1,(reportLines.Length+reportRows-1)/reportRows);
    string[] SnapshotReport() {
        session.FlushCore();
        var lines=new List<string>();
        string data=Application.Current.DirectoryInfo.Data;
        lines.AddRange(GraphicsSessionLog.Wrap(new[]{session.Context,session.CoreSummary,InputDiagnostics()},reportColumns));
        string attempt=DiagnosticFiles.ReadTail(Path.Combine(data,"jit-attempt.txt"),8192);
        if(attempt.Length>0)lines.AddRange(GraphicsSessionLog.Wrap(new[]{"LAST JIT ATTEMPT (may be from an earlier session; check timestamp)",attempt},reportColumns));
        if(game!=null) {
            lines.AddRange(GraphicsSessionLog.Wrap(new[]{"CURRENT GAME METRICS (CPU timings; audio gaps are application queue events)"},reportColumns));
            lines.AddRange(GraphicsSessionLog.Wrap(game.Diagnostics,reportColumns));
            lines.Add("SESSION LOG");
        }
        string native=DiagnosticFiles.ReadTail(Path.Combine(data,"jit-native.log"),4096);
        if(native.Length>0)lines.AddRange(GraphicsSessionLog.Wrap(new[]{"NATIVE JIT TRACE (latest events)",native},reportColumns));
        if(game==null) {
            string previous=DiagnosticFiles.ReadTail(Path.Combine(data,"graphics-test.previous.log"),4096);
            if(previous.Length>0)lines.AddRange(GraphicsSessionLog.Wrap(new[]{"PREVIOUS SESSION (tail)",previous},reportColumns));
        }
        lines.AddRange(session.Snapshot(reportColumns));return lines.ToArray();
    }
    void ExportDiagnostics() {
        try {
            session.FlushCore();
            string folder=SelectedItem!=null?Path.GetDirectoryName(SelectedItem.Path):allItems.Length>0?Path.GetDirectoryName(allItems[0].Path):null;
            string metrics=session.Context+Environment.NewLine+session.CoreSummary+Environment.NewLine+InputDiagnostics()+Environment.NewLine+
                (game==null?"No active game":string.Join(Environment.NewLine,game.Diagnostics));
            ReportExportSummary summary;
            string path=DiagnosticFiles.Export(Application.Current.DirectoryInfo.Data,folder,metrics,out summary);
            Log("LOG_EXPORT "+path+" bytes="+summary.Bytes+" recovered="+summary.RecoveredReports+" backups="+summary.BackupCount+" warnings="+summary.Warnings);
            var lines=new List<string>{"USB report verified: "+summary.Bytes.ToString("N0")+" bytes",path,
                "Copies retained in app: "+summary.BackupCount};
            if(summary.RecoveredReports>0)lines.Add("Earlier reports also copied to USB: "+summary.RecoveredReports);
            if(!string.IsNullOrEmpty(summary.Warnings))lines.Add("Storage warning: "+summary.Warnings);
            lines.Add("Export complete. Use safe USB removal if available, then share the text files.");
            reportLines=GraphicsSessionLog.Wrap(lines,reportColumns).ToArray();reportPage=0;
        }catch(ReportExportException e){
            Log("LOG_EXPORT_ERROR "+e.Message+" type="+e.OriginalExceptionType+" hresult=0x"+e.OriginalHResult.ToString("X8")+
                " current_backup="+e.BackupRetained+" backups="+e.BackupCount);
            Log("LOG_EXPORT_DETAIL "+e.ToString());
            var lines=new List<string>{"USB export did not complete: "+e.Message};
            if(e.BackupRetained)lines.Add("This report is retained in the app. Reconnect the game USB and export again; no need to replay the game.");
            else if(e.BackupLimitReached)lines.Add("Earlier reports are retained, but there is no room for this snapshot. Keep the app open, reconnect the game USB and export again.");
            else lines.Add("This snapshot could not be retained. Keep the app open and export again after checking storage.");
            reportLines=GraphicsSessionLog.Wrap(lines,reportColumns).ToArray();reportPage=0;
        }catch(Exception e){
            Log("LOG_EXPORT_ERROR "+e.ToString());reportLines=GraphicsSessionLog.Wrap(new[]{"Could not save logs: "+e.Message,
                "Keep the app open, check the game USB connection, and try exporting again."},reportColumns).ToArray();reportPage=0;
        }
        ShowText();
    }
    void ShowText()
    {
        if(label==null) return;
        UpdatePerformanceOverlay();
        if(mapperVisible) {libraryView?.Hide();label.Hide();DrawMapper();return;}
        if(optionsMenu.Page!=MenuPage.None && !paused && !terminating){label.Hide();DrawOptions();return;}
        if(selecting && !reportVisible && !paused && !terminating){label.Hide();DrawLibrary();return;}
        libraryView?.Hide();
        if(game!=null && !game.Faulted && !reportVisible && !paused && !terminating){label.Hide();return;}
        label.Show();
        string warning=session.WriteError==null?"":"<br/>Log file unavailable: "+Escape(session.WriteError)+". Use on-screen report.";
        if(reportVisible) {
            reportPage=Math.Min(reportPage,PageCount-1);
            var text=new System.Text.StringBuilder();
            text.Append("<font_size=24>TizenPPSSPP diagnostics — page ").Append(reportPage+1).Append("/").Append(PageCount)
                .Append(" — Left/Right: pages • Down: USB logs • OK / Back: return<br/>");
            int end=Math.Min(reportLines.Length,(reportPage+1)*reportRows);
            for(int i=reportPage*reportRows;i<end;i++) text.Append(Escape(reportLines[i])).Append("<br/>");
            label.Text=text.ToString()+warning+"</font_size>";
        } else label.Text="<font_size=26>TizenPPSSPP "+AppVersion.Current+" — "+Escape(status)+
            "<br/>"+(selecting?"Left/Right: select | OK: details | Up: options | Down: rescan | Back: exit":game!=null?"Mapped controller | Back: pause menu | Info: diagnostics":"Down: scan USB | OK: report | Back: exit")+warning+"</font_size>";
    }
    void Layout()
    {
        if(window==null || label==null) return;
        var g=window.Geometry; int w=Math.Max(320,g.Width),h=Math.Max(240,g.Height);
        int columns=Math.Max(12,(w-64)/24),rows=Math.Max(3,(h-100)/38);
        if(columns!=reportColumns || rows!=reportRows) {
            reportColumns=columns; reportRows=rows;
            if(reportVisible) { reportLines=SnapshotReport(); reportPage=0; }
        }
        label.Geometry=new Rect(24,16,w-48,reportVisible?h-32:110);
        if(game!=null && game.View!=IntPtr.Zero) {
            int vw=Math.Min(w,h*480/272),vh=vw*272/480,x=(w-vw)/2,y=(h-vh)/2;
            Screen.evas_object_move(game.View,x,y);Screen.evas_object_resize(game.View,vw,vh);
            LayoutPerformanceOverlay(x,y,vw,vh);
            if(!reportVisible && !mapperVisible && !paused && !terminating)Screen.evas_object_show(game.View);else Screen.evas_object_hide(game.View);
        }
        if(glview!=null) glview.Layout(24,130,w-48,Math.Max(1,h-154),!reportVisible && !mapperVisible && !paused && !terminating);
        if(image!=IntPtr.Zero) {
            int vw=Math.Min(w-48,(h-150)*16/9),vh=vw*9/16;
            Screen.evas_object_move(image,(w-vw)/2,130);
            Screen.evas_object_resize(image,vw,vh);
            if(reportVisible || mapperVisible) Screen.evas_object_hide(image); else Screen.evas_object_show(image);
        }
        ShowText();
    }
    void StopGlView()
    {
        if(glviewTimer!=IntPtr.Zero) { EcoreMainloop.RemoveTimer(glviewTimer); glviewTimer=IntPtr.Zero; }
        if(glview!=null) {
            Log("GLVIEW_COUNTERS "+glview.Summary);
            glview.Dispose(); // Keep field rooted if detachment fails; never unmap an active callback.
            glview=null;
            try { File.Delete(glviewPendingPath); } catch(Exception e) { Log("GLVIEW recovery-marker cleanup: "+e.Message); }
        }
    }
    void StartDefault()
    {
        string marker=Path.Combine(Application.Current.DirectoryInfo.Data,"core-startup.pending");
        string previous=File.Exists(marker)?File.ReadAllText(marker):null;
        if(previous!=null)Log("Previous core session stopped at: "+previous);
        string jitMarker=Path.Combine(Application.Current.DirectoryInfo.Data,"jit-attempt.pending");
        bool jitInterrupted=File.Exists(jitMarker);
        if(jitInterrupted)Log("PREVIOUS_JIT_INCOMPLETE "+DiagnosticFiles.ReadTail(jitMarker)+"; saved CPU preference retained; ARM JIT is the default");
        ScanGames();
        if(previous!=null){status="Previous game session was interrupted. Info opens the report.";ShowText();}
        if(jitInterrupted){status="Previous session incomplete. Info then Down exports logs; game details offers an interpreter override.";ShowText();}
    }
    void SelectionStatus() {
        status=games.Length==0?(allItems.Length==0?"No ISO/CSO/PBP/ELF found in USB /PSP. Extract archives; Down rescans.":"No games in "+libraryFilter+". Up opens library options."):
            "USB PSP "+(selected+1)+"/"+games.Length+": "+Path.GetFileName(games[selected]);
    }
    void ScanGames() {
        if(game!=null) {status="Exit and reopen to choose another game";ShowText();return;}
        tested=true;selecting=true;reportVisible=false;
        string previous=games.Length>0?games[selected]:null;
        allItems=UsbGames.Find(Log).Select(path=> {
            long bytes=-1;try{bytes=new FileInfo(path).Length;}catch(Exception e){Log("LIBRARY_METADATA "+e.Message);}
            return new LibraryItem(path,bytes);
        }).ToArray();
        libraryView?.RefreshArtwork();gameDetails=false;
        ApplyLibraryView(previous);Layout();
    }
    void StartSelectedGame() {
        if(SelectedItem==null)return;
        var launchedItem=SelectedItem;
        bool recorded=false,attemptJit=EffectiveArmJit;
        selecting=false;gameDetails=false;reportVisible=false;ClearUiInput();
        libraryView?.Hide(); // Release menu artwork before allocating the emulation core.
        session.SetContext("app_version="+AppVersion.Current+" game_file="+Path.GetFileName(launchedItem.Path)+" requested_cpu="+(attemptJit?"ARM_JIT":"IR_INTERPRETER")+" requested_audio_ms="+EffectiveAudio+" requested_buffer_effects="+(EffectiveSkipBufferEffects?"skipped":"normal")+" stage=launch_begin");
        try {
            // Verify readability before native code sees the path; never copy the ISO into the package.
            using(var input=File.OpenRead(games[selected])) {if(input.Length==0)throw new Exception("Selected game is empty");}
            game=NativeGraphics.StartPpssppGame(Application.Current.DirectoryInfo.Resource,Application.Current.DirectoryInfo.Data,games[selected],window.Handle,Log,EffectiveAudio,attemptJit,EffectiveResolutionScale,EffectiveFrameSkip,EffectiveFastMemory,EffectiveSkipBufferEffects);
            game.LivePause=()=>experiment?.Cancel("game_paused");
            session.SetContext("app_version="+AppVersion.Current+" "+game.DiagnosticContext);
            status=game.Result;Layout();
            if(game.Faulted)return;
            gameTimer=EcoreMainloop.AddTimer(game.FrameSeconds,()=> {
                try {
                    if(terminating) {gameTimer=IntPtr.Zero;return false;}
                    if(paused || reportVisible || mapperVisible)return true;
                    PumpController();UpdateGameButtons();
                    if(reportVisible || mapperVisible)return true;
                    game.Tick();
                    if(game.Faulted) {status="PPSSPP stopped; Info/I/F1: report. "+game.Summary;Log(status);ShowText();gameTimer=IntPtr.Zero;return false;}
                    if(!recorded && game.Frames>0) {
                        recorded=true;
                        try{catalog.RecordStarted(launchedItem,DateTime.UtcNow);SaveLibraryPreferences();Log("LIBRARY_RECENT first video frame: "+launchedItem.Title);}
                        catch(Exception e){Log("LIBRARY_RECENT "+e.Message);}
                    }
                    if(++glviewTicks%60==0) {status=game.Summary;ShowText();}
                    return true;
                } catch(Exception e) {status="Game tick failed: "+e.Message;Log(e.ToString());ShowText();gameTimer=IntPtr.Zero;return false;}
            });
        } catch(Exception e) {status="Game boot failed: "+e.Message;Log(e.ToString());Layout();}
    }
    void StartActualCore()
    {
        if(paused || terminating) return;
        tested=true; reportVisible=false;
        try {
            StopGlView();
            if(image!=IntPtr.Zero) { Screen.evas_object_del(image); image=IntPtr.Zero; }
            status=NativeGraphics.StartPpsspp(Application.Current.DirectoryInfo.Resource,Application.Current.DirectoryInfo.Data,Log);
        } catch(Exception e) {status="Core startup error: "+e.Message;Log(e.ToString());}
        Layout();
    }
    void StartCoreCheck()
    {
        if(paused || terminating) return;
        tested=true; reportVisible=false;
        try {
            StopGlView();
            if(image!=IntPtr.Zero) { Screen.evas_object_del(image); image=IntPtr.Zero; }
            Log("CORE_PREFLIGHT_BEGIN: checks exact core imports only; no core mapping or execution");
            status=NativeGraphics.CheckCoreRuntime(Application.Current.DirectoryInfo.Resource,Log);
        } catch(Exception e) {status="Core runtime check failed: "+e.Message;Log(e.ToString());}
        Layout();
    }
    void StartGlView()
    {
        if(paused || terminating) return;
        tested=true;
        try {
            StopGlView();
            if(image!=IntPtr.Zero) { Screen.evas_object_del(image); image=IntPtr.Zero; }
            File.WriteAllText(glviewPendingPath,"GLView teardown pending\n");
            var api=new GlViewApi();
            NativeGraphics.ClearModule renderer;
            try { renderer=new NativeGraphics.ClearModule(Application.Current.DirectoryInfo.Resource,Log); }
            catch { api.Dispose(); throw; }
            glview=new GlViewBridge(api,renderer,()=>EcoreMainloop.IsMainThread,Log);
            glview.Start(window.Handle);
            counters.Backend="glview-hw-shader-test";
            status="GLView waiting for native shader callback";
            glviewTicks=glviewWaitingTicks=0;
            Layout();
            glviewTimer=EcoreMainloop.AddTimer(1.0/30,()=> {
                try {
                    if(terminating || glview==null) { glviewTimer=IntPtr.Zero; return false; }
                    if(paused || reportVisible) return true;
                    glview.Tick();
                    if(glview.Frames==0 && ++glviewWaitingTicks>=150) glview.NoCallbacks();
                    if(glview.Faulted) {
                        status="GLView stopped — see report; Left/Right selects readback fallback";
                        Log("GLVIEW_COUNTERS "+glview.Summary); Layout();
                        glviewTimer=IntPtr.Zero; return false;
                    }
                    if(++glviewTicks%30==0) {
                        status=glview.Frames==0?"GLView waiting for native callback":"C++ startup OK | shader frames="+glview.Frames+" | CPU readbacks=0";
                        if(glviewTicks%300==0) Log("GLVIEW_COUNTERS "+glview.Summary);
                        ShowText();
                    }
                    return true;
                } catch(Exception e) { Log("GLVIEW_TIMER_ERROR "+e.Message); status="GLView error — see report"; ShowText(); glviewTimer=IntPtr.Zero; return false; }
            });
        } catch(Exception e) {
            Log("GLVIEW_START_ERROR "+e);
            status="GLView unavailable: "+e.Message;
            ShowText();
        }
    }
    void Test(bool baseline)
    {
        if(paused || terminating) return;
        tested=true;
        counters.Backend=baseline?"egl-pbuffer-readback-v0.1.0":"egl-pbuffer-readback-diagnostics";
        Log("TEST_BEGIN "+counters.Backend+"; UI thread="+EcoreMainloop.IsMainThread);
        IntPtr pixels=IntPtr.Zero;
        bool attemptCounted=false,nativeSucceeded=false;
        try {
            StopGlView();
            if(!EcoreMainloop.IsMainThread) throw new Exception("Graphics test requires UI thread");
            Log("Process: "+RuntimeInformation.ProcessArchitecture+", pointer bytes="+IntPtr.Size);
            var canvas=Screen.evas_object_evas_get(window.Handle);
            if(canvas==IntPtr.Zero) throw new Exception("Window has no Evas canvas");
            Log("Verified non-null window Evas canvas.");
            if(!baseline) {
                try { NativeGraphics.Probe(Log); }
                catch(Exception e) { counters.DiagnosticErrors++; Log("Symbol probe unavailable: "+e.Message); }
            }
            pixels=Marshal.AllocHGlobal(Width*Height*4);
            var timer=Stopwatch.StartNew();
            try {
                NativeGraphics.Render(Application.Current.DirectoryInfo.Resource,pixels,Width,Height,
                    text=> { counters.Observe(text); Log(text); },baseline);
                nativeSucceeded=true;
            } finally {
                timer.Stop(); counters.Complete(nativeSucceeded,timer.Elapsed.TotalMilliseconds); attemptCounted=true;
            }
            if(image==IntPtr.Zero) image=Screen.evas_object_image_filled_add(canvas);
            if(image==IntPtr.Zero) throw new Exception("Evas image allocation failed");
            Screen.evas_object_image_size_set(image,Width,Height);
            Screen.evas_object_image_data_copy_set(image,pixels);
            Screen.evas_object_image_data_update_add(image,0,0,Width,Height);
            counters.FramesPresented++;
            status=baseline?"Original v0.1.0 triangle submitted":"Diagnostic triangle submitted (CPU readback)";
            Log("Evas viewport submitted; visual confirmation required for this build.");
        } catch(Exception e) {
            if(!attemptCounted) counters.Complete(false,0);
            else if(nativeSucceeded) counters.PresentationErrors++;
            // An old triangle must not masquerade as a successful retry.
            if(image!=IntPtr.Zero) { Screen.evas_object_del(image); image=IntPtr.Zero; }
            status="Graphics unavailable: "+e.Message;
            Log(e.ToString());
        } finally {
            if(pixels!=IntPtr.Zero) Marshal.FreeHGlobal(pixels);
            Log("COUNTERS "+counters.Summary); Layout();
        }
    }
    static string Escape(string text) => text.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;");
    protected override void OnPause()
    {
        experiment?.Cancel("application_paused");
        FinishButtonCheck("application paused");
        ResetFaces();
        paused=true; held.Clear();acceptedKeys.Clear();pendingKeys.Clear();inputMap.ClearHeld();sticks.Clear();controllerNavigation.Reset();controller?.Enable(false);game?.Pause(true);
        HidePerformanceOverlay();
        Log("LIFECYCLE pause; presentation retained; see backend-specific resource status");
        if(glview!=null) { Log("GLVIEW pause: stop invalidation, retain widget; "+glview.Summary); Layout(); }
        Log("COUNTERS "+counters.Summary); base.OnPause();
    }
    protected override void OnResume()
    {
        base.OnResume(); ps5Refresh=true;paused=false; held.Clear();inputMap.ClearHeld();sticks.Clear();controllerNavigation.Reset();controller?.Enable(true);game?.Pause(reportVisible || mapperVisible); Log("LIFECYCLE resume");
        if(window!=null) { if(!tested && startupTimer==IntPtr.Zero) StartDefault(); else Layout(); }
    }
    protected override void OnTerminate()
    {
        StopLiveConnection();
        FinishButtonCheck("application terminating");
        faceReader?.Stop();
        terminating=true;ps5Pads.Stop();controller?.Stop();
        DisposePerformanceOverlay();
        if(inputTimer!=IntPtr.Zero) {EcoreMainloop.RemoveTimer(inputTimer);inputTimer=IntPtr.Zero;}
        Log("LIFECYCLE terminate; UI thread="+EcoreMainloop.IsMainThread);
        if(libraryTimer!=IntPtr.Zero){EcoreMainloop.RemoveTimer(libraryTimer);libraryTimer=IntPtr.Zero;}
        if(startupTimer!=IntPtr.Zero) { EcoreMainloop.RemoveTimer(startupTimer); startupTimer=IntPtr.Zero; }
        if(gameTimer!=IntPtr.Zero) {EcoreMainloop.RemoveTimer(gameTimer);gameTimer=IntPtr.Zero;}
        try {game?.Close();} catch(Exception e) {Log("CORE_CLOSE_ERROR "+e);}
        try { StopGlView(); } catch(Exception e) { Log("GLVIEW shutdown detachment failed; retaining mapped module until process exit: "+e.Message); }
        if(image!=IntPtr.Zero) { Screen.evas_object_del(image); image=IntPtr.Zero; }
        Log("COUNTERS "+counters.Summary); Log("SHUTDOWN_COMPLETE: image and startup timer released");
        session?.FlushCore();
        base.OnTerminate();
    }
}
internal static class Screen
{
    const string E="libevas.so.1";
    [DllImport(E)] internal static extern IntPtr evas_object_evas_get(IntPtr obj);
    [DllImport(E)] internal static extern IntPtr evas_object_image_filled_add(IntPtr canvas);
    [DllImport(E)] internal static extern void evas_object_image_size_set(IntPtr obj,int w,int h);
    [DllImport(E)] internal static extern void evas_object_image_data_copy_set(IntPtr obj,IntPtr data);
    [DllImport(E)] internal static extern void evas_object_image_data_update_add(IntPtr obj,int x,int y,int w,int h);
    [DllImport(E)] internal static extern void evas_object_move(IntPtr obj,int x,int y);
    [DllImport(E)] internal static extern void evas_object_resize(IntPtr obj,int w,int h);
    [DllImport(E)] internal static extern void evas_object_show(IntPtr obj);
    [DllImport(E)] internal static extern void evas_object_hide(IntPtr obj);
    [DllImport(E)] internal static extern void evas_object_del(IntPtr obj);
}
