"""Exercise the real LibraryView background lifecycle with fake Evas and clock."""
import re, shutil, subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[1]
work=root/'build/library-view-check';work.mkdir(parents=True,exist_ok=True)
source=(root/'app/LibraryView.cs').read_text()
replacements={
'evas_object_rectangle_add':'Native.Rectangle',
'evas_object_color_set':'Native.Color',
'evas_object_layer_set':'Native.Layer',
'evas_object_image_file_set':'Native.File',
'evas_object_image_load_error_get':'Native.Error',
'evas_object_image_load_size_set':'Native.LoadSize',
'evas_object_image_size_get':'Native.Size'}
source=re.sub(r'\s*\[DllImport\([^\n]+\)\]static extern [^\n]+;','',source)
for a,b in replacements.items():source=source.replace(a,b)
source=source.replace('static double Now=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;','static double Now=>Native.Now;')
(work/'LibraryView.cs').write_text(source)
shutil.copy2(root/'app/LibraryTheme.cs',work/'LibraryTheme.cs')
shutil.copy2(root/'app/AppVersion.cs',work/'AppVersion.cs')
shutil.copy2(root/'app/tizen-manifest.xml',work/'tizen-manifest.xml')
(work/'Check.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>netcoreapp3.1</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup><EmbeddedResource Include="tizen-manifest.xml" LogicalName="TizenPSP.Manifest" /></ItemGroup></Project>')
(work/'Check.cs').write_text(r'''
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
namespace ElmSharp {
    enum WrapType {Mixed}
    class Rect {public int X,Y,Width,Height;public Rect(int x,int y,int w,int h){X=x;Y=y;Width=w;Height=h;}}
    class Window {public Rect Geometry=new Rect(0,0,1920,1080);public IntPtr Handle;}
    class Label {internal static readonly List<Label> All=new List<Label>();public Label(Window w){All.Add(this);}public WrapType LineWrapType;public short Layer;public Rect Geometry;public string Text;public bool Visible;public void Show(){Visible=true;}public void Hide(){Visible=false;}}
    static class EcoreMainloop {
        internal static readonly Dictionary<IntPtr,Func<bool>> Timers=new Dictionary<IntPtr,Func<bool>>();
        static int next;
        internal static IntPtr AddTimer(double seconds,Func<bool> callback){if(seconds<1.0/31)throw new Exception("Unbounded fade rate");var id=new IntPtr(++next);Timers[id]=callback;return id;}
        internal static void RemoveTimer(IntPtr timer){Timers.Remove(timer);}
        internal static void Fire(){foreach(var t in Timers.ToArray())if(!t.Value())Timers.Remove(t.Key);}
    }
}
static class Native {
    internal sealed class Item {internal bool Image,Visible;internal string Path;internal int Alpha=255,Layer,W,H;}
    internal static readonly Dictionary<IntPtr,Item> Items=new Dictionary<IntPtr,Item>();
    internal static string BadPath;internal static double Now;internal static int Loads;
    internal static IntPtr Make(bool image){var id=new IntPtr(Items.Count+1);Items[id]=new Item{Image=image};return id;}
    internal static IntPtr Rectangle(IntPtr c)=>Make(false);
    internal static void Color(IntPtr p,int r,int g,int b,int a){if(r>a||g>a||b>a)throw new Exception("Evas alpha not premultiplied");Items[p].Alpha=a;}
    internal static void Layer(IntPtr p,short layer){Items[p].Layer=layer;}
    internal static void File(IntPtr p,string path,string key){Items[p].Path=path;Loads++;}
    internal static int Error(IntPtr p)=>Items[p].Path==BadPath?1:0;
    internal static void LoadSize(IntPtr p,int w,int h){Items[p].W=w;Items[p].H=h;}
    internal static void Size(IntPtr p,out int w,out int h){w=1672;h=941;}
}
static class Screen {
    internal static IntPtr evas_object_evas_get(IntPtr p)=>IntPtr.Zero;
    internal static IntPtr evas_object_image_filled_add(IntPtr p)=>Native.Make(true);
    internal static void evas_object_move(IntPtr p,int x,int y){}
    internal static void evas_object_resize(IntPtr p,int w,int h){}
    internal static void evas_object_show(IntPtr p){Native.Items[p].Visible=true;}
    internal static void evas_object_hide(IntPtr p){Native.Items[p].Visible=false;}
}
class Checks {
    static void Need(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Draw(LibraryView view,string path)=>view.Draw(new[]{path},0,0,false,0,"",Array.Empty<string>(),"ALL","","");
    static int Images=>Native.Items.Values.Count(p=>p.Image);
    static void Main(string[] args) {
        string resources=args[0],temp=args[1];Directory.CreateDirectory(temp);
        string[] games={"Grand Theft Auto - Vice City Stories","Grand Theft Auto - Liberty City Stories","God of War - Chains of Olympus","Mortal Kombat - Unchained","Tekken 6","Gran Turismo","Need for Speed - Most Wanted 5-1-0","Shrek the Third"};
        var names=games.Select(x=>LibraryTheme.ForGame(x+".iso").Asset).ToArray();Need(names.Distinct().Count()==8,"Distinct backgrounds for eight current games");
        foreach(string game in games)Need(LibraryTheme.UsableFile(LibraryTheme.ForGame(game+".iso").Bundled(resources)),"Bundled theme missing");
        var window=new ElmSharp.Window();var view=new LibraryView(window,resources);
        Draw(view,games[0]+".iso");Need(Images==1&&ElmSharp.EcoreMainloop.Timers.Count==0,"First background should show without fade");
        Draw(view,games[1]+".iso");Need(Images==2&&ElmSharp.EcoreMainloop.Timers.Count==1,"Two-image selection fade");
        Native.Now=.14;ElmSharp.EcoreMainloop.Fire();Need(Native.Items.Values.Any(x=>x.Image&&x.Alpha>0&&x.Alpha<255),"Intermediate fade alpha");
        Native.Now=.30;ElmSharp.EcoreMainloop.Fire();Need(ElmSharp.EcoreMainloop.Timers.Count==0&&Native.Items.Values.Count(x=>x.Image&&x.Path!=null)==1,"Completed fade releases old pixels");
        for(int i=0;i<30;i++){Draw(view,games[i%8]+".iso");Native.Now+=.04;ElmSharp.EcoreMainloop.Fire();}
        Need(Images==2&&ElmSharp.EcoreMainloop.Timers.Count<=1,"Rapid selection must retain only two backgrounds and one timer");
        Need(Native.Items.Values.Where(x=>x.Image).All(x=>x.W==1280&&x.H==720),"Background decode budget");
        view.Hide();Need(ElmSharp.EcoreMainloop.Timers.Count==0&&Native.Items.Values.Where(x=>x.Image).All(x=>x.Path==null&&!x.Visible),"Hide stops fade and clears artwork files");
        string testGame=Path.Combine(temp,"God of War - Test.iso"),custom=Path.Combine(temp,"God of War - Test.background.jpg");
        File.WriteAllText(custom,"invalid image fixture");Native.BadPath=custom;
        Need(LibraryTheme.ForGame(testGame).Background(testGame,resources)==custom,"Sidecar takes priority");
        Draw(view,testGame);Need(Native.Items.Values.Any(x=>x.Image&&x.Path==LibraryTheme.ForGame(testGame).Bundled(resources)),"Invalid sidecar falls back to bundled image");
        int loads=Native.Loads;Draw(view,testGame);Need(loads==Native.Loads,"Failed sidecar should not be decoded again every redraw");
        view.Hide();Need(Images==2,"Reopen reuses existing image objects");
        string[] actions={"Play game","Controller settings","Sound: Default","CPU: ARM JIT","Resolution: 1x","Rendering: Full frames",
            "Effects: Skip buffer effects (may hide graphics)","Add to Favorites","Back to collection"};
        foreach(int width in new[]{1920,1280,640}) {
            int height=width*9/16;window.Geometry=new ElmSharp.Rect(0,0,width,height);
            view.Draw(new[]{testGame},0,0,true,8,"",actions,"ALL","","");
            var actionLabels=ElmSharp.Label.All.Where(label=>label.Visible && actions.Any(action=>label.Text.Contains(action))).OrderBy(label=>label.Geometry.Y).ToArray();
            Need(actionLabels.Length==9,"Nine game settings were not rendered");
            for(int i=0;i<actionLabels.Length;i++) {
                var rect=actionLabels[i].Geometry;
                var fontMatch=System.Text.RegularExpressions.Regex.Match(actionLabels[i].Text,@"<font_size=(\d+)>");
                Need(fontMatch.Success && rect.Height>=int.Parse(fontMatch.Groups[1].Value)*1.25,"Game setting text box clips its emitted font size");
                Need(rect.Y>=0 && rect.Y+rect.Height<946*width/1920 && rect.X>=0 && rect.X+rect.Width<=width,"Game setting overlaps footer or leaves screen");
                if(i>0)Need(actionLabels[i-1].Geometry.Y+actionLabels[i-1].Geometry.Height<=rect.Y,"Game settings overlap");
            }
        }
        view.Hide();
        Console.WriteLine("PASS: nine game settings fit above footer at 1080p/720p/360p; eight game themes, sidecar priority/fallback, real view crossfade lifecycle, rapid selection bound, decode-size requests and hide cleanup.");
    }
}
''')
subprocess.run(['dotnet','run','--project',str(work/'Check.csproj'),'-c','Release','--',str(root/'app/res'),str(work/'input')],cwd=root,check=True)
