using System;

internal sealed partial class TizenPspApp
{
    readonly FrontendMenu optionsMenu=new FrontendMenu();
    bool reportReturnToOptions,mapperReturnToOptions;
    void OpenOptions() {
        optionsMenu.Open(game!=null);reportVisible=true;ClearUiInput();game?.Pause(true);Layout();
    }
    void AskExit() {
        optionsMenu.AskExit(game!=null);reportVisible=true;ClearUiInput();game?.Pause(true);Layout();
    }
    void OpenReport(bool returnToOptions) {
        optionsMenu.Close();reportReturnToOptions=returnToOptions;reportVisible=true;
        ClearUiInput();game?.Pause(true);reportPage=0;reportLines=SnapshotReport();Layout();
    }
    void CloseReport() {
        if(reportReturnToOptions){OpenOptions();return;}
        reportVisible=false;ClearUiInput();game?.Pause(false);Layout();
    }
    void OptionsKey(string key) {
        var command=optionsMenu.Key(key);
        string previous=SelectedItem?.Path;
        switch(command) {
            case MenuCommand.Filter:
                libraryFilter=(LibraryFilter)(((int)libraryFilter+3+optionsMenu.Direction)%3);gameDetails=false;ApplyLibraryView(previous);break;
            case MenuCommand.Sort:
                catalog.Sort=(LibrarySort)(((int)catalog.Sort+3+optionsMenu.Direction)%3);ApplyLibraryView(previous);SaveLibraryPreferences();break;
            case MenuCommand.Audio:ChangeAudioChoice(optionsMenu.Direction);break;
            case MenuCommand.ToggleStats:TogglePerformanceOverlay();break;
            case MenuCommand.Controls:OpenMapper();return;
            case MenuCommand.Scan:optionsMenu.Close();ScanGames();return;
            case MenuCommand.Report:OpenReport(true);return;
            case MenuCommand.Export:OpenReport(true);ExportDiagnostics();return;
            case MenuCommand.Live:ToggleLiveConnection();break;
            case MenuCommand.Exit:terminating=true;Log("LIFECYCLE confirmed exit");Exit();return;
        }
        if(optionsMenu.Page==MenuPage.None) {reportVisible=false;ClearUiInput();game?.Pause(false);}
        Layout();
    }
    void DrawOptions() {
        EnsureLibraryView();
        string title,description;string[] actions;
        if(optionsMenu.Page==MenuPage.Library) {
            title="Library options";description="Choose a collection, change its order, or set the default sound buffer.";
            actions=new[]{"Collection: "+libraryFilter,"Sort: "+SortLabel+(libraryFilter==LibraryFilter.Recent?" (Recent always uses last played)":""),
                "Default sound: "+AudioLabel,"Controller settings","Rescan USB","Help & controls","Diagnostics",LiveLabel,"Back to collection"};
        } else if(optionsMenu.Page==MenuPage.Pause) {
            title="Game paused";description=(SelectedItem?.Title??"PSP game")+"\nResume continues the current session.";
            actions=new[]{"Resume game","Controller settings",PerformanceOverlayLabel,"Diagnostics","Export logs to USB","Help & controls",LiveLabel,"Exit app..."};
        } else if(optionsMenu.Page==MenuPage.ConfirmExit) {
            title="Exit TizenPPSSPP?";description=game==null?"Your library preferences are kept.":"Current gameplay will end. Progress is not saved automatically.\nCancel, then Resume to save through the game's own menu first.";
            actions=new[]{"Cancel","Exit app"};
        } else {
            title="Help & controls";description="TizenPPSSPP "+AppVersion.Current;actions=Array.Empty<string>();
        }
        libraryView.DrawMenu(title,description,actions,optionsMenu.Selected,optionsMenu.Page==MenuPage.Help,liveNotice??(live==null?(optionsMenu.Page==MenuPage.Library?status:""):live.Status+" | "+experiment.Summary));
    }
}
