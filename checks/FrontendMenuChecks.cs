using System;

internal static class FrontendMenuChecks
{
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    internal static void Run() {
        var menu=new FrontendMenu();menu.Open(true);
        Need(menu.Page==MenuPage.Pause && menu.Selected==0,"Pause must default to Resume");
        Need(menu.Key("XF86Back")==MenuCommand.Resume && menu.Page==MenuPage.None,"Back from pause must resume, never exit");
        menu.Open(true);menu.Key("Up");
        Need(menu.Key("Return")==MenuCommand.None && menu.Page==MenuPage.ConfirmExit && menu.Selected==0,"Exit needs a confirmation defaulting to cancel");
        Need(menu.Key("Return")==MenuCommand.None && menu.Page==MenuPage.Pause,"Repeated OK must not exit from the cancel choice");
        menu.Key("Return");menu.Key("Down");
        Need(menu.Key("Return")==MenuCommand.Exit,"Explicit confirmed exit missing");
        menu.Open(true);for(int i=0;i<5;i++)menu.Key("Down");menu.Key("Return");
        Need(menu.Page==MenuPage.Help,"Pause help unavailable");
        Need(menu.Key("Escape")==MenuCommand.None && menu.Page==MenuPage.Pause && menu.Selected==5,"Help lost its return focus");
        menu.Open(false);Need(menu.Key("Left")==MenuCommand.Filter && menu.Direction==-1,"Collection reverse navigation");
        menu.Key("Down");Need(menu.Key("Right")==MenuCommand.Sort,"Sort navigation");
        menu.Key("Down");Need(menu.Key("Return")==MenuCommand.Audio,"Default sound navigation");
        menu.Key("Down");Need(menu.Key("Return")==MenuCommand.Controls,"Controller settings inaccessible");
        menu.Key("Down");Need(menu.Key("Return")==MenuCommand.Scan,"USB rescan inaccessible");
        menu.Key("Down");menu.Key("Return");Need(menu.Page==MenuPage.Help,"Library help inaccessible");
        menu.Key("Return");menu.Key("Down");Need(menu.Key("Return")==MenuCommand.Report,"Library diagnostics inaccessible");
        menu.Key("Down");Need(menu.Key("Return")==MenuCommand.Live&&menu.Page==MenuPage.Library,"Library live toggle");
        menu.Key("Down");menu.Key("Return");Need(menu.Page==MenuPage.None,"Library return failed");
        menu.AskExit(false);menu.Key("Escape");Need(menu.Page==MenuPage.None,"Collection exit cancel failed");
        menu.Open(true);menu.Key("Down");menu.Key("Down");
        Need(menu.Key("Return")==MenuCommand.ToggleStats && menu.Page==MenuPage.Pause,"Stats toggle must keep game paused");
        menu.Key("Down");Need(menu.Key("Return")==MenuCommand.Report,"Pause report inaccessible");menu.Key("Down");
        Need(menu.Key("Return")==MenuCommand.Export,"Pause export inaccessible");
        menu.Key("Down");menu.Key("Down");Need(menu.Key("Return")==MenuCommand.Live&&menu.Page==MenuPage.Pause,"Pause live toggle");
        Console.WriteLine("PASS: pause/resume, menu wrapping and focus, help return, settings navigation, USB export and explicit exit confirmation.");
    }
}
