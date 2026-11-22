using System;
using System.IO;
using ElmSharp;
using Tizen.Applications;

internal sealed partial class TizenPspApp
{
    ButtonIdentityCheck buttonCheck;
    EcoreEvent<EcoreKeyEventArgs> checkKeyDown,checkKeyUp;
    string buttonAccess="",buttonKeyStatus="";
    int buttonCheckGeneration;
    void BeginButtonCheck() {
        comparison=null;learning=learnAll=false;ClearUiInput();
        int generation=++buttonCheckGeneration;
        buttonAccess="Raw device metadata check pending.";
        buttonCheck=new ButtonIdentityCheck(ControllerReader.Now);controller.CaptureAllEvents=true;
        buttonKeyStatus="Ecore key-code observer enabled";
        try {
            checkKeyDown=new EcoreEvent<EcoreKeyEventArgs>(EcoreEventType.KeyDown,EcoreKeyEventArgs.Create);
            checkKeyUp=new EcoreEvent<EcoreKeyEventArgs>(EcoreEventType.KeyUp,EcoreKeyEventArgs.Create);
            checkKeyDown.On+=(s,e)=>buttonCheck?.Key("ecore",e.KeyName,e.KeyCode,true,ControllerReader.Now);
            checkKeyUp.On+=(s,e)=>buttonCheck?.Key("ecore",e.KeyName,e.KeyCode,false,ControllerReader.Now);
        }catch(Exception e){buttonKeyStatus="Ecore key-code observer unavailable: "+e.Message;DisposeButtonObservers();}
        // Normal permission context, metadata only; never read/grab competing input.
        new System.Threading.Thread(()=> {
            string result;try{result=InputAccessProbe.Run();}catch(Exception e){result="Device metadata check failed: "+e.Message;}
            EcoreMainloop.PostAndWakeUp(()=> {
                if(terminating||generation!=buttonCheckGeneration)return;
                buttonAccess=result;Log(result);
                if(buttonCheck==null)SaveButtonEvidence(lastButtonEvidence+"\n"+result);
            });
        }){IsBackground=true,Name="Button identity metadata"}.Start();
        DrawMapper();
    }
    string lastButtonEvidence;
    void SaveButtonEvidence(string report) {
        try{DiagnosticFiles.DurableWrite(Path.Combine(Application.Current.DirectoryInfo.Data,"button-identity-check.txt"),report);}
        catch(Exception e){Log("BUTTON_CHECK_SAVE_FAILED "+e.Message);}
    }
    void DisposeButtonObservers(){checkKeyDown?.Dispose();checkKeyUp?.Dispose();checkKeyDown=checkKeyUp=null;}
    void FinishButtonCheck(string reason=null) {
        if(buttonCheck==null)return;
        if(reason!=null)buttonCheck.Abort(reason);
        string result=buttonCheck.Result;
        lastButtonEvidence=session.Context+"\n"+buttonKeyStatus+"\n"+controller.Diagnostics+"\n"+(faceReader?.Diagnostics??"")+"\n"+buttonCheck.Report;
        SaveButtonEvidence(lastButtonEvidence+"\n"+buttonAccess);Log("BUTTON_CHECK_RESULT "+result);
        controller.CaptureAllEvents=false;buttonCheck=null;DisposeButtonObservers();ClearUiInput();
        mapperMessage=result+" Info opens diagnostics; Down exports the saved check.";
        DrawMapper();
    }
}
