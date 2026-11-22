using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

// Adapted from TizenPS KeyInput/PadMapping: hold physical inputs independently,
// learn raw events before any firmware button-number conversion, persist bindings.
internal sealed class PspInputMap
{
    internal static readonly string[] Names={"Cross","Circle","Square","Triangle","L","R","Start","Select","Up","Down","Left","Right","App menu"};
    internal static readonly int[] Bits={0,8,1,9,10,11,3,2,4,5,6,7,20};
    internal readonly string[] Bindings=new string[13];
    readonly HashSet<string> held=new HashSet<string>(StringComparer.Ordinal);
    internal string Last="No input yet",LastRaw="No raw button yet",LastKey="No window key yet";
    internal uint LastPressed {get;private set;}
    internal bool HasRawBindings=>Bindings.Any(t=>t!=null&&!t.StartsWith("key:",StringComparison.Ordinal));
    internal uint RawHeld {get {uint bits=0;for(int i=0;i<Bindings.Length;i++)if(Bindings[i]!=null&&!Bindings[i].StartsWith("key:",StringComparison.Ordinal)&&held.Contains(Bindings[i]))bits|=1u<<Bits[i];return bits;}}
    internal static string Describe(uint mask) {var names=new List<string>();for(int i=0;i<Bits.Length;i++)if((mask&(1u<<Bits[i]))!=0)names.Add(Names[i]);return names.Count==0?"None":string.Join(" + ",names);}
    internal int Owner(string token)=>Array.IndexOf(Bindings,token);
    internal bool TryAssign(int action,string token,out string conflict) {
        int other=Owner(token);conflict=other>=0&&other!=action?Names[other]:null;
        if(conflict!=null)return false;Assign(action,token);return true;
    }
    internal uint Held {get {uint bits=0;for(int i=0;i<Bindings.Length;i++)if(Bindings[i]!=null && held.Contains(Bindings[i]))bits|=1u<<Bits[i];return bits;}}
    internal uint HeldExcept(string token) {uint bits=0;for(int i=0;i<Bindings.Length;i++)if(Bindings[i]!=null&&Bindings[i]!=token&&held.Contains(Bindings[i]))bits|=1u<<Bits[i];return bits;}
    internal bool Neutral=>held.Count==0;
    internal string HeldSources=>held.Count==0?"none":string.Join(", ",held.OrderBy(t=>t,StringComparer.Ordinal).Take(6))+(held.Count>6?", ...":"");
    internal bool RawNeutral=>!held.Any(t=>!t.StartsWith("key:",StringComparison.Ordinal));
    internal static string Raw(int type,int code,int value) => type==1?"button:"+code:type==3 && (code==16||code==17) && value!=0?"hat:"+code+":"+Math.Sign(value):null;
    internal string ObserveRaw(int type,int code,int value)
    {
        if(type==1 || type==3 && (code==16||code==17))Last=LastRaw="Raw "+type+" / "+code+" / "+value;
        if(type==1 && code>=0 && code<256 && value>=0 && value<=2) {
            string token=Raw(type,code,value);if(value==0)held.Remove(token);else held.Add(token);if(value==1 && Held!=0)LastPressed=Held;return value==1?token:null;
        }
        if(type==3 && (code==16||code==17) && value>=-1 && value<=1) {
            held.Remove("hat:"+code+":-1");held.Remove("hat:"+code+":1");
            var token=Raw(type,code,value);if(token!=null) {held.Add(token);if(Held!=0)LastPressed=Held;}return token;
        }
        return null;
    }
    internal void ObserveKey(string key,bool down) {string t="key:"+key;if(down)held.Add(t);else held.Remove(t);Last=LastKey=(down?"Key down: ":"Key up: ")+key;if(down && Held!=0)LastPressed=Held;}
    internal bool MappedKey(string key)=>Bindings.Contains("key:"+key);
    internal void ClearHeld(){held.Clear();LastPressed=0;}
    internal void ClearRaw(){held.RemoveWhere(t=>!t.StartsWith("key:",StringComparison.Ordinal));}
    internal void Assign(int action,string token)
    {
        if(action<0||action>=Bindings.Length||!Valid(token))throw new ArgumentException("Invalid input binding");
        for(int i=0;i<Bindings.Length;i++)if(Bindings[i]==token)Bindings[i]=null;
        Bindings[action]=token;
    }
    internal static bool Valid(string token)
    {
        if(string.IsNullOrWhiteSpace(token)||token.Length>160)return false;
        if(token.StartsWith("key:",StringComparison.Ordinal))return token.Length>4 && token.IndexOfAny(new[]{'\r','\n','\t'})<0;
        var p=token.Split(':');int code,direction;
        return p.Length==2 && p[0]=="button" && int.TryParse(p[1],out code)&&code>=0&&code<256 ||
            p.Length==3 && p[0]=="hat" && int.TryParse(p[1],out code)&&(code==16||code==17)&&int.TryParse(p[2],out direction)&&(direction==-1||direction==1);
    }
    internal string Serialize()=>"TizenPSP-input-v1\n"+string.Join("\n",Bindings.Select((t,i)=>i+"\t"+(t==null?"":Convert.ToBase64String(Encoding.UTF8.GetBytes(t)))));
    internal static PspInputMap Parse(string text) {
        var map=new PspInputMap();if(text.Length>32768||!text.StartsWith("TizenPSP-input-v1\n"))throw new FormatException("Unknown input map format");
        foreach(var line in text.Split('\n').Skip(1)) {
            var p=line.TrimEnd('\r').Split('\t');int i;
            if(p.Length!=2||p[1].Length==0)continue;
            if(!int.TryParse(p[0],out i))throw new FormatException("Invalid map index");
            map.Assign(i,Encoding.UTF8.GetString(Convert.FromBase64String(p[1])));
        }
        return map;
    }
    internal static bool Echo(double keyTime,double rawTime)=>Math.Abs(keyTime-rawTime)<=.30;
    internal static uint Keyboard(string key) {
        switch(key) {
        case "Up":return 1u<<4;case "Down":return 1u<<5;case "Left":return 1u<<6;case "Right":return 1u<<7;
        case "Return":case "KP_Enter":case "z":case "Z":return 1;
        case "p":case "P":case "XF86Play":case "XF86AudioPlay":case "XF86PlayPause":return 1u<<3;
        case "Tab":case "Shift_L":case "Shift_R":return 1u<<2;
        case "c":case "C":return 1u<<8;case "s":case "S":return 1u<<1;
        case "t":case "T":return 1u<<9;case "q":case "Q":return 1u<<10;case "e":case "E":return 1u<<11;
        default:return 0;
        }
    }
}
