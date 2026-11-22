using System;

internal enum MenuPage { None, Library, Pause, Help, ConfirmExit }
internal enum MenuCommand { None, Filter, Sort, Audio, Controls, Scan, Report, Export, Resume, Exit, ToggleStats, Live }

// Navigation only. Pausing, input release, rendering and disk writes stay in the UI host.
internal sealed class FrontendMenu
{
    internal MenuPage Page {get;private set;}
    internal int Selected {get;private set;}
    internal int Direction {get;private set;}=1;
    MenuPage parent;
    int parentSelected;
    bool playing;
    internal void Open(bool activeGame) {playing=activeGame;Page=activeGame?MenuPage.Pause:MenuPage.Library;Selected=0;}
    internal void Close(){Page=MenuPage.None;Selected=0;}
    void Child(MenuPage page){parent=Page;parentSelected=Selected;Page=page;Selected=0;}
    void Return(){Page=parent;Selected=parentSelected;}
    internal void AskExit(bool activeGame){playing=activeGame;parent=MenuPage.None;parentSelected=0;Page=MenuPage.ConfirmExit;Selected=0;}
    internal MenuCommand Key(string key) {
        bool back=key=="XF86Back"||key=="Escape",ok=key=="Return"||key=="KP_Enter";
        if(Page==MenuPage.None)return MenuCommand.None;
        if(back) {
            if(Page==MenuPage.Help || Page==MenuPage.ConfirmExit){Return();return MenuCommand.None;}
            Close();return playing?MenuCommand.Resume:MenuCommand.None;
        }
        if(Page==MenuPage.Help){if(ok)Return();return MenuCommand.None;}
        int count=Page==MenuPage.Library?9:Page==MenuPage.Pause?8:2;
        if(key=="Up")Selected=(Selected+count-1)%count;
        if(key=="Down")Selected=(Selected+1)%count;
        Direction=key=="Left"?-1:1;
        if(Page==MenuPage.Library && Selected<3 && (key=="Left"||key=="Right"||ok))
            return new[]{MenuCommand.Filter,MenuCommand.Sort,MenuCommand.Audio}[Selected];
        if(!ok)return MenuCommand.None;
        if(Page==MenuPage.ConfirmExit) {
            if(Selected==1)return MenuCommand.Exit;
            Return();return MenuCommand.None;
        }
        if(Page==MenuPage.Library) {
            if(Selected==3)return MenuCommand.Controls;
            if(Selected==4)return MenuCommand.Scan;
            if(Selected==5){Child(MenuPage.Help);return MenuCommand.None;}
            if(Selected==6)return MenuCommand.Report;
            if(Selected==7)return MenuCommand.Live;
            Close();return MenuCommand.None;
        }
        if(Selected==0){Close();return MenuCommand.Resume;}
        if(Selected==1)return MenuCommand.Controls;
        if(Selected==2)return MenuCommand.ToggleStats;
        if(Selected==3)return MenuCommand.Report;
        if(Selected==4)return MenuCommand.Export;
        if(Selected==5){Child(MenuPage.Help);return MenuCommand.None;}
        if(Selected==6)return MenuCommand.Live;
        Child(MenuPage.ConfirmExit);return MenuCommand.None;
    }
}
