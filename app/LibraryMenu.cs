using System;
using System.IO;
using System.Linq;
using ElmSharp;
using Tizen.Applications;

internal sealed partial class TizenPspApp
{
    LibraryView libraryView;
    LibraryCatalog catalog;
    FastMemoryPreferences fastMemoryPreferences;
    string fastMemoryPath;
    LibraryItem[] allItems=Array.Empty<LibraryItem>(),visibleItems=Array.Empty<LibraryItem>();
    LibraryFilter libraryFilter;
    string libraryPath;
    bool gameDetails;
    int libraryAction;
    readonly int[] audioBuffers={0,60,120,180},gameAudioBuffers={-1,0,60,120,180};
    int audioChoice=2;
    LibraryItem SelectedItem=>selected>=0&&selected<visibleItems.Length?visibleItems[selected]:null;
    GamePreference SelectedPreference=>SelectedItem==null?new GamePreference():catalog.Get(SelectedItem);
    bool EffectiveArmJit=>SelectedPreference.UseArmJit;
    bool EffectiveFastMemory=>EffectiveArmJit&&fastMemoryPreferences.Enabled(SelectedItem);
    int EffectiveResolutionScale=>SelectedPreference.ResolutionScale;
    int EffectiveFrameSkip=>SelectedPreference.FrameSkip;
    bool EffectiveSkipBufferEffects=>SelectedPreference.SkipBufferEffects;
    string CpuLabel=>EffectiveFastMemory?"ARM JIT (fast memory)":SelectedPreference.Cpu==GameCpuChoice.Default?"Default (ARM JIT)":EffectiveArmJit?"ARM JIT":"IR interpreter";
    string ResolutionLabel=>EffectiveResolutionScale==2?"2x - 960 x 544":"1x - 480 x 272";
    string RenderingLabel=>EffectiveFrameSkip==0?"Full frames":"Frameskip 1 (less smooth)";
    string EffectsLabel=>!EffectiveArmJit?"Normal (ARM JIT required to skip)":EffectiveSkipBufferEffects?"Skip buffer effects (may hide graphics)":"Normal";
    static string BufferLabel(int value)=>value==0?"Muted":value+" ms buffer";
    string AudioLabel=>BufferLabel(audioBuffers[audioChoice]);
    int SelectedAudio=>SelectedItem==null?-1:catalog.Get(SelectedItem).AudioBuffer;
    int EffectiveAudio=>SelectedAudio<0?audioBuffers[audioChoice]:SelectedAudio;
    string GameAudioLabel=>SelectedAudio<0?"Default ("+AudioLabel+")":BufferLabel(SelectedAudio);
    string SortLabel=>catalog.Sort==LibrarySort.Title?"Title A-Z":catalog.Sort==LibrarySort.TitleDescending?"Title Z-A":"Last played";
    void LoadLibraryPreferences() {
        libraryPath=Path.Combine(Application.Current.DirectoryInfo.Data,"library-v1.tsv");
        catalog=LibraryCatalog.Load(libraryPath,Log);
        fastMemoryPath=Path.Combine(Application.Current.DirectoryInfo.Data,"jit-fast-memory-v1.txt");
        fastMemoryPreferences=FastMemoryPreferences.Load(fastMemoryPath,Log);
    }
    bool SaveLibraryPreferences() {
        try{catalog.Save(libraryPath);fastMemoryPreferences.Save(fastMemoryPath);return true;}
        catch(Exception e){status="Library preferences could not be saved: "+e.Message;Log("LIBRARY_SAVE "+e);return false;}
    }
    void LoadAudioChoice() {
        try {int value;if(int.TryParse(File.ReadAllText(Path.Combine(Application.Current.DirectoryInfo.Data,"audio-buffer-ms.txt")),out value)) {
            int choice=Array.IndexOf(audioBuffers,value);if(choice>=0)audioChoice=choice;
        }}catch(IOException){}catch(UnauthorizedAccessException){}
    }
    void ChangeAudioChoice(int direction) {
        audioChoice=(audioChoice+audioBuffers.Length+direction)%audioBuffers.Length;
        try {
            string path=Path.Combine(Application.Current.DirectoryInfo.Data,"audio-buffer-ms.txt"),temp=path+".tmp";
            File.WriteAllText(temp,audioBuffers[audioChoice].ToString());
            if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);
            status="Default sound saved. Individual game overrides take priority.";
        }catch(Exception e){status="Sound preference could not be saved: "+e.Message;Log("AUDIO_SETTINGS_SAVE "+e.Message);}
    }
    void ChangeGameAudio(int direction) {
        if(SelectedItem==null)return;
        int next=(Array.IndexOf(gameAudioBuffers,SelectedAudio)+gameAudioBuffers.Length+direction)%gameAudioBuffers.Length;
        try{catalog.SetAudio(SelectedItem,gameAudioBuffers[next]);status="Sound preference applies to this game on its next launch.";SaveLibraryPreferences();}
        catch(Exception e){status=e.Message;Log("LIBRARY_AUDIO "+e.Message);}
    }
    void ChangeGameCpu(int direction) {
        if(SelectedItem==null)return;
        int current=EffectiveFastMemory?2:SelectedPreference.Cpu==GameCpuChoice.Interpreter?3:(int)SelectedPreference.Cpu;
        int next=(current+4+direction)%4;
        catalog.SetCpu(SelectedItem,next==3?GameCpuChoice.Interpreter:next==0?GameCpuChoice.Default:GameCpuChoice.ArmJit);
        fastMemoryPreferences.Set(SelectedItem,next==2);
        status=EffectiveFastMemory?"Fast memory reduces CPU checks. If a game crashes, select ARM JIT. Relaunch to apply.":EffectiveArmJit?"ARM JIT with checked memory saved. Relaunch to apply.":"IR interpreter saved: slower compatibility mode without movie decoding. Applies on next launch.";SaveLibraryPreferences();
    }
    void ChangeGameResolution() {
        if(SelectedItem==null)return;
        catalog.SetResolution(SelectedItem,EffectiveResolutionScale==1?2:1);
        status=EffectiveResolutionScale==2?"2x is sharper but draws four times as many pixels. Relaunch to apply.":"Native 1x uses less GPU work. Relaunch to apply.";SaveLibraryPreferences();
    }
    void ChangeGameRendering() {
        if(SelectedItem==null)return;
        catalog.SetFrameSkip(SelectedItem,EffectiveFrameSkip==0?1:0);
        status=EffectiveFrameSkip==0?"Full rendering restored for next launch.":"Frameskip 1 reduces drawing with less smooth motion. Relaunch to apply.";SaveLibraryPreferences();
    }
    void ChangeGameEffects() {
        if(SelectedItem==null)return;
        if(!EffectiveArmJit) {status="Skip buffer effects requires ARM JIT. IR interpreter uses Normal effects.";return;}
        catalog.SetSkipBufferEffects(SelectedItem,!EffectiveSkipBufferEffects);
        status=EffectiveSkipBufferEffects?"Effect skipping saved for this game. Graphics may disappear or flicker; switch Effects to Normal to restore. Applies on next launch.":"Normal buffer effects restored for this game on its next launch.";
        SaveLibraryPreferences();
    }
    void ApplyLibraryView(string previous=null) {
        visibleItems=catalog.Select(allItems,libraryFilter);games=visibleItems.Select(x=>x.Path).ToArray();
        selected=Math.Max(0,Array.IndexOf(games,previous));
        libraryFrom=libraryTo=selected;libraryStarted=ControllerReader.Now;SelectionStatus();
    }
    void ToggleFavorite() {
        if(SelectedItem==null)return;
        string previous=SelectedItem.Path;
        try {
            catalog.ToggleFavorite(SelectedItem);ApplyLibraryView(previous);
            if(Array.IndexOf(games,previous)<0)gameDetails=false;
            SaveLibraryPreferences();
        }catch(Exception e){status=e.Message;Log("LIBRARY_FAVORITE "+e.Message);}
    }
    string DetailsMetadata {
        get {
            if(SelectedItem==null)return "";
            var item=SelectedItem;var p=catalog.Get(item);
            string size=item.Bytes<0?"Size unavailable":(item.Bytes/(1024.0*1024)).ToString("N0")+" MiB";
            string recent=p.LastPlayedUtcTicks==0?"Not played yet":"Last played: "+new DateTime(p.LastPlayedUtcTicks,DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            return size+(p.Favorite?"  /  FAVORITE":"")+"\n"+recent+"\n"+ResolutionLabel+"  /  PSP widescreen";
        }
    }
    double libraryFrom,libraryTo,libraryStarted;
    IntPtr libraryTimer;
    double LibraryPosition {
        get {double t=Math.Max(0,Math.Min(1,(ControllerReader.Now-libraryStarted)/.20));return libraryFrom+(libraryTo-libraryFrom)*(1-Math.Pow(1-t,3));}
    }
    void EnsureLibraryView(){if(libraryView==null)libraryView=new LibraryView(window,Application.Current.DirectoryInfo.Resource);}
    void DrawLibrary() {
        EnsureLibraryView();
        string[] actions={"Play game","Controller settings","Sound: "+GameAudioLabel,"CPU: "+CpuLabel,"Resolution: "+ResolutionLabel,"Rendering: "+RenderingLabel,"Effects: "+EffectsLabel,
            SelectedItem!=null&&catalog.Get(SelectedItem).Favorite?"Remove from Favorites":"Add to Favorites","Back to collection"};
        string empty=allItems.Length==0?"Connect your USB with extracted games in the PSP folder.\nDown scans again. Up opens library options.":
            libraryFilter==LibraryFilter.Favorites?"Add a game to Favorites from its details.\nUp opens options to return to All games.":"Games appear here after their first video frame.\nUp opens options to return to All games.";
        libraryView.Draw(games,selected,LibraryPosition,gameDetails,libraryAction,status,actions,
            libraryFilter.ToString().ToUpperInvariant()+"  /  "+(libraryFilter==LibraryFilter.Recent?"LAST PLAYED":SortLabel.ToUpperInvariant()),empty,DetailsMetadata);
    }
    void LibraryKey(string key) {
        if(key=="XF86Back"||key=="Escape") {
            if(gameDetails){gameDetails=false;ShowText();}else AskExit();return;
        }
        if(key=="XF86Info"||key=="i"||key=="F1") {OpenReport(false);return;}
        if(gameDetails) {
            if(key=="Up")libraryAction=(libraryAction+8)%9;
            else if(key=="Down")libraryAction=(libraryAction+1)%9;
            else if(libraryAction==2 && (key=="Left"||key=="Right"))ChangeGameAudio(key=="Left"?-1:1);
            else if(libraryAction==3 && (key=="Left"||key=="Right"))ChangeGameCpu(key=="Left"?-1:1);
            else if(libraryAction==4 && (key=="Left"||key=="Right"))ChangeGameResolution();
            else if(libraryAction==5 && (key=="Left"||key=="Right"))ChangeGameRendering();
            else if(libraryAction==6 && (key=="Left"||key=="Right"))ChangeGameEffects();
            else if(key=="Return"||key=="KP_Enter") {
                if(libraryAction==0){libraryView.Hide();StartSelectedGame();return;}
                if(libraryAction==1){OpenMapper();return;}
                if(libraryAction==2)ChangeGameAudio(1);
                else if(libraryAction==3)ChangeGameCpu(1);
                else if(libraryAction==4)ChangeGameResolution();
                else if(libraryAction==5)ChangeGameRendering();
                else if(libraryAction==6)ChangeGameEffects();
                else if(libraryAction==7)ToggleFavorite();
                else gameDetails=false;
            }
            ShowText();return;
        }
        if(key=="Up"){OpenOptions();return;}
        if(key=="Down"){ScanGames();return;}
        if(games.Length==0)return;
        if(key=="Return"||key=="KP_Enter"){gameDetails=true;libraryAction=0;ShowText();return;}
        if(key=="Left"||key=="Right") {
            libraryFrom=LibraryPosition;selected=Math.Max(0,Math.Min(games.Length-1,selected+(key=="Left"?-1:1)));
            libraryTo=selected;libraryStarted=ControllerReader.Now;SelectionStatus();
            if(libraryTimer==IntPtr.Zero)libraryTimer=EcoreMainloop.AddTimer(1.0/30,()=> {
                if(paused||terminating||!selecting||mapperVisible||reportVisible||gameDetails){libraryTimer=IntPtr.Zero;return false;}
                ShowText();if(ControllerReader.Now-libraryStarted>=.2){libraryTimer=IntPtr.Zero;return false;}return true;
            });
            ShowText();
        }
    }
}
