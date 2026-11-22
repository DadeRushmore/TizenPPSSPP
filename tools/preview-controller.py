"""Render the actual mapper layout with host drawing stubs; TV font validation remains separate."""
import json, subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[1]
work=root/'build/controller-preview';work.mkdir(parents=True,exist_ok=True)
source=(root/'app/InputMapperView.cs').read_text()
def replace_body(text, signature, body):
    start=text.index('{',text.index(signature));depth=1;end=start+1
    while depth:
        if text[end]=='{':depth+=1
        elif text[end]=='}':depth-=1
        end+=1
    return text[:start]+'{'+body+'}'+text[end:]
source=replace_body(source,'void Panel(','Preview.Panel(x,y,w,h,r,g,b);')
source=replace_body(source,'void Text(','Preview.Text(text,x,y,w,h,size,color);')
(work/'InputMapperView.cs').write_text(source)
(work/'PspInputMap.cs').write_bytes((root/'app/PspInputMap.cs').read_bytes())
(work/'StickInput.cs').write_bytes((root/'app/StickInput.cs').read_bytes())
(work/'ButtonIdentityCheck.cs').write_bytes((root/'app/ButtonIdentityCheck.cs').read_bytes())
(work/'DualSenseReports.cs').write_bytes((root/'app/DualSenseReports.cs').read_bytes())
(work/'Preview.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>netcoreapp3.1</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup></Project>')
(work/'Preview.cs').write_text(r'''
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
namespace ElmSharp {
    enum WrapType { Mixed }
    class Rect {public int Width,Height;public Rect(int x,int y,int w,int h){Width=w;Height=h;}}
    class Window {public Rect Geometry=new Rect(0,0,1920,1080);public IntPtr Handle;}
    class Label {public Label(Window w){}public WrapType LineWrapType;public short Layer;public Rect Geometry;public string Text;public void Show(){}public void Hide(){}}
}
static class Screen {
    internal static IntPtr evas_object_evas_get(IntPtr p)=>IntPtr.Zero;
    internal static void evas_object_move(IntPtr p,int x,int y){}
    internal static void evas_object_resize(IntPtr p,int w,int h){}
    internal static void evas_object_show(IntPtr p){}
    internal static void evas_object_hide(IntPtr p){}
}
class Primitive {
    public string Kind {get;set;} public string Text {get;set;} public string Color {get;set;}
    public int X {get;set;} public int Y {get;set;} public int W {get;set;} public int H {get;set;} public int Size {get;set;}
}
static class Preview {
    static readonly List<Primitive> items=new List<Primitive>();
    public static void Panel(int x,int y,int w,int h,int r,int g,int b)=>items.Add(new Primitive{Kind="panel",X=x,Y=y,W=w,H=h,Color="#"+r.ToString("X2")+g.ToString("X2")+b.ToString("X2")});
    public static void Text(string t,int x,int y,int w,int h,int size,string color)=>items.Add(new Primitive{Kind="text",Text=t,X=x,Y=y,W=w,H=h,Size=size,Color=color});
    static void Main(string[] args) {
        var view=new InputMapperView(new ElmSharp.Window());var map=new PspInputMap();
        for(int i=0;i<13;i++)map.Assign(i,"button:"+i);
        string[] prompts={"Select Start or Select, press remote OK, then press that controller button.","Waiting for release: key:Return, button:2, hat:16:1. Release all buttons; Back cancels.","Connected, but no controller events arrived. Check Input slot; Back cancels.","Input: Slot 4 selected. Try a button; saved bindings stay the same."};
        for(int i=0;i<prompts.Length;i++) {
            items.Clear();view.Draw(map,i==3?18:6,i==1||i==2,prompts[i],"Connected; event counts below | slot 2 | buttons 123 hats 4 axes 12345 other 0 | Raw 1 / 9 / 0 | Key up: Return",i==3?"Input: Slot 4":"Input: Auto");
            foreach(var p in items)if(p.X<0||p.Y<0||p.X+p.W>1920||p.Y+p.H>1080)throw new Exception("Mapper primitive outside canvas");
            File.WriteAllText(Path.Combine(args[0],i.ToString("D2")+"-mapper.json"),JsonSerializer.Serialize(items));
        }
        var sticks=new StickInput();sticks.Observe(3,0,0);sticks.Observe(3,1,128);sticks.Observe(3,2,255);sticks.Observe(3,5,128);
        for(int i=0;i<4;i++) {
            sticks.RightMode=i;items.Clear();view.DrawSticks(sticks,i,null,"Calibrated. Test both sticks, then Save & return.");
            foreach(var p in items)if(p.X<0||p.Y<0||p.X+p.W>1920||p.Y+p.H>1080)throw new Exception("Stick primitive outside canvas");
            File.WriteAllText(Path.Combine(args[0],(i+4).ToString("D2")+"-sticks.json"),JsonSerializer.Serialize(items));
        }
        items.Clear();view.DrawSticks(sticks,0,new StickCalibration(),"Move both sticks in circles, release them, then press remote OK.");
        File.WriteAllText(Path.Combine(args[0],"08-calibration.json"),JsonSerializer.Serialize(items));
        var check=new ButtonIdentityCheck(0);
        foreach(int t in new[]{0,2,8}) {
            items.Clear();view.DrawButtonCheck(check,t);
            File.WriteAllText(Path.Combine(args[0],(9+t).ToString("D2")+"-button-check.json"),JsonSerializer.Serialize(items));
        }
        items.Clear();view.Draw(map,20,false,"No repeatable separate button/key identity found. Export the scan/event trace for review. Keep R3 for Circle. Info opens diagnostics; Down exports the saved check.","Check saved in diagnostics; mappings unchanged.");
        File.WriteAllText(Path.Combine(args[0],"18-button-result.json"),JsonSerializer.Serialize(items));
        items.Clear();view.Draw(map,21,false,"PS5 face fix Auto: waits for verified DualSense reports. R3 stays available.","Connected | PS5 faces: Direct HID active","Input: Auto",true,DualSenseReports.Circle);
        File.WriteAllText(Path.Combine(args[0],"19-direct-circle.json"),JsonSerializer.Serialize(items));
        items.Clear();view.Draw(map,21,false,"PS5 face fix Off: original TV input and R3 workaround restored.","Connected | PS5 faces: Off","Input: Auto",false);
        File.WriteAllText(Path.Combine(args[0],"20-direct-off.json"),JsonSerializer.Serialize(items));
    }
}
''')
subprocess.run(['dotnet','run','--project',str(work/'Preview.csproj'),'-c','Release','--',str(work)],check=True)
subprocess.run(['pwsh','-NoProfile','-File',str(root/'tools/render-library-preview.ps1'),'-Directory',str(work)],check=True)
print('Mapper host preview:',work/'contact-sheet.png')
