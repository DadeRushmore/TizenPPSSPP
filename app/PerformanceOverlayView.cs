using System;
using System.Runtime.InteropServices;
using ElmSharp;

internal sealed class PerformanceOverlayView : IDisposable
{
    readonly Label text;
    IntPtr panel,accent;
    bool shown;
    int font=24;
    string primary="Game -- FPS   |   Speed --%   |   Sampling...",secondary="";
    [DllImport("libevas.so.1")]static extern IntPtr evas_object_rectangle_add(IntPtr canvas);
    [DllImport("libevas.so.1")]static extern void evas_object_color_set(IntPtr obj,int r,int g,int b,int a);
    [DllImport("libevas.so.1")]static extern void evas_object_layer_set(IntPtr obj,short layer);
    [DllImport("libevas.so.1")]static extern void evas_object_pass_events_set(IntPtr obj,byte pass);
    internal PerformanceOverlayView(Window window) {
        text=new Label(window){LineWrapType=WrapType.Mixed,Layer=62};
        IntPtr canvas=Screen.evas_object_evas_get(window.Handle);
        panel=evas_object_rectangle_add(canvas);accent=evas_object_rectangle_add(canvas);
        evas_object_layer_set(panel,60);evas_object_layer_set(accent,61);
        evas_object_pass_events_set(panel,1);evas_object_pass_events_set(accent,1);evas_object_pass_events_set(text.Handle,1);
        // Evas RGBA is premultiplied. Keep the HUD legible over bright gameplay.
        evas_object_color_set(panel,7,12,24,225);evas_object_color_set(accent,75,224,228,255);
    }
    internal void Layout(int x,int y,int width,int height) {
        double scale=Math.Max(.33,Math.Min(width/1920.0,height/1080.0));
        int margin=Math.Max(8,(int)(24*scale)),w=Math.Min(width-2*margin,(int)(1040*scale)),h=(int)(115*scale);
        font=Math.Max(12,(int)(27*scale));
        int left=x+margin,top=y+margin,padding=Math.Max(5,(int)(17*scale));
        Screen.evas_object_move(panel,left,top);Screen.evas_object_resize(panel,w,h);
        Screen.evas_object_move(accent,left,top);Screen.evas_object_resize(accent,Math.Max(2,(int)(4*scale)),h);
        text.Geometry=new Rect(left+padding,top+Math.Max(4,(int)(8*scale)),w-padding*2,h-Math.Max(5,(int)(10*scale)));
        Render();
    }
    internal void Update(PerformanceReading reading,string status) {primary=reading.Primary;secondary=status??"";Render();}
    static string Escape(string value)=>value.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;");
    void Render() {
        text.Text="<font_size="+font+"><color=#eaf7ff>"+Escape(primary)+"</color></font_size><br/>"+
            "<font_size="+Math.Max(12,font*3/4)+"><color=#acc3d6>"+Escape(secondary)+"</color></font_size><br/>"+
            "<font_size="+Math.Max(11,font*2/3)+"><color=#78a8b8>Speed = PSP time / real time   |   Select + L + R: hide</color></font_size>";
    }
    internal void Show(){if(shown)return;shown=true;Screen.evas_object_show(panel);Screen.evas_object_show(accent);text.Show();}
    internal void Hide(){if(!shown)return;shown=false;Screen.evas_object_hide(panel);Screen.evas_object_hide(accent);text.Hide();}
    public void Dispose(){text.Hide();text.Unrealize();if(panel!=IntPtr.Zero){Screen.evas_object_del(panel);panel=IntPtr.Zero;}if(accent!=IntPtr.Zero){Screen.evas_object_del(accent);accent=IntPtr.Zero;}shown=false;}
}
