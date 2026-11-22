using System;
using System.Globalization;
using System.IO;
using System.Linq;

internal sealed class StickAxis
{
    internal int Code,Center=128,Negative=0,Positive=255;
    internal StickAxis Copy()=>new StickAxis{Code=Code,Center=Center,Negative=Negative,Positive=Positive};
    internal bool Valid=>Code>=0&&Code<16 && Center>=-32768&&Center<=65535 &&
        Negative>=-32768&&Negative<=65535&&Positive>=-32768&&Positive<=65535 &&
        Math.Abs(Negative-Center)>=16&&Math.Abs(Positive-Center)>=16 &&
        (long)(Negative-Center)*(Positive-Center)<0;
    internal double Normalize(int raw) {
        double delta=raw-Center;
        bool negative=delta*(Negative-Center)>0;
        return Math.Max(-1,Math.Min(1,(negative?-1:1)*Math.Abs(delta)/(negative?Math.Abs(Negative-Center):Math.Abs(Positive-Center))));
    }
}

// Raw EV_ABS is already delivered by the single SDK reader. No competing reader.
internal sealed class StickInput
{
    internal StickAxis[] Axes={new StickAxis{Code=0},new StickAxis{Code=1},new StickAxis{Code=2},new StickAxis{Code=5}};
    internal readonly int[] Raw=new int[16];
    internal readonly bool[] Seen=new bool[16];
    internal int DeadZone=16,RightMode=1;
    internal static readonly string[] RightModes={"PSP right stick (supported games)","D-pad","PSP left stick","L + PSP left stick (game dependent)"};
    internal bool Observe(int type,int code,int value) {
        if(type!=3||code<0||code>=16||value< -32768||value>65535)return false;
        Raw[code]=value;Seen[code]=true;return true;
    }
    internal void Clear(){Array.Clear(Seen,0,Seen.Length);Array.Clear(Raw,0,Raw.Length);}
    internal short[] Values {
        get {var v=new short[4];WriteValues(v);return v;}
    }
    // The UI owns this buffer. Overwrite every component: Route mutates its input.
    internal void WriteValues(short[] v) {
        if(v==null||v.Length<4)throw new ArgumentException("Four stick components required",nameof(v));
        v[0]=v[1]=v[2]=v[3]=0;
        for(int pair=0;pair<2;pair++) {
            int i=pair*2;
            double x=Seen[Axes[i].Code]?Axes[i].Normalize(Raw[Axes[i].Code]):0;
            double y=Seen[Axes[i+1].Code]?Axes[i+1].Normalize(Raw[Axes[i+1].Code]):0;
            double length=Math.Sqrt(x*x+y*y),dead=DeadZone/100.0;
            if(length<=dead)continue;
            double gain=(Math.Min(1,length)-dead)/((1-dead)*length);
            v[i]=(short)Math.Round(x*gain*32767);v[i+1]=(short)Math.Round(y*gain*32767);
        }
    }
    internal uint Route(short[] v) {
        if(RightMode==0)return 0;
        uint bits=0;
        if(RightMode==1) {
            const int threshold=16384;
            if(v[2]<-threshold)bits|=1u<<6;if(v[2]>threshold)bits|=1u<<7;
            if(v[3]<-threshold)bits|=1u<<4;if(v[3]>threshold)bits|=1u<<5;
        } else if(v[2]!=0||v[3]!=0) {
            v[0]=v[2];v[1]=v[3];if(RightMode==3)bits|=1u<<10;
        }
        v[2]=v[3]=0;return bits;
    }
    internal string Summary {
        get {var v=Values;return "left="+v[0]+","+v[1]+" right="+v[2]+","+v[3]+" deadzone="+DeadZone+"% mode="+RightMode+
            " axes="+string.Join(";",Axes.Select(a=>a.Code+":"+(Seen[a.Code]?Raw[a.Code].ToString():"unseen")+"["+a.Negative+","+a.Center+","+a.Positive+"]"));}
    }
    internal string Serialize()=>"TizenPSP-sticks-v1\n"+DeadZone+"\t"+RightMode+"\n"+
        string.Join("\n",Axes.Select(a=>a.Code+"\t"+a.Center+"\t"+a.Negative+"\t"+a.Positive))+"\n";
    internal static StickInput Parse(string text) {
        if(text==null||text.Length>2048)throw new FormatException("Invalid stick settings");
        var rows=text.Replace("\r\n","\n").TrimEnd('\n').Split('\n');
        if(rows.Length!=6||rows[0]!="TizenPSP-sticks-v1")throw new FormatException("Unknown stick settings");
        var result=new StickInput();var options=rows[1].Split('\t');
        if(options.Length!=2)throw new FormatException("Invalid stick options");
        result.DeadZone=int.Parse(options[0],CultureInfo.InvariantCulture);result.RightMode=int.Parse(options[1],CultureInfo.InvariantCulture);
        if(result.DeadZone<5||result.DeadZone>35||result.RightMode<0||result.RightMode>=RightModes.Length)throw new FormatException("Invalid stick options");
        for(int i=0;i<4;i++) {
            var p=rows[i+2].Split('\t');if(p.Length!=4)throw new FormatException("Invalid axis");
            var a=new StickAxis{Code=int.Parse(p[0]),Center=int.Parse(p[1]),Negative=int.Parse(p[2]),Positive=int.Parse(p[3])};
            if(!a.Valid)throw new FormatException("Invalid axis range");result.Axes[i]=a;
        }
        if(result.Axes.Select(a=>a.Code).Distinct().Count()!=4)throw new FormatException("Duplicate stick axes");
        return result;
    }
    internal static StickInput Load(string path,Action<string> log) {
        foreach(var p in new[]{path,path+".bak"})try {
            if(!File.Exists(p))continue;if(new FileInfo(p).Length>2048)throw new FormatException("Stick settings too large");
            return Parse(File.ReadAllText(p));
        }catch(Exception e){log("STICK_LOAD "+e.Message);}
        return new StickInput();
    }
    internal void Save(string path) {
        var temp=path+".tmp";File.WriteAllText(temp,Serialize());
        if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);
    }
}

