using System;

// Keep physical menu holds separate from gameplay holds cleared on UI transitions.
// All actions use the saved PSP bindings. Native PS5 defaults map Circle to Back;
// an explicitly chosen custom binding remains supported.
internal sealed class ControllerNavigation
{
    readonly PspInputMap physical=new PspInputMap();
    uint previous;
    string repeatKey;
    double repeatAt;
    bool previousMenu,previousStats,waitForRelease,consumeStats;
    bool directFaces;
    uint faceBits;
    internal void ObserveFaces(bool active,uint bits){directFaces=active;faceBits=active?bits:0;}
    internal const uint StatsChord=(1u<<2)|(1u<<10)|(1u<<11);
    internal const uint Directions=(1u<<4)|(1u<<5)|(1u<<6)|(1u<<7);
    internal const uint Horizontal=(1u<<6)|(1u<<7);
    internal void Observe(PspInputMap map,int type,int code,int value) {
        Array.Copy(map.Bindings,physical.Bindings,map.Bindings.Length);
        physical.ObserveRaw(type,code,value);
    }
    internal void Reset() {physical.ClearHeld();previous=faceBits=0;directFaces=false;repeatKey=null;previousMenu=previousStats=waitForRelease=consumeStats=false;}
    internal void BlockHeld() {waitForRelease=!physical.RawNeutral||faceBits!=0;repeatKey=null;}
    internal static string KeyForBits(uint bits) {
        if((bits&(1u<<8))!=0)return "XF86Back";
        if((bits&1)!=0)return "Return";
        if((bits&Horizontal)==(1u<<6))return "Left";
        if((bits&Horizontal)==(1u<<7))return "Right";
        uint vertical=(1u<<4)|(1u<<5);
        if((bits&vertical)==(1u<<4))return "Up";
        if((bits&vertical)==(1u<<5))return "Down";
        return null;
    }
    internal string Next(double now,bool menuVisible,bool capturing,uint repeatMask) {
        // Gate physical tokens, not mapped bits: learning may assign a held token
        // that had no action at the time the transition began.
        if(waitForRelease) {
            if(physical.RawNeutral&&faceBits==0){waitForRelease=false;previous=0;previousMenu=previousStats=consumeStats=false;}
            return null;
        }
        uint bits=directFaces?physical.HeldExcept("button:2")|faceBits:physical.RawHeld,pressed=bits&~previous;previous=bits;
        if((bits&StatsChord)==0)consumeStats=false;
        bool menu=(bits&(1u<<20))!=0 || (bits&12)==12; // App menu or Start + Select.
        bool open=menu&&!previousMenu;previousMenu=menu;
        bool stats=(bits&StatsChord)==StatsChord,toggle=stats&&!previousStats;previousStats=stats;
        if(capturing){repeatKey=null;return null;}
        if(open){repeatKey=null;return "AppMenu";}
        if(toggle&&!menu&&!menuVisible&&!consumeStats){consumeStats=true;repeatKey=null;return "ToggleStats";}
        if(!menuVisible||menu){repeatKey=null;return null;}
        string action=KeyForBits(pressed & ~Directions);
        string direction=KeyForBits(bits & Directions);
        if(action!=null){repeatKey=null;return action;}
        if(direction!=repeatKey) {
            repeatKey=direction;repeatAt=now+.45;
            // Opposite releases can reveal an older hold: don't treat that as a new press.
            return direction!=null && KeyForBits(pressed&Directions)==direction?direction:null;
        }
        if(direction!=null && now>=repeatAt && KeyForBits(bits&repeatMask)==direction) {
            repeatAt=now+.15;return direction;
        }
        return null;
    }
    internal uint GameButtons(uint bits)=>consumeStats?bits&~StatsChord:bits;
}
