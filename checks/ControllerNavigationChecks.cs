using System;
internal static class ControllerNavigationChecks {
    static void Need(bool ok,string message){if(!ok)throw new Exception(message);}
    internal static void Run() {
        var map=new PspInputMap();map.Assign(0,"button:1");map.Assign(1,"button:13");map.Assign(3,"button:2");
        map.Assign(6,"button:9");map.Assign(7,"button:8");map.Assign(8,"hat:17:-1");map.Assign(9,"hat:17:1");
        map.Assign(10,"hat:16:-1");map.Assign(11,"hat:16:1");
        var n=new ControllerNavigation();
        n.Observe(map,3,16,1);Need(n.Next(1,true,false,ControllerNavigation.Horizontal)=="Right","Startup controller browse");
        Need(n.Next(1.4,true,false,ControllerNavigation.Horizontal)==null,"Initial repeat delay");
        Need(n.Next(1.46,true,false,ControllerNavigation.Horizontal)=="Right","Held direction repeat");
        n.Observe(map,3,16,0);Need(n.Next(2,true,false,ControllerNavigation.Horizontal)==null,"Release stops repeat");
        n.Observe(map,1,1,1);Need(n.Next(3,true,false,0)=="Return","Cross opens details");
        n.Observe(map,1,1,2);Need(n.Next(4,true,false,0)==null,"Held Cross must not start game from details");
        n.BlockHeld();map.ClearHeld();n.Observe(map,3,0,129);Need(n.Next(5,true,false,0)==null,"UI clear/analog noise must not rearm held Cross");
        n.Observe(map,1,1,0);n.Next(6,true,false,0);n.Observe(map,1,1,1);Need(n.Next(7,true,false,0)=="Return","Release and repress confirms");
        n.Reset();n.Observe(map,1,2,1);Need(n.Next(8,true,false,0)==null,"Colliding physical Circle/Triangle cannot become Back");
        n.Observe(map,1,13,1);Need(n.Next(9,true,false,0)=="XF86Back","R3 saved Circle is Back");
        n.Reset();n.Observe(map,3,17,1);Need(n.Next(10,true,false,ControllerNavigation.Horizontal)=="Down","Down action");
        Need(n.Next(11,true,false,ControllerNavigation.Horizontal)==null,"Don't repeatedly rescan/export while Down held");
        n.Reset();n.Observe(map,1,1,1);Need(n.Next(12,true,true,0)==null,"Capture does not navigate");
        n.BlockHeld();Need(n.Next(13,true,false,0)==null,"Captured Cross does not reopen learning");
        n.Reset();n.Observe(map,1,9,1);Need(n.Next(14,false,false,0)==null,"Start remains game input");
        n.Observe(map,1,8,1);Need(n.Next(15,false,false,0)=="AppMenu","Start+Select opens pause");
        n.BlockHeld();Need(n.Next(16,true,false,0)==null,"Pause chord cannot immediately close menu");
        n.Reset();n.Observe(map,1,1,1);Need(n.Next(17,false,false,0)==null,"Gameplay Cross does not navigate");
        Need(n.Next(18,true,false,0)==null,"Existing game hold cannot confirm opened pause");
        n.Reset();Need(n.Next(19,true,false,ControllerNavigation.Directions)==null,"Disconnect clears repeat");
        n.Observe(map,1,22,1);Need(n.Next(20,true,true,0)==null,"Unassigned learning token");
        n.BlockHeld();map.Assign(0,"button:22");n.Observe(map,3,0,128);
        Need(n.Next(21,true,false,0)==null,"Newly assigned held token must not reopen learning");
        n.Observe(map,1,22,0);n.Next(22,true,false,0);n.Observe(map,1,22,1);
        Need(n.Next(23,true,false,0)=="Return","Learned token works after release");
        Need(ControllerNavigation.KeyForBits((1u<<6)|(1u<<7))==null,"Opposite directions cancel");
        map.Assign(4,"button:4");map.Assign(5,"button:5");n.Reset();
        n.Observe(map,1,8,1);Need(n.Next(24,false,false,0)==null,"Select alone is game input");
        n.Observe(map,1,4,1);Need(n.Next(25,false,false,0)==null,"Partial HUD chord is game input");
        n.Observe(map,1,5,1);Need(n.Next(26,false,false,0)=="ToggleStats","Select+L+R toggles stats during play");
        Need(n.Next(27,false,false,0)==null,"Held stats chord must toggle only once");
        Need(n.GameButtons(ControllerNavigation.StatsChord|1)==1,"Accepted HUD chord must not reach PSP; other buttons preserved");
        n.Observe(map,1,4,0);n.Next(28,false,false,0);n.Observe(map,1,4,1);
        Need(n.Next(29,false,false,0)==null && n.GameButtons((1u<<2)|(1u<<11))==0,"Consume until all shortcut buttons are released");
        n.Observe(map,1,4,0);n.Observe(map,1,5,0);n.Observe(map,1,8,0);n.Next(29.1,false,false,0);
        Need(n.GameButtons(ControllerNavigation.StatsChord)==ControllerNavigation.StatsChord,"Unrecognized key-bound chord is not silently eaten");
        n.Observe(map,1,4,1);n.Observe(map,1,5,1);n.Observe(map,1,8,1);
        Need(n.Next(29.2,false,false,0)=="ToggleStats","Repress after full release toggles stats again");
        n.Reset();n.Observe(map,1,8,1);n.Observe(map,1,4,1);n.Observe(map,1,5,1);
        Need(n.Next(30,true,true,0)==null && n.Next(31,false,false,0)==null,"Capture cannot leak a HUD chord into gameplay");
        n.Reset();n.Observe(map,1,8,1);n.Observe(map,1,4,1);n.Observe(map,1,5,1);n.Observe(map,1,9,1);
        Need(n.Next(32,false,false,0)=="AppMenu","Pause chord takes priority over overlay");
        Console.WriteLine("PASS: controller library navigation, saved R3 Back, repeat/release gates, capture isolation and pause chord");
    }
}