internal sealed class StickCalibration
{
    internal int Step {get;private set;}
    readonly int[] centers=new int[16];
    readonly bool[] seen=new bool[16];
    internal readonly StickAxis[] Result=new StickAxis[4];
    internal bool Complete=>Step==9;
    internal string Prompt=>Step==0?"Move both sticks, then RELEASE them. Press remote OK with both centered.":Complete?"Calibration complete. Test both sticks, then Save & return.":
        "Hold "+new[]{"LEFT stick LEFT","LEFT stick RIGHT","LEFT stick UP","LEFT stick DOWN","RIGHT stick LEFT","RIGHT stick RIGHT","RIGHT stick UP","RIGHT stick DOWN"}[Step-1]+" fully. Press remote OK while holding it.";
    internal bool Confirm(StickInput input,out string error) {
        error=null;if(Complete)return false;
        if(Step==0) {
            if(input.Seen.Count(x=>x)<4){error="Move both sticks in circles, release them, then press remote OK.";return false;}
            Array.Copy(input.Raw,centers,16);Array.Copy(input.Seen,seen,16);Step++;return true;
        }
        int axis=(Step-1)/2;
        if(Step%2==1) {
            int best=-1,magnitude=31;
            for(int c=0;c<16;c++)if(seen[c]&&input.Seen[c]&&!Result.Take(axis).Any(a=>a.Code==c)) {
                int delta=Math.Abs(input.Raw[c]-centers[c]);if(delta>magnitude){best=c;magnitude=delta;}
            }
            if(best<0){error="No clear new axis movement. Hold only the requested stick direction fully.";return false;}
            Result[axis]=new StickAxis{Code=best,Center=centers[best],Negative=input.Raw[best]};
        } else {
            var a=Result[axis];a.Positive=input.Raw[a.Code];
            if(!input.Seen[a.Code]||!a.Valid){error="Hold the SAME stick in the opposite direction, then press remote OK.";return false;}
        }
        Step++;return true;
    }
}
