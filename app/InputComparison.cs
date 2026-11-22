using System;
using System.Linq;
using System.Collections.Generic;

// Capture complete press/release samples, not just the first event in a burst.
internal sealed class InputComparison
{
    internal static readonly int[] Actions={0,1,3,6};
    internal readonly HashSet<string>[] Raw=new HashSet<string>[4],Keys=new HashSet<string>[4];
    internal int Step {get;private set;}
    internal bool Finished=>Step==4;
    internal string Error;
    double arm,first=-1,last,deadline;
    internal InputComparison(double now){for(int i=0;i<4;i++){Raw[i]=new HashSet<string>();Keys[i]=new HashSet<string>();}Arm(now);}
    void Arm(double now){arm=now+.65;first=-1;deadline=now+20;}
    internal int Action=>Actions[Math.Min(Step,3)];
    internal void Observe(string token,bool raw,double time) {
        if(Finished||time<arm||token==null)return;
        if(first<0)first=time;last=time;(raw?Raw[Step]:Keys[Step]).Add(token);
    }
    internal void Tick(double now,bool neutral) {
        if(Finished)return;
        if(now>deadline){Error="No clean press/release for "+PspInputMap.Names[Action]+"; comparison cancelled";Step=4;return;}
        if(first>=0 && neutral && now-first>=.6 && now-last>=.35){Step++;if(!Finished)Arm(now);}
    }
    internal string Prompt(double now) {
        if(Finished) {
            string error;var result=Resolve(out error);
            return result==null?error+" Back returns to the mapper.":"Distinct inputs found. OK applies; Back keeps the old map. Test all buttons afterward.";
        }
        return now<arm?"Release all controls. Next: "+PspInputMap.Names[Action]:"CHECK "+(Step+1)+"/4: press and release "+PspInputMap.Names[Action]+" once. Do not use the TV remote.";
    }
    internal string Summary=>string.Join(" | ",Actions.Select((a,i)=>PspInputMap.Names[a]+": raw["+string.Join(",",Raw[i])+"], keys["+string.Join(",",Keys[i])+"]"));
    internal string[] Resolve(out string error) {
        error=Error;if(!Finished||error!=null){error=error??"Comparison not complete";return null;}
        var result=new string[4];
        for(int i=0;i<4;i++) {
            var raw=Raw[i].Where(t=>!Raw.Where((x,n)=>n!=i).Any(s=>s.Contains(t))).ToArray();
            var keys=Keys[i].Where(t=>!Keys.Where((x,n)=>n!=i).Any(s=>s.Contains(t))).ToArray();
            if(raw.Length==1)result[i]=raw[0];
            else if(raw.Length==0 && keys.Length==1)result[i]=keys[0];
            else {error="Cannot uniquely identify "+PspInputMap.Names[Actions[i]]+". Nothing changed; keep the comparison report.";return null;}
        }
        return result;
    }
    internal bool Apply(PspInputMap map,out string error) {
        var plan=Resolve(out error);if(plan==null)return false;
        for(int i=0;i<plan.Length;i++) {
            int owner=map.Owner(plan[i]);
            if(owner>=0&&!Actions.Contains(owner)){error=plan[i]+" already belongs to "+PspInputMap.Names[owner]+". Nothing changed.";return false;}
        }
        foreach(int a in Actions)map.Bindings[a]=null;
        for(int i=0;i<plan.Length;i++)map.Assign(Actions[i],plan[i]);
        map.ClearHeld();return true;
    }
}
