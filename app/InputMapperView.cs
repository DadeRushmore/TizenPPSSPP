using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ElmSharp;

// Native retained Evas/ElmSharp UI, adapted from TizenPS VisualMappingView.
internal sealed class InputMapperView
{
    readonly Window window;
    readonly List<Label> labels=new List<Label>();
    readonly List<IntPtr> panels=new List<IntPtr>();
    int usedLabels,usedPanels;double scale;int ox,oy;
    [DllImport("libevas.so.1")]static extern IntPtr evas_object_rectangle_add(IntPtr canvas);
    [DllImport("libevas.so.1")]static extern void evas_object_color_set(IntPtr obj,int r,int g,int b,int a);
    [DllImport("libevas.so.1")]static extern void evas_object_layer_set(IntPtr obj,short layer);
    internal InputMapperView(Window window){this.window=window;}
    void Panel(int x,int y,int w,int h,int r,int g,int b) {
        IntPtr p;if(usedPanels==panels.Count){p=evas_object_rectangle_add(Screen.evas_object_evas_get(window.Handle));panels.Add(p);evas_object_layer_set(p,25);}else p=panels[usedPanels];usedPanels++;
        Screen.evas_object_move(p,ox+(int)(x*scale),oy+(int)(y*scale));Screen.evas_object_resize(p,(int)(w*scale),(int)(h*scale));evas_object_color_set(p,r,g,b,255);Screen.evas_object_show(p);
    }
    void Text(string text,int x,int y,int w,int h,int size,string color) {
        Label l;if(usedLabels==labels.Count){l=new Label(window){LineWrapType=WrapType.Mixed};l.Layer=30;labels.Add(l);}else l=labels[usedLabels];usedLabels++;
        l.Geometry=new Rect(ox+(int)(x*scale),oy+(int)(y*scale),(int)(w*scale),(int)(h*scale));
        l.Text="<font_size="+Math.Max(12,(int)(size*scale))+"><color="+color+">"+text.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace("\n","<br/>")+"</color></font_size>";l.Show();
    }
    internal void Hide(){foreach(var l in labels)l.Hide();foreach(var p in panels)Screen.evas_object_hide(p);}
    internal void Draw(PspInputMap map,int selected,bool learning,string prompt,string source,string sourceLabel="Input: Auto",bool faceFix=true,uint? delivered=null)
    {
        var g=window.Geometry;scale=Math.Min(Math.Max(320,g.Width)/1920.0,Math.Max(240,g.Height)/1080.0);ox=(g.Width-(int)(1920*scale))/2;oy=(g.Height-(int)(1080*scale))/2;
        usedLabels=usedPanels=0;
        Panel(0,0,1920,1080,8,13,26);
        Panel(62,60,7,76,66,214,226);
        Text("TizenPPSSPP / CONTROLLER",90,50,1450,68,42,"#f3f7ff");
        Text("Use the TV remote: arrows select  |  OK learns  |  Back saves and returns",92,119,1740,50,25,"#b1c2dd");
        Panel(76,278,1190,520,29,39,60);Panel(116,310,1110,456,39,52,76);
        Panel(420,350,500,332,5,13,25);Panel(438,369,464,295,12,27,46);
        Text(learning?"PRESS & RELEASE":"LIVE INPUT",479,409,400,55,32,learning?"#ffd678":"#5de7ed");
        uint live=delivered??map.Held;
        Text(learning&&selected<13?PspInputMap.Names[selected]:PspInputMap.Describe(delivered.HasValue?live:map.Held!=0?map.Held:map.LastPressed),479,480,405,88,32,live!=0?"#85ffc0":"#ffffff");
        Text(learning?"Waiting for the selected action":live!=0?"Held now / green buttons":delivered.HasValue?"Released\nYellow = selected mapping row":"Last press (released)\nYellow = selected mapping row",479,581,410,78,22,"#93accf");
        int[] x={1030,1132,926,1030,160,1020,736,470,224,224,116,330,555};
        int[] y={552,450,450,352,223,223,708,708,352,552,450,450,822};
        uint bits=live;
        for(int i=0;i<13;i++) {
            bool active=(bits&(1u<<PspInputMap.Bits[i]))!=0,focus=selected==i;
            Panel(x[i]-6,y[i]-4,i==12?230:116,74,active?27:focus?69:23,active?127:focus?83:32,active?97:focus?110:50);
            Text(PspInputMap.Names[i],x[i]+4,y[i]+10,i==12?225:115,66,24,active?"#a4ffcf":focus?"#ffe3a0":"#d0def3");
        }
        Text("PSP ACTION",1330,201,250,44,23,"#8ea8cc");Text("PHYSICAL INPUT",1578,201,300,44,23,"#8ea8cc");
        for(int i=0;i<13;i++) {
            bool active=(bits&(1u<<PspInputMap.Bits[i]))!=0;
            if(selected==i)Panel(1308,249+i*47,550,44,42,64,90);
            Text((selected==i?"> ":"")+PspInputMap.Names[i],1325,254+i*47,244,43,23,active?"#85ffc0":selected==i?"#ffe3a0":"#d0def3");
            Text(DualSenseButtonMap.TokenName(map.Bindings[i]),1578,254+i*47,280,43,21,map.Bindings[i]==null?"#7689a5":"#6cdce5");
        }
        string[] actions={"Restore PS5 defaults","Save & return"};
        for(int i=0;i<actions.Length;i++) {
            Panel(78+i*196,924,184,66,selected==13+i?50:23,selected==13+i?81:34,selected==13+i?111:52);
            Text((selected==13+i?"> ":"")+actions[i],88+i*196,936,166,52,18,selected==13+i?"#ffe3a0":"#d0def3");
        }
        Text(prompt,92,178,1175,90,24,learning?"#ffe3a0":"#a8c2e2");
        Text(source,92,997,1750,76,21,"#8fa8c8");
        for(int i=usedLabels;i<labels.Count;i++)labels[i].Hide();for(int i=usedPanels;i<panels.Count;i++)Screen.evas_object_hide(panels[i]);
    }
    internal void DrawButtonCheck(ButtonIdentityCheck check,double now) {
        var g=window.Geometry;scale=Math.Min(Math.Max(320,g.Width)/1920.0,Math.Max(240,g.Height)/1080.0);ox=(g.Width-(int)(1920*scale))/2;oy=(g.Height-(int)(1080*scale))/2;
        usedLabels=usedPanels=0;Panel(0,0,1920,1080,8,13,26);
        Text("TizenPPSSPP / FACE BUTTON CHECK",92,70,1740,90,42,"#f3f7ff");
        Text("Keep the sticks and other buttons released. Follow the six prompts; do not use R3.",92,185,1740,95,28,"#b1c2dd");
        Text(check.Prompt(now),120,367,1680,180,48,"#ffe3a0");
        Text("Step "+Math.Min(6,check.Step+1)+" / 6     •     Circle and Triangle, three times each",120,602,1680,90,30,"#5de7ed");
        Text("No mappings will change. The completed check is saved with your diagnostics.\nAfterward: remote Info opens diagnostics; Down exports the report to USB.",92,809,1740,150,27,"#b1c2dd");
        Text("Remote Back cancels the check.",92,990,1740,60,23,"#8fa8c8");
        for(int i=usedLabels;i<labels.Count;i++)labels[i].Hide();for(int i=usedPanels;i<panels.Count;i++)Screen.evas_object_hide(panels[i]);
    }
    internal void DrawSticks(StickInput sticks,int selected,StickCalibration calibration,string message) {
        var g=window.Geometry;scale=Math.Min(Math.Max(320,g.Width)/1920.0,Math.Max(240,g.Height)/1080.0);ox=(g.Width-(int)(1920*scale))/2;oy=(g.Height-(int)(1080*scale))/2;
        usedLabels=usedPanels=0;Panel(0,0,1920,1080,8,13,26);
        Panel(62,60,7,76,66,214,226);Text("TizenPPSSPP / STICKS",90,50,1450,68,42,"#f3f7ff");
        Text("Use the TV remote during calibration. Back cancels a calibration or saves and returns.",92,119,1740,60,25,"#b1c2dd");
        Text(calibration!=null?calibration.Prompt:message??"Move both sticks to test them.",92,205,1740,95,30,"#ffe3a0");
        if(calibration!=null && message!=null)Text(message,92,298,1740,70,24,"#ffbda0");
        var values=sticks.Values;
        for(int pair=0;pair<2;pair++) {
            int x=120+pair*480,y=399,size=320;
            Text(pair==0?"LEFT / PSP movement":"RIGHT STICK",x,y-47,460,45,22,"#a8c2e2");
            Panel(x,y,size,size,27,39,60);Panel(x+159,y,2,size,75,92,115);Panel(x,y+159,size,2,75,92,115);
            int dz=(int)(size*.5*sticks.DeadZone/100.0);Panel(x+160-dz,y+160-dz,dz*2,dz*2,45,69,84);
            int px=x+160+(int)(values[pair*2]/32767.0*145),py=y+160+(int)(values[pair*2+1]/32767.0*145);
            Panel(px-10,py-10,20,20,93,231,237);
            Text("X "+values[pair*2]+"    Y "+values[pair*2+1],x,y+338,450,50,24,"#e6f3ff");
        }
        string[] actions={"Calibrate both sticks","Dead zone: "+sticks.DeadZone+"%","Right stick: "+StickInput.RightModes[sticks.RightMode],"Restore DualSense defaults","Save & return"};
        for(int i=0;i<actions.Length;i++) {
            bool focus=i==selected && calibration==null;
            Panel(1090,365+i*91,740,83,focus?50:23,focus?81:34,focus?111:52);
            Text((focus?"> ":"")+actions[i],1110,380+i*91,695,70,24,focus?"#ffe3a0":"#d0def3");
        }
        Text(calibration!=null?"Calibration step "+calibration.Step+" / 8. Hold only the requested direction.":"PSP games use one main stick. Choose a right-stick action that matches the game's controls.",92,862,1740,82,25,"#a8c2e2");
        Text(sticks.Summary,92,974,1740,100,20,"#8fa8c8");
        for(int i=usedLabels;i<labels.Count;i++)labels[i].Hide();for(int i=usedPanels;i<panels.Count;i++)Screen.evas_object_hide(panels[i]);
    }
}
