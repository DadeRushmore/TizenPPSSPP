using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ElmSharp;

// Retained Evas objects. Artwork is decoded only on selection, never during gameplay.
internal sealed class LibraryView
{
    readonly Window window;
    readonly string resources;
    readonly List<Label> labels=new List<Label>();
    readonly List<IntPtr> panels=new List<IntPtr>();
    int usedLabels,usedPanels;double scale;int ox,oy;
    bool visible;
    [DllImport("libevas.so.1")]static extern IntPtr evas_object_rectangle_add(IntPtr canvas);
    [DllImport("libevas.so.1")]static extern void evas_object_color_set(IntPtr obj,int r,int g,int b,int a);
    [DllImport("libevas.so.1")]static extern void evas_object_layer_set(IntPtr obj,short layer);
    [DllImport("libevas.so.1")]static extern void evas_object_image_file_set(IntPtr obj,string path,string key);
    [DllImport("libevas.so.1")]static extern int evas_object_image_load_error_get(IntPtr obj);
    [DllImport("libevas.so.1")]static extern void evas_object_image_load_size_set(IntPtr obj,int w,int h);
    [DllImport("libevas.so.1")]static extern void evas_object_image_size_get(IntPtr obj,out int w,out int h);
    internal LibraryView(Window window,string resourceDirectory=null){this.window=window;resources=resourceDirectory;}
    void Panel(int x,int y,int w,int h,int r,int g,int b,int alpha=255) {
        IntPtr p;if(usedPanels==panels.Count){p=evas_object_rectangle_add(Screen.evas_object_evas_get(window.Handle));panels.Add(p);evas_object_layer_set(p,25);}else p=panels[usedPanels];usedPanels++;
        Screen.evas_object_move(p,ox+(int)(x*scale),oy+(int)(y*scale));Screen.evas_object_resize(p,(int)(w*scale),(int)(h*scale));
        evas_object_color_set(p,r*alpha/255,g*alpha/255,b*alpha/255,alpha);Screen.evas_object_show(p);
    }
    void Text(string text,int x,int y,int w,int h,int size,string color,bool centered=false) {
        Label l;if(usedLabels==labels.Count){l=new Label(window){LineWrapType=WrapType.Mixed};l.Layer=30;labels.Add(l);}else l=labels[usedLabels];usedLabels++;
        l.Geometry=new Rect(ox+(int)(x*scale),oy+(int)(y*scale),(int)(w*scale),(int)(h*scale));
        l.Text=(centered?"<align=center>":"")+"<font_size="+Math.Max(12,(int)(size*scale))+"><color="+color+">"+text.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace("\n","<br/>")+"</color></font_size>"+(centered?"</align>":"");l.Show();
    }
    readonly IntPtr[] backgrounds=new IntPtr[2];
    int front=-1,behind=-1;
    string backgroundPath,lastGame;
    IntPtr fadeTimer;
    double fadeStarted;
    LibraryTheme theme=LibraryTheme.ForGame(null);
    static double Now=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
    void BackgroundGeometry(IntPtr p) {
        int iw,ih;evas_object_image_size_get(p,out iw,out ih);
        double fill=iw>0&&ih>0?Math.Max(1920.0/iw,1080.0/ih):1;
        int w=iw>0?(int)(iw*fill):1920,h=ih>0?(int)(ih*fill):1080;
        Screen.evas_object_move(p,ox+(int)((1920-w)*scale/2),oy+(int)((1080-h)*scale/2));
        Screen.evas_object_resize(p,Math.Max(1,(int)(w*scale)),Math.Max(1,(int)(h*scale)));
    }
    void StopFade() {if(fadeTimer!=IntPtr.Zero){EcoreMainloop.RemoveTimer(fadeTimer);fadeTimer=IntPtr.Zero;}}
    void FreeBackground(int index) {
        if(index<0||backgrounds[index]==IntPtr.Zero)return;
        Screen.evas_object_hide(backgrounds[index]);evas_object_image_file_set(backgrounds[index],null,null);
    }
    void SetBackground(string path,string fallback) {
        if(path==backgroundPath&&front>=0){BackgroundGeometry(backgrounds[front]);if(behind>=0)BackgroundGeometry(backgrounds[behind]);return;}
        StopFade();
        int next=front==0?1:0;IntPtr p=backgrounds[next];
        if(p==IntPtr.Zero){p=backgrounds[next]=Screen.evas_object_image_filled_add(Screen.evas_object_evas_get(window.Handle));evas_object_image_load_size_set(p,1280,720);}
        evas_object_image_file_set(p,path,null);
        if(path==null||evas_object_image_load_error_get(p)!=0) {
            evas_object_image_file_set(p,fallback,null);
            if(fallback==null||evas_object_image_load_error_get(p)!=0){FreeBackground(next);return;}
        }
        behind=front;front=next;backgroundPath=path;BackgroundGeometry(p);evas_object_layer_set(p,24);
        if(behind>=0) {
            evas_object_layer_set(backgrounds[behind],23);evas_object_color_set(backgrounds[behind],255,255,255,255);
            evas_object_color_set(p,0,0,0,0);Screen.evas_object_show(p);fadeStarted=Now;
            fadeTimer=EcoreMainloop.AddTimer(1.0/30,()=> {
                if(!visible){fadeTimer=IntPtr.Zero;return false;}
                double t=Math.Max(0,Math.Min(1,(Now-fadeStarted)/.28));int a=(int)(255*t*t*(3-2*t));
                evas_object_color_set(backgrounds[front],a,a,a,a);
                if(t>=1){FreeBackground(behind);behind=-1;fadeTimer=IntPtr.Zero;return false;}return true;
            });
        } else {evas_object_color_set(p,255,255,255,255);Screen.evas_object_show(p);}
    }
    internal void Hide(){
        if(!visible)return;visible=false;StopFade();
        foreach(var l in labels)l.Hide();foreach(var p in panels)Screen.evas_object_hide(p);
        for(int i=0;i<pictures.Count;i++){Screen.evas_object_hide(pictures[i]);evas_object_image_file_set(pictures[i],null,null);picturePaths[i]=null;pictureValid[i]=false;}
        FreeBackground(0);FreeBackground(1);front=behind=-1;backgroundPath=null;
    }
    readonly List<IntPtr> pictures=new List<IntPtr>();
    readonly List<string> picturePaths=new List<string>();
    readonly List<bool> pictureValid=new List<bool>();
    int usedPictures;
    readonly Dictionary<string,string> covers=new Dictionary<string,string>(),backdrops=new Dictionary<string,string>();
    internal void RefreshArtwork(){covers.Clear();backdrops.Clear();backgroundPath=null;for(int i=0;i<picturePaths.Count;i++)picturePaths[i]=null;}
    string CachedCover(string file){string path;if(!covers.TryGetValue(file,out path)){path=Cover(file);covers[file]=path;}return path;}
    string CachedBackdrop(string file){string path;if(!backdrops.TryGetValue(file??"",out path)){path=theme.Background(file,resources);backdrops[file??""]=path;}return path;}
    internal static string Title(string file)=>LibraryTheme.ForGame(file).Title.Replace('\n',' ');
    internal static string Cover(string file) {
        foreach(string extension in new[]{".png",".jpg",".jpeg"}) {string p=Path.ChangeExtension(file,extension);if(LibraryTheme.UsableFile(p))return p;}return null;
    }
    bool Picture(string path,int x,int y,int w,int h) {
        if(path==null)return false;
        int i=usedPictures++;IntPtr p;
        if(i==pictures.Count){p=Screen.evas_object_image_filled_add(Screen.evas_object_evas_get(window.Handle));pictures.Add(p);picturePaths.Add(null);pictureValid.Add(false);evas_object_layer_set(p,26);evas_object_image_load_size_set(p,640,640);}else p=pictures[i];
        if(picturePaths[i]!=path){evas_object_image_file_set(p,path,null);picturePaths[i]=path;pictureValid[i]=evas_object_image_load_error_get(p)==0;}
        if(!pictureValid[i]){Screen.evas_object_hide(p);return false;}
        int iw,ih;evas_object_image_size_get(p,out iw,out ih);if(iw<=0||ih<=0){Screen.evas_object_hide(p);return false;}
        double fit=Math.Min(w/(double)iw,h/(double)ih);int pw=(int)(iw*fit),ph=(int)(ih*fit);
        Screen.evas_object_move(p,ox+(int)((x+(w-pw)/2)*scale),oy+(int)((y+(h-ph)/2)*scale));
        Screen.evas_object_resize(p,Math.Max(1,(int)(pw*scale)),Math.Max(1,(int)(ph*scale)));Screen.evas_object_show(p);return true;
    }
    void Card(string file,int x,int y,int w,int h,bool focus) {
        Panel(x-5,y-5,w+10,h+10,focus?theme.R:31,focus?theme.G:42,focus?theme.B:60,focus?255:230);
        Panel(x,y,w,h,8,13,22,245);
        if(!Picture(CachedCover(file),x+5,y+5,w-10,h-10)) {
            Text("PSP",x+14,y+18,w-26,32,21,theme.Accent);
            Text(Title(file),x+14,y+70,w-28,h-76,Math.Max(16,w/12),"#edf3f9");
        }
    }
    void BeginFrame() {
        var g=window.Geometry;scale=Math.Min(Math.Max(320,g.Width)/1920.0,Math.Max(240,g.Height)/1080.0);ox=(g.Width-(int)(1920*scale))/2;oy=(g.Height-(int)(1080*scale))/2;
        usedLabels=usedPanels=usedPictures=0;visible=true;
    }
    void EndFrame() {
        for(int i=usedLabels;i<labels.Count;i++)labels[i].Hide();for(int i=usedPanels;i<panels.Count;i++)Screen.evas_object_hide(panels[i]);for(int i=usedPictures;i<pictures.Count;i++)Screen.evas_object_hide(pictures[i]);
    }
    void Header(string label) {
        Panel(0,0,1920,180,5,9,20,90);Panel(100,66,3,58,190,201,214);
        Text("TizenPPSSPP",124,51,800,90,54,"#f4f8fc");
        Text(label,100,151,1510,36,19,"#a7bbcc");Text(AppVersion.Current,1686,83,150,35,19,"#a7bbcc");
    }
    void Footer(string first,string second,string status) {
        Panel(0,946,1920,134,5,9,18,225);Panel(100,947,1720,1,53,70,88,160);
        if(!string.IsNullOrEmpty(status))Text(status,100,959,1720,32,17,"#8ba2b8");
        Text(first+"\n"+second,100,997,1720,70,21,"#c0d0df");
    }
    internal void Draw(string[] games,int selected,double position,bool details,int action,string status,string[] actions,string collection,string empty,string metadata) {
        BeginFrame();lastGame=games.Length==0?null:games[selected];theme=LibraryTheme.ForGame(lastGame);
        SetBackground(CachedBackdrop(lastGame),theme.Bundled(resources));Panel(0,0,1920,1080,4,8,17,42);
        Header(details?"COLLECTION  /  GAME SETTINGS":collection);
        if(games.Length==0) {
            Text("Make room for your\nnext adventure.",100,298,1160,225,76,"#f3f7fb");
            Text(empty,108,572,1030,150,28,"#b7cada");
        } else if(details) {
            Panel(70,207,1780,725,6,12,24,205);
            Text(theme.Title.Replace('\n',' '),100,222,1710,110,43,"#f3f7fb");
            Card(lastGame,108,365,318,452,true);
            Text(metadata,105,839,470,91,21,"#a9bdce");
            int actionStep=actions.Length>8?62:70;
            for(int i=0;i<actions.Length;i++) {
                bool focus=i==action;int y=354+i*actionStep;
                Panel(584,y,1230,actionStep-7,focus?theme.R:17,focus?theme.G:28,focus?theme.B:44,focus?235:210);
                Text((i+1).ToString("00"),605,y+15,55,actionStep-25,18,focus?"#182033":"#7890a6");
                Text(actions[i],677,y+8,1114,actionStep-14,25,focus?"#101828":"#d9e6f1");
            }
        } else {
            Panel(350,212,1220,132,5,10,20,150);
            Text(theme.Category,400,224,1120,36,21,theme.Accent,true);
            Text(theme.Title.Replace('\n',' '),400,267,1120,75,43,"#f4f8fb",true);
            position=Math.Max(0,Math.Min(games.Length-1,position));
            int first=Math.Max(0,(int)Math.Floor(position)-3),last=Math.Min(games.Length-1,(int)Math.Ceiling(position)+3);
            for(int i=first;i<=last;i++) {
                double distance=i-position,focus=Math.Max(0,1-Math.Abs(distance));
                int w=(int)(225+75*focus),h=(int)(320+106*focus),x=(int)(960+distance*344-w/2),y=586-h/2;
                if(x<65||x+w>1855)continue;
                Card(games[i],x,y,w,h,i==selected);
            }
            Panel(810,818,300,4,theme.R,theme.G,theme.B);
            Text((selected+1).ToString("00")+"  /  "+games.Length.ToString("00")+"    •    Cross / OK: game details",400,851,1120,48,24,"#d8e5ef",true);
        }
        Footer(details?"D-pad: choose / change   •   Cross / OK: select   •   Circle / Back: collection":"Left / Right: browse   •   Cross / OK: details   •   Up: options   •   Down: rescan",
            "Start + Select: menu   •   Circle uses your saved mapping (R3 if assigned)   •   Info: diagnostics",status);
        EndFrame();
    }
    internal void DrawMenu(string title,string description,string[] actions,int selected,bool help,string status) {
        BeginFrame();SetBackground(CachedBackdrop(lastGame),theme.Bundled(resources));Panel(0,0,1920,1080,4,8,17,125);Header("TIZENPPSSPP  /  "+title.ToUpperInvariant());
        Panel(70,211,1780,721,6,12,24,210);Text(title,105,232,1620,86,48,"#f2f7fb");
        Text(description,108,316,1610,86,23,"#aebfd0");
        if(help) {
            string[] topics={
                "COLLECTION  —  D-pad browses; Cross opens details. Up: options. Down: rescan USB.",
                "CONTROLS  —  Mapped Circle goes Back (R3 if assigned). Start + Select opens Pause.",
                "PERFORMANCE  —  Select + L + R toggles stats. Remote Yellow or keyboard F3 also works.",
                "CPU & GRAPHICS  —  ARM JIT is the default. Save CPU, 1x / 2x resolution and rendering per game.",
                "ARTWORK  —  Add Game.background.jpg beside Game.iso to use your own background.",
                "DIAGNOSTICS  —  Info opens reports. Down exports logs to USB. Save game progress before exiting."};
            for(int i=0;i<topics.Length;i++)Text(topics[i],109,425+i*77,1610,68,25,"#d4e1ed");
        } else {
            int step=actions.Length>8?54:actions.Length>7?61:70;
            for(int i=0;i<actions.Length;i++) {
                bool focus=i==selected;int y=423+i*step;
                Panel(105,y,1704,step-6,focus?theme.R:17,focus?theme.G:28,focus?theme.B:44,focus?235:210);
                Text(actions[i],134,y+10,1640,step-15,25,focus?"#101828":"#d9e6f1");
            }
        }
        Footer(help?"Cross / OK or Circle / Back: return":"D-pad: choose / change   •   Cross / OK: select   •   Circle / Back: return",
            "Controller and TV remote both work. Your saved bindings stay in use.",status);
        EndFrame();
    }
}
