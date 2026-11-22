using System;
internal static class InputMapChecks {
    static void Check(bool pass,string message) {if(!pass)throw new Exception(message);}
    internal static void Run() {
        var m=new PspInputMap();m.Assign(6,"button:9");m.Assign(7,"button:8");m.Assign(0,"button:1");
        m.ObserveRaw(1,9,1);m.ObserveRaw(1,8,1);Check(m.Held==12,"Start/Select Libretro bits");
        m.ObserveRaw(3,0,128);Check(m.Held==12,"Unrelated analog event released buttons");
        m.ObserveRaw(1,9,0);Check(m.Held==4,"Independent releases");m.ClearHeld();
        m.Assign(8,"hat:17:-1");m.Assign(9,"hat:17:1");m.ObserveRaw(3,17,-1);Check(m.Held==(1u<<4),"D-pad up");
        m.ObserveRaw(3,17,1);Check(m.Held==(1u<<5),"D-pad direction reversal");m.ObserveRaw(3,17,0);Check(m.Held==0,"D-pad neutral");
        m.ObserveRaw(1,1,1);m.Assign(1,"button:1");Check(m.Bindings[0]==null && m.Held==(1u<<8),"Conflict moves physical binding; held state retained for release gate");
        m.ObserveRaw(1,1,0);Check(m.Neutral,"Capture release gate");
        m.Assign(0,"key:XF86Back");Check(m.MappedKey("XF86Back"),"Explicit bindings override menu key names");
        var copy=PspInputMap.Parse(m.Serialize());Check(copy.Serialize()==m.Serialize(),"Map persistence");
        Check(!PspInputMap.Valid("hat:17:0")&&!PspInputMap.Valid("button:999")&&!PspInputMap.Valid("key:"),"Malformed bindings");
        Check(PspInputMap.Echo(10,10.1)&&PspInputMap.Echo(10,9.9)&&!PspInputMap.Echo(10,11),"Window/controller correlation");
        m.ClearHeld();Check(m.Held==0,"Pause clears all input");
        var live=new PspInputMap();live.Assign(1,"button:2");live.Assign(3,"button:3");
        live.ObserveRaw(1,2,1);Check(PspInputMap.Describe(live.Held)=="Circle","Live display Circle independent of selected row");
        live.ObserveRaw(1,2,0);Check(live.Held==0&&PspInputMap.Describe(live.LastPressed)=="Circle","Last press is explicitly released");
        string conflict;Check(!live.TryAssign(3,"button:2",out conflict)&&conflict=="Circle"&&live.Bindings[3]=="button:3","Conflict must preserve both mappings");
        live.ObserveRaw(1,3,1);Check(PspInputMap.Describe(live.Held)=="Triangle","Triangle has distinct bit");
        live.ObserveRaw(3,0,128);Check(live.LastRaw.Contains("1 / 3 / 1"),"Analog noise must not erase button evidence");
        var cmp=new InputComparison(0);double now=1;
        string[] raw={"button:1","button:2","button:2","button:9"};
        string[] keys={"key:XF86Back","key:CircleUnique","key:TriangleUnique","key:StartUnique"};
        for(int i=0;i<4;i++){cmp.Observe(raw[i],true,now);cmp.Observe(keys[i],false,now);cmp.Tick(now+.7,true);now+=2;}
        string problem;var resolved=cmp.Resolve(out problem);Check(resolved!=null && resolved[1]==keys[1]&&resolved[2]==keys[2],"Shared raw code must use independently observed keys");
        var fixedMap=new PspInputMap();Check(cmp.Apply(fixedMap,out problem),"Apply comparison atomically");
        cmp.Keys[2].Clear();cmp.Keys[2].Add(keys[1]);Check(cmp.Resolve(out problem)==null,"Identical raw and key identities must remain a reported blocker");
        string before=fixedMap.Serialize();Check(!cmp.Apply(fixedMap,out problem)&&fixedMap.Serialize()==before,"Failed comparison must leave every binding unchanged");
        Console.WriteLine("PASS: PSP Start/Select IDs, independent holds/releases, hats, conflicts, persistence and echo policy");
    }
}
