"""Capture the actual LibraryView layout with host drawing primitives, not Evas.

No app code is patched. The generated host copy replaces only native drawing
methods and uses stub ElmSharp types. Text/font rendering still needs TV review.
"""
import json
import shutil
import subprocess
from pathlib import Path

root = Path(__file__).resolve().parents[1]
work = root / 'build/library-preview'
work.mkdir(parents=True, exist_ok=True)
source = (root / 'app/LibraryView.cs').read_text(encoding='utf-8-sig')


def replace_body(text, signature, body):
    start = text.index('{', text.index(signature))
    depth, end = 1, start + 1
    while depth:
        if text[end] == '{':
            depth += 1
        elif text[end] == '}':
            depth -= 1
        end += 1
    return text[:start] + '{' + body + '}' + text[end:]


source = replace_body(source, 'void Panel(', 'Preview.Panel(x,y,w,h,r,g,b,alpha);')
source = replace_body(source, 'void Text(', 'Preview.Text(text,x,y,w,h,size,color,centered);')
source = replace_body(source, 'bool Picture(', 'return Preview.Picture(path,x,y,w,h);')
source = replace_body(source, 'void SetBackground(', 'Preview.Background(path??fallback);')
(work / 'LibraryView.cs').write_text(source, encoding='utf-8')
shutil.copy2(root / 'app/LibraryTheme.cs',work / 'LibraryTheme.cs')
(work / 'Preview.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>netcoreapp3.1</TargetFramework>
<NoWarn>0649;0169;0414</NoWarn></PropertyGroup></Project>''')
(work / 'Preview.cs').write_text(r'''
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
namespace ElmSharp {
    enum WrapType { Mixed }
    class Rect {public int Width,Height;public Rect(int x,int y,int w,int h){Width=w;Height=h;}}
    class Window {public Rect Geometry=new Rect(0,0,1920,1080);public IntPtr Handle;}
    class Label {public Label(Window w){} public WrapType LineWrapType;public short Layer;public Rect Geometry;public string Text;public void Show(){}public void Hide(){}}
    static class EcoreMainloop {internal static IntPtr AddTimer(double time,Func<bool> callback)=>IntPtr.Zero;internal static void RemoveTimer(IntPtr timer){}}
}
static class Screen {
    internal static IntPtr evas_object_evas_get(IntPtr p)=>IntPtr.Zero;
    internal static IntPtr evas_object_image_filled_add(IntPtr p)=>IntPtr.Zero;
    internal static void evas_object_move(IntPtr p,int x,int y){}
    internal static void evas_object_resize(IntPtr p,int w,int h){}
    internal static void evas_object_show(IntPtr p){}
    internal static void evas_object_hide(IntPtr p){}
}
class Primitive {
    public string Kind {get;set;} public string Text {get;set;} public string Color {get;set;}
    public int X {get;set;} public int Y {get;set;} public int W {get;set;} public int H {get;set;} public int Size {get;set;}
    public string Path {get;set;} public bool Fit {get;set;} public int Alpha {get;set;}=255;
    public bool Centered {get;set;}
}
static class Preview {
    static readonly List<Primitive> commands=new List<Primitive>();
    internal static void Panel(int x,int y,int w,int h,int r,int g,int b,int alpha)=>commands.Add(new Primitive{Kind="panel",X=x,Y=y,W=w,H=h,Color="#"+r.ToString("X2")+g.ToString("X2")+b.ToString("X2"),Alpha=alpha});
    internal static void Text(string text,int x,int y,int w,int h,int size,string color,bool centered)=>commands.Add(new Primitive{Kind="text",Text=text,X=x,Y=y,W=w,H=h,Size=size,Color=color,Centered=centered});
    internal static bool Picture(string path,int x,int y,int w,int h){if(!File.Exists(path))return false;commands.Add(new Primitive{Kind="image",Path=path,X=x,Y=y,W=w,H=h,Fit=true});return true;}
    internal static void Background(string path){if(File.Exists(path))commands.Add(new Primitive{Kind="image",Path=path,X=0,Y=0,W=1920,H=1080,Fit=false});}
    static void Capture(string name,Action draw) {
        commands.Clear();draw();
        foreach(var p in commands)if(p.X<0||p.Y<0||p.X+p.W>1920||p.Y+p.H>1080)throw new Exception("Layout outside canvas: "+name+" "+p.Text);
        File.WriteAllText(name+".json",JsonSerializer.Serialize(commands));
    }
    static void Main(string[] args) {
        var view=new LibraryView(new ElmSharp.Window(),args[0]);
        string[] games=Array.ConvertAll(Directory.GetFiles(args[1],"*.jpg"),p=>System.IO.Path.ChangeExtension(p,".iso"));Array.Sort(games,StringComparer.OrdinalIgnoreCase);
        if(games.Length!=8)throw new Exception("Expected eight preview covers copied from the game USB");
        string[] actions={"Play game","Controller settings","Sound: Default (120 ms buffer)","CPU: Default (ARM JIT)","Resolution: 1x - 480 x 272","Rendering: Full frames","Remove from Favorites","Back to collection"};
        int vice=Array.FindIndex(games,p=>p.Contains("Vice City"));
        Capture("01-collection",()=>view.Draw(games,vice,vice,false,0,"Background changes with your selected game.",actions,"ALL / TITLE A-Z","",""));
        Capture("16-moving",()=>view.Draw(games,vice,Math.Max(0,vice-.5),false,0,"",actions,"ALL / TITLE A-Z","",""));
        Capture("02-details",()=>view.Draw(games,vice,vice,true,4,"Saved settings apply on the next launch.",actions,"ALL / TITLE A-Z","","1,617 MiB  /  FAVORITE\nLast played: 2026-11-09 18:00\n1x - 480 x 272  /  PSP widescreen"));
        actions[3]="CPU: ARM JIT (fast memory)";
        Capture("17-fast-memory",()=>view.Draw(games,vice,vice,true,3,"Fast memory reduces CPU checks. If a game crashes, select ARM JIT. Relaunch to apply.",actions,"ALL / TITLE A-Z","","1,617 MiB  /  FAVORITE\n1x - 480 x 272  /  PSP widescreen"));
        actions[3]="CPU: Default (ARM JIT)";
        Capture("03-options",()=>view.DrawMenu("Library options","Choose a collection, change its order, or set the default sound buffer.",new[]{"Collection: Recent","Sort: Title A-Z (Recent always uses last played)","Default sound: 120 ms buffer","Controller settings","Rescan USB","Help & controls","Diagnostics","Back to collection"},0,false,""));
        Capture("04-pause",()=>view.DrawMenu("Game paused","Mortal Kombat Unchained\nResume continues the current session.",new[]{"Resume game","Controller settings","Performance overlay: On","Diagnostics","Export logs to USB","Help & controls","Exit app..."},0,false,""));
        Capture("05-help",()=>view.DrawMenu("Help & controls","TizenPPSSPP 0.16.3",Array.Empty<string>(),0,true,""));
        Capture("06-exit",()=>view.DrawMenu("Exit TizenPPSSPP?","Current gameplay will end. Progress is not saved automatically.\nCancel, then Resume to save through the game's own menu first.",new[]{"Cancel","Exit app"},0,false,""));
        Capture("07-empty-favorites",()=>view.Draw(Array.Empty<string>(),0,0,false,0,"No games in Favorites. Up opens library options.",actions,"FAVORITES / TITLE A-Z","Add a game to Favorites from its details.\nUp opens options to return to All games.",""));
        for(int i=0;i<games.Length;i++){int index=i;Capture((i+8).ToString("00")+"-game",()=>view.Draw(games,index,index,false,0,"",actions,"ALL / TITLE A-Z","",""));}
    }
}
''', encoding='utf-8')
subprocess.run(['dotnet', 'run', '--project', str(work / 'Preview.csproj'), '-c', 'Release', '-v', 'quiet','--',str(root/'app/res'),str(root/'build/preview-assets')], cwd=work, check=True)
subprocess.run([shutil.which('pwsh') or 'powershell', '-NoProfile', '-File', str(root / 'tools/render-library-preview.ps1'), '-Directory', str(work)], check=True)
print(json.dumps({'preview': str(work), 'limit': 'Actual layout commands, generated backgrounds and copied user cover art; host fonts, not TV screenshots'}))
