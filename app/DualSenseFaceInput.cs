using System;
using System.IO;
using Tizen.Applications;

internal sealed partial class TizenPspApp
{
    readonly DualSenseFaceState faceState=new DualSenseFaceState();
    DualSenseHidReader faceReader;
    bool faceFixEnabled=true;
    int faceUiGeneration;
    string faceOptionPath;
    void InitializeFaces() {
        faceOptionPath=Path.Combine(Application.Current.DirectoryInfo.Data,"dualsense-faces.txt");
        try{if(File.Exists(faceOptionPath))faceFixEnabled=File.ReadAllText(faceOptionPath).Trim()!="off";}catch(Exception e){Log("DUALSENSE_OPTION "+e.Message);}
        faceReader=new DualSenseHidReader();faceReader.Start();
    }
    void PumpFaces() {
        // Evidence is specific to this single-pad profile. Other maps/devices retain
        // the original path. A verified packet is required before overriding code 2.
        faceReader.Allowed=faceFixEnabled && controller.Connected && controller.Activity.SingleFirstSlot &&
            inputMap.Bindings[3]=="button:2" && inputMap.Bindings[1]=="button:13";
        int generation=faceUiGeneration;
        foreach(var e in faceReader.Drain()) {
            uint previous=faceState.Held;
            bool changed=faceState.Apply(e.Active,e.Faces);
            if(changed) {
                game?.ResetInput();inputMap.ClearRaw();controllerNavigation.Reset();
                Log("DUALSENSE_FACE_ROUTE active="+e.Active+" "+faceReader.Diagnostics);
            }
            controllerNavigation.ObserveFaces(faceState.Active,faceState.Held);
            if(changed)controllerNavigation.BlockHeld();
            if(previous!=faceState.Held){rawTimes.Enqueue(e.Time);while(rawTimes.Count>512)rawTimes.Dequeue();}
            buttonCheck?.Faces(e.Active,e.Faces,e.Time);
            NavigateController(e.Time);UpdateGameButtons();
            if(generation!=faceUiGeneration)break; // discard taps queued before a menu change
        }
    }
    void ResetFaces() {
        ps5Pads.Enabled=false;
        if(faceReader!=null)faceReader.Allowed=false;
        faceState.Apply(false,0);controllerNavigation.ObserveFaces(false,0);
    }
    void ToggleFaces() {
        bool enabled=!faceFixEnabled;
        try{File.WriteAllText(faceOptionPath,enabled?"auto":"off");}
        catch(Exception e){mapperMessage="Could not save PS5 face setting: "+e.Message;return;}
        faceFixEnabled=enabled;ResetFaces();ClearUiInput();
        mapperMessage=enabled?"PS5 face fix Auto: waits for verified DualSense reports. R3 stays available.":"PS5 face fix Off: original TV input and R3 workaround restored.";
    }
}
