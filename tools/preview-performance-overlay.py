"""Capture the actual HUD layout with host widget stubs; no TV or game screenshot."""
from pathlib import Path
import re, shutil, subprocess
root=Path(__file__).resolve().parents[1]
work=root/'build/performance-preview';work.mkdir(parents=True,exist_ok=True)
source=(root/'app/PerformanceOverlayView.cs').read_text()
replacements={
 'static extern IntPtr evas_object_rectangle_add(IntPtr canvas);':'static IntPtr evas_object_rectangle_add(IntPtr canvas)=>Preview.Rectangle();',
 'static extern void evas_object_color_set(IntPtr obj,int r,int g,int b,int a);':'static void evas_object_color_set(IntPtr obj,int r,int g,int b,int a)=>Preview.Color(obj,r,g,b);',
 'static extern void evas_object_layer_set(IntPtr obj,short layer);':'static void evas_object_layer_set(IntPtr obj,short layer){}',
 'static extern void evas_object_pass_events_set(IntPtr obj,byte pass);':'static void evas_object_pass_events_set(IntPtr obj,byte pass){}'}
source=re.sub(r'\[DllImport\("libevas.so.1"\)\]','',source)
for before,after in replacements.items():
    assert before in source;source=source.replace(before,after)
(work/'PerformanceOverlayView.cs').write_text(source)
(work/'Preview.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>netcoreapp3.1</TargetFramework><LangVersion>8.0</LangVersion></PropertyGroup><ItemGroup><Compile Include="'+str(root/'app/PerformanceOverlayMetrics.cs')+'" Link="PerformanceOverlayMetrics.cs" /></ItemGroup></Project>')
(work/'Preview.cs').write_text(r'''
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ElmSharp {
    enum WrapType{Mixed}
    class Rect{public int X,Y,Width,Height;public Rect(int x,int y,int w,int h){X=x;Y=y;Width=w;Height=h;}}
    class Window{public IntPtr Handle;}
    class Label{
        public Label(Window window){Preview.Labels.Add(this);}public WrapType LineWrapType;public short Layer;
        public IntPtr Handle;public Rect Geometry;public string Text;public bool Visible;
        public void Show(){Visible=true;}public void Hide(){Visible=false;}public void Unrealize(){Visible=false;}
    }
}
class Primitive{public string Kind{get;set;}public string Text{get;set;}public string Color{get;set;}public int X{get;set;}public int Y{get;set;}public int W{get;set;}public int H{get;set;}public int Size{get;set;}}
static class Screen{
    internal static IntPtr evas_object_evas_get(IntPtr p)=>IntPtr.Zero;
    internal static void evas_object_move(IntPtr p,int x,int y){Preview.Panels[p].X=x;Preview.Panels[p].Y=y;}
    internal static void evas_object_resize(IntPtr p,int w,int h){Preview.Panels[p].W=w;Preview.Panels[p].H=h;}
    internal static void evas_object_show(IntPtr p){Preview.Visible.Add(p);}
    internal static void evas_object_hide(IntPtr p){Preview.Visible.Remove(p);}
    internal static void evas_object_del(IntPtr p){Preview.Panels.Remove(p);Preview.Visible.Remove(p);}
}
static class Preview{
    internal static readonly Dictionary<IntPtr,Primitive> Panels=new Dictionary<IntPtr,Primitive>();
    internal static readonly HashSet<IntPtr> Visible=new HashSet<IntPtr>();
    internal static readonly List<ElmSharp.Label> Labels=new List<ElmSharp.Label>();
    internal static IntPtr Rectangle(){var id=(IntPtr)(Panels.Count+1);Panels[id]=new Primitive{Kind="panel"};return id;}
    internal static void Color(IntPtr p,int r,int g,int b){Panels[p].Color="#"+r.ToString("X2")+g.ToString("X2")+b.ToString("X2");}
    static void Capture(string file){
        var result=new List<Primitive>{new Primitive{Kind="panel",X=0,Y=0,W=1920,H=1080,Color="#273342"}};
        foreach(var p in Visible)result.Add(Panels[p]);
        foreach(var label in Labels)if(label.Visible){
            int y=label.Geometry.Y;
            foreach(Match m in Regex.Matches(label.Text,@"<font_size=(\d+)><color=([^>]+)>(.*?)</color></font_size>")){
                int size=int.Parse(m.Groups[1].Value),height=(int)Math.Ceiling(size*1.4);
                if(y+height>label.Geometry.Y+label.Geometry.Height)throw new Exception("HUD text exceeds label bounds");
                result.Add(new Primitive{Kind="text",X=label.Geometry.X,Y=y,W=label.Geometry.Width,H=height,Text=WebUtility.HtmlDecode(m.Groups[3].Value),Size=size,Color=m.Groups[2].Value});y+=height;
            }
        }
        foreach(var p in result)if(p.X<0||p.Y<0||p.X+p.W>1920||p.Y+p.H>1080)throw new Exception("HUD outside canvas");
        File.WriteAllText(file+".json",JsonSerializer.Serialize(result));
    }
    static void Main(){
        var view=new PerformanceOverlayView(new ElmSharp.Window());view.Layout(7,0,1905,1080);
        view.Update(new PerformanceReading{HasRates=true,HasGuestRates=true,GameFps=29.9,GuestSpeedPercent=99.7,VideoFps=59.8,CadencePercent=99.7,RunP95Ms=11.8},"ARM JIT | 1x | Full frames | Audio playing | queue 21.7 ms | gaps 12");view.Show();Capture("01-running");
        view.Update(new PerformanceReading{HasRates=true,HasGuestRates=true,GameFps=15.8,GuestSpeedPercent=26.4,VideoFps=15.8,CadencePercent=26.4,RunP95Ms=73.2},"ARM JIT | 2x | Skip 1 | Audio playing | queue 0.0 ms | gaps 1290");Capture("02-slow");
        view.Layout(0,0,1280,725);Capture("03-small");view.Hide();
        if(Visible.Count!=0 || Labels.Exists(x=>x.Visible))throw new Exception("HUD did not hide");view.Dispose();
        if(Panels.Count!=0)throw new Exception("HUD panel leak");
        Console.WriteLine("PASS: actual HUD layout, hidden state and disposal captured with host widgets");
    }
}
''')
subprocess.run(['dotnet','run','--project',str(work/'Preview.csproj'),'-c','Release','-v','quiet'],cwd=work,check=True)
subprocess.run([shutil.which('pwsh') or 'powershell','-NoProfile','-File',str(root/'tools/render-library-preview.ps1'),'-Directory',str(work)],check=True)
print('HUD previews:',work)
