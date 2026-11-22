using System;
using System.IO;
using System.Collections.Generic;
using ElmSharp;
using Tizen.Applications;

internal sealed partial class TizenPspApp
{
    PspInputMap inputMap=new PspInputMap();
    StickInput sticks=new StickInput();
    readonly short[] gameAxes=new short[4];
    StickCalibration stickCalibration;
    string stickPath,stickMessage="Move both sticks to test them. Calibrate with the TV remote.";
    bool sticksVisible;
    int stickSelected;
    readonly ControllerNavigation controllerNavigation=new ControllerNavigation();
    ControllerReader controller;
    readonly List<ControllerReader.Event> controllerBatch=new List<ControllerReader.Event>(128);
    InputMapperView mapperView;
    InputComparison comparison;
    readonly InputLearningWatch learningWatch=new InputLearningWatch();
    bool accessProbeBusy;
    IntPtr inputTimer;
    string mappingPath,mapperMessage="Select a PSP button, then press OK to learn it.";
    bool mapperVisible,learning,learnAll,learnReady;
    int mapperSelected=6,inputRefresh;
    double lastRaw=-1000,armAfter,lastDraw;
    readonly Queue<PendingKey> pendingKeys=new Queue<PendingKey>();
    readonly Queue<double> rawTimes=new Queue<double>();
    readonly HashSet<string> acceptedKeys=new HashSet<string>();
    sealed class PendingKey {internal string Key;internal double Time;}
    void InitializeInput()
    {
        mappingPath=Path.Combine(Application.Current.DirectoryInfo.Data,"psp-controller-map.txt");
        stickPath=Path.Combine(Application.Current.DirectoryInfo.Data,"psp-controller-sticks.txt");
        sticks=StickInput.Load(stickPath,Log);
        mapperView=new InputMapperView(window);
        controller=new ControllerReader();
        InitializeNativePs5();
        inputTimer=EcoreMainloop.AddTimer(1.0/60,()=> {
            if(terminating){inputTimer=IntPtr.Zero;return false;}
            if(paused)return true;
            try {
                PumpController();NavigateController(ControllerReader.Now);ProcessKeyQueue();
                double now=ControllerReader.Now;
                if(buttonCheck!=null){buttonCheck.Tick(now);if(buttonCheck.Finished)FinishButtonCheck();}
                if(comparison!=null && !comparison.Finished) {
                    comparison.Tick(now,inputMap.Neutral);mapperSelected=comparison.Action;
                    if(comparison.Finished){pendingKeys.Clear();inputMap.ClearHeld();controllerNavigation.BlockHeld();game?.ResetInput();Log("INPUT_COMPARE "+comparison.Summary);}
                }
                if(learning && !learnReady && now>=armAfter && inputMap.Neutral) {learnReady=true;mapperMessage="Press and RELEASE "+PspInputMap.Names[mapperSelected]+" on your controller.";Log("INPUT_LEARN_READY action="+PspInputMap.Names[mapperSelected]);}
                UpdateGameButtons();
                UpdatePerformanceOverlay();
                if(mapperVisible && now-lastDraw>.1)DrawMapper();
                if(++inputRefresh%300==0 && (game!=null || mapperVisible))Log(InputDiagnostics());
            } catch(Exception e) {Log("INPUT_ERROR "+e.Message);ResetFaces();game?.ResetInput();}
            return true;
        });
    }
    void OnInputKey(string key,bool down)
    {
        if(terminating || paused)return;
        double now=ControllerReader.Now;
        buttonCheck?.Key("window",key,0,down,now);
        if(down)comparison?.Observe("key:"+key,false,now);
        inputMap.ObserveKey(key,down);
        if(down) {
            if(!held.Add(key))return;
            // The remote's OK used to arm learning must be released first.
            pendingKeys.Enqueue(new PendingKey{Key=key,Time=now});
        } else {
            held.Remove(key);acceptedKeys.Remove(key);
        }
        UpdateGameButtons();
    }
    void PumpController(){PumpNativePs5();}
    void NavigateController(double now) {
        bool visible=game==null || reportVisible || mapperVisible;
        uint repeat=mapperVisible || selecting && !reportVisible && gameDetails || optionsMenu.Page!=MenuPage.None?ControllerNavigation.Directions:ControllerNavigation.Horizontal;
        string action=controllerNavigation.Next(now,visible,learning || buttonCheck!=null || stickCalibration!=null || comparison!=null && !comparison.Finished,repeat);
        if(action==null)return;
        if(action=="ToggleStats") {game?.ResetInput();TogglePerformanceOverlay();UpdateGameButtons();return;}
        if(action=="AppMenu") {if(mapperVisible)return;if(optionsMenu.Page!=MenuPage.None)Key("XF86Back");else OpenOptions();}
        else Key(action);
    }
    bool IsControllerEcho(double time)=>ps5Pads.Slots.RecentActivity((long)(time*System.Diagnostics.Stopwatch.Frequency));
    void ProcessKeyQueue()
    {
        double now=ControllerReader.Now;
        while(pendingKeys.Count>0 && now-pendingKeys.Peek().Time>=.32) {
            var e=pendingKeys.Dequeue();if(IsControllerEcho(e.Time))continue;
            if(buttonCheck!=null) {
                if(e.Key=="Escape"||e.Key=="XF86Back"&&!IsControllerEcho(e.Time))FinishButtonCheck("cancelled with Back");
                continue;
            }
            if(sticksVisible && stickCalibration!=null && (e.Key=="XF86Back"||e.Key=="Escape")){StickKey(e.Key);continue;}
            // Back is reserved for leaving capture, even if a controller never sent
            // its release. Echo filtering must not trap the user inside learning.
            if(mapperVisible && (learning || comparison!=null) && (e.Key=="XF86Back" || e.Key=="Escape")) {
                Log("INPUT_LEARN_CANCEL "+InputDiagnostics());comparison=null;learning=learnAll=learnReady=false;
                ClearUiInput();mapperMessage="Capture cancelled. Bindings kept; choose an action or Input slot.";DrawMapper();continue;
            }
            if(comparison!=null && !comparison.Finished)continue;
            if(IsControllerEcho(e.Time) && !(!learning && !mapperVisible && inputMap.MappedKey(e.Key))) {
                if(!inputMap.MappedKey(e.Key))inputMap.ObserveKey(e.Key,false);continue;
            }
            if(learning) {
                if(e.Key=="XF86Back" || e.Key=="Escape") {learning=learnAll=false;mapperMessage="Capture cancelled. Select a button or Save & return.";continue;}
                if(learnReady && e.Time>=armAfter) {Capture("key:"+e.Key);continue;}
                continue;
            }
            if(mapperVisible) {MapperKey(e.Key);continue;}
            if(game!=null && !reportVisible && inputMap.MappedKey(e.Key)) {
                int target=Array.IndexOf(inputMap.Bindings,"key:"+e.Key);
                if(target==12)OpenOptions();
                // Gameplay keys were already delivered on their original down/up edges.
                // Never replay them when this delayed navigation queue drains.
                continue;
            }
            if(inputMap.MappedKey(e.Key)) {
                int target=Array.IndexOf(inputMap.Bindings,"key:"+e.Key);
                if(target==12)OpenOptions();
                else {string action=ControllerNavigation.KeyForBits(1u<<PspInputMap.Bits[target]);if(action!=null)Key(action);}
                continue;
            }
            // Only non-echo window keys become keyboard/remote gameplay input.
            if(held.Contains(e.Key))acceptedKeys.Add(e.Key);
            else if(game!=null && !reportVisible && !(controller.Connected && inputMap.HasRawBindings)) {game.Buttons(PspInputMap.Keyboard(e.Key));game.Buttons(0);}
            Key(e.Key);
        }
    }
    void UpdateGameButtons(){if(game==null||reportVisible||mapperVisible||paused)return;uint mask=controllerNavigation.GameButtons(inputMap.RawHeld)&0xffff;game.Sticks(gameAxes[0],gameAxes[1],gameAxes[2],gameAxes[3]);game.Buttons(mask);}
    void ClearUiInput() {
        ps5Pads.Slots.BlockHeld();ps5Refresh=true;ps5Previous=0;Array.Clear(gameAxes,0,gameAxes.Length);
        if(game!=null)Log("CORE_INPUT before UI transition: "+game.InputSummary);
        held.Clear();acceptedKeys.Clear();pendingKeys.Clear();inputMap.ClearHeld();sticks.Clear();game?.ResetInput();
        faceState.BlockUntilNeutral();faceReader?.Resync();faceUiGeneration++;
        controllerNavigation.BlockHeld();
    }
    string InputDiagnostics()=>ps5Pads.Diagnostics+" player="+(ps5Owner+1)+" mapping="+ps5Map.Serialize();
    void OpenMapper()
    {
        mapperReturnToOptions=optionsMenu.Page!=MenuPage.None;optionsMenu.Close();reportReturnToOptions=false;
        comparison=null;mapperVisible=true;learning=learnAll=learnReady=false;mapperSelected=6;
        sticksVisible=false;stickCalibration=null;
        reportVisible=false;game?.Pause(true);ClearUiInput();
        mapperMessage="Use the TV remote to choose an action and press OK. Then press a PS5 button.";
        Log("INPUT_MAPPER_OPEN "+InputDiagnostics());
        Layout();
    }
    void Capture(string token){
        int source;if(!token.StartsWith("button:")||!int.TryParse(token.Substring(7),out source))return;
        ps5Map.Assign(mapperSelected,source);SyncPs5Map();controllerNavigation.BlockHeld();
        mapperMessage=PspInputMap.Names[mapperSelected]+" assigned to "+ps5Map.SourceName(mapperSelected)+". Release and press again to verify.";
        learning=learnAll=learnReady=false;DrawMapper();
    }
    void MapperKey(string key){
        if(learning){if(key=="XF86Back"||key=="Escape"){learning=false;mapperMessage="Assignment cancelled.";}return;}
        if(key=="XF86Back"||key=="Escape"){SaveMapper();return;}
        if(key=="Up"||key=="Left")mapperSelected=(mapperSelected+14)%15;
        else if(key=="Down"||key=="Right")mapperSelected=(mapperSelected+1)%15;
        else if(key=="Return"||key=="KP_Enter"){
            if(mapperSelected==14){SaveMapper();return;}
            if(mapperSelected==13){ps5Map.Reset();SyncPs5Map();mapperMessage="Standard PS5 layout restored.";}
            else {learning=true;learnReady=false;armAfter=ControllerReader.Now+.45;mapperMessage="Release all controls, then press the PS5 button for "+PspInputMap.Names[mapperSelected]+".";}
        }DrawMapper();
    }
    void StickKey(string key) {
        bool back=key=="XF86Back"||key=="Escape",ok=key=="Return"||key=="KP_Enter";
        if(stickCalibration!=null) {
            if(back){stickCalibration=null;stickMessage="Calibration cancelled; previous stick settings kept.";controllerNavigation.BlockHeld();}
            else if(ok) {
                string error;
                if(!stickCalibration.Confirm(sticks,out error))stickMessage=error;
                else {
                    stickMessage=null;
                    if(stickCalibration.Complete) {
                        sticks.Axes=stickCalibration.Result;stickCalibration=null;
                        stickMessage="Calibrated. Test both sticks, then Save & return.";
                        controllerNavigation.BlockHeld();Log("INPUT_STICK_CALIBRATED "+sticks.Summary);
                    }
                }
            }
        } else if(back || ok&&stickSelected==4) {
            try {sticks.Save(stickPath);Log("INPUT_STICKS_SAVED "+sticks.Summary);}
            catch(Exception e){stickMessage="Save failed: "+e.Message;DrawMapper();return;}
            sticksVisible=false;ClearUiInput();mapperMessage="Stick settings saved. Button bindings kept.";
        } else if(key=="Up"||key=="Left")stickSelected=(stickSelected+4)%5;
        else if(key=="Down"||key=="Right")stickSelected=(stickSelected+1)%5;
        else if(ok) {
            if(stickSelected==0){stickCalibration=new StickCalibration();stickMessage=null;controllerNavigation.BlockHeld();}
            if(stickSelected==1){int[] zones={10,16,22,28};sticks.DeadZone=zones[(Array.IndexOf(zones,sticks.DeadZone)+1)%zones.Length];}
            if(stickSelected==2)sticks.RightMode=(sticks.RightMode+1)%StickInput.RightModes.Length;
            if(stickSelected==3){sticks=new StickInput();stickMessage="DualSense defaults restored. Move both sticks to test.";}
        }
        DrawMapper();
    }
    void BeginInputAccessProbe() {
        if(accessProbeBusy)return;accessProbeBusy=true;
        mapperMessage="Checking read-only controller device access...";DrawMapper();
        new System.Threading.Thread(()=> {
            string result;try{result=InputAccessProbe.Run();}catch(Exception e){result="Controller access check failed: "+e.Message;}
            EcoreMainloop.PostAndWakeUp(()=> {
                accessProbeBusy=false;if(terminating)return;
                Log(result);mapperVisible=false;mapperView.Hide();ClearUiInput();game?.Pause(true);
                reportVisible=true;reportReturnToOptions=mapperReturnToOptions;reportPage=0;reportLines=GraphicsSessionLog.Wrap(new[]{result},reportColumns).ToArray();Layout();
            });
        }){IsBackground=true,Name="Controller access probe"}.Start();
    }
    void SaveMapper()
    {
        try {
            string temp=mappingPath+".tmp";File.WriteAllText(temp,ps5Map.Serialize());
            if(File.Exists(mappingPath))File.Replace(temp,mappingPath,mappingPath+".bak");else File.Move(temp,mappingPath);
            Log("INPUT_MAP_SAVED "+mappingPath);
        } catch(Exception e) {mapperMessage="Save failed: "+e.Message;DrawMapper();return;}
        mapperVisible=false;learning=learnAll=false;mapperView.Hide();
        ClearUiInput();
        if(mapperReturnToOptions){mapperReturnToOptions=false;OpenOptions();}
        else {game?.Pause(false);Layout();}
    }
    void DrawMapper(){if(!mapperVisible)return;lastDraw=ControllerReader.Now;mapperView.Draw(inputMap,mapperSelected,learning,mapperMessage,ps5Owner<0?"Connect a PS5 controller":"PS5 controller connected - standard layout","PS5 controller",true,inputMap.RawHeld);}
}
