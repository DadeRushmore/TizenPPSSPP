using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static class LiveChecks
{
    // Deliberately synthetic workload for cross-language transport/analysis testing.
    // It does not execute a PSP game or measure any hardware performance gain.
    internal static void Integration(string configuration) {
        var live=new LiveConnection(LivePairing.Parse(File.ReadAllText(configuration)));live.Start();
        double now=0;bool variant=true;long guest=0,flips=0,cpu=0,render=0,dispatch=0,fast=0,fallback=0;
        var frame=new LiveFrameWindow();bool finished=false;
        Func<string[]> snapshot=()=>new[]{
            "SESSION id=synthetic app_version=fixture synthetic_test=1",
            "game_file=synthetic_fixture core_sha256=fixture resolution=480x272 frameskip=0",
            "LIVE_STATE ready=1 connection="+live.Generation+" dropped="+live.Dropped,
            "JIT_NATIVE profile_enabled=2 process_cpu_valid=1 perf_clock_errors=0 process_cpu_us="+cpu+" render_cpu_us="+render+" dispatch_cpu_us="+dispatch+
                " command_queue_requested="+(variant?1:0)+" command_queue_supported=1 command_queue_queued="+(fast+fallback)+" command_queue_skipped="+fast+" vertex_binding_requested=1 vertex_binding_supported=1 vertex_binding_fast_draws="+fast+" vertex_binding_fallback_draws="+fallback+
                " prof_hle_calls="+((long)Math.Round(now*10)*10)+" prof_hle_samples="+(long)Math.Round(now*10)+" prof_hle_cpu_us="+((long)Math.Round(now*10)*100),
            "CORE_PROGRESS guest_ms="+guest+" actual_flips="+flips,frame.Summary};
        var experiment=new LiveExperiment(()=>now,()=>true,()=>true,b=>{variant=b;return true;},frame.Record,snapshot,
            data=>{if(data.StartsWith("AB_DONE "))finished=true;return live.Send(data);},()=>live.Dropped,"command_queue");
        var deadline=System.Diagnostics.Stopwatch.StartNew();int generation=0;
        try {
            while(!finished&&deadline.Elapsed.TotalSeconds<25) {
                now+=.1;guest+=variant?60:50;flips+=variant?6:5;cpu+=variant?40000:50000;render+=variant?8000:10000;dispatch+=20000;
                if(variant)fast+=10;else fallback+=10;
                frame.Observe(0,now);frame.Observe(1,now+.005);
                experiment.Tick(live.Connected,live.Generation);
                if(live.Connected&&generation!=live.Generation){generation=live.Generation;live.Send("STATUS\n"+string.Join("\n",snapshot()));}
                string command;
                if(live.TryCommand(out command)) {
                    if(command=="STATUS")live.Send("STATUS\n"+string.Join("\n",snapshot()));
                    else if(command=="CANCEL")experiment.Cancel("requested");
                    else {string[] parts=command.Split(' ');string error=parts.Length==3?experiment.Start(int.Parse(parts[1]),parts[2],live.Generation):"command";live.Send(error==null?"OK capture_started":"ERROR "+error);}
                }
                live.OfferLog("synthetic fixture log 日本語");Thread.Sleep(10);
            }
            Need(finished,"Cross-language fixture did not finish: "+live.Status);Thread.Sleep(500);
        } finally {live.Dispose();live.WaitStopped(4000);}
        Console.WriteLine("PASS: synthetic C# client / Python receiver capture complete; no device performance claim.");
    }
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static void Reject(Action action){try{action();}catch(Exception){return;}throw new Exception("Invalid live input accepted");}
    static string Config(string host,int port)=>"TIZENPSP_LIVE_V1\nhost="+host+"\nport="+port+"\nkey="+new string('1',64)+"\n";
    internal static void Run() {
        var pairing=LivePairing.Parse(Config("127.0.0.1",27941));
        foreach(string host in new[]{"10.1.2.3","172.16.0.2","192.168.1.2","169.254.1.2"})LivePairing.Parse(Config(host,27941));
        foreach(string host in new[]{"8.8.8.8","0.0.0.0","224.0.0.1","::1","example.com"})Reject(()=>LivePairing.Parse(Config(host,27941)));
        Reject(()=>LivePairing.Parse(Config("127.0.0.1",80)));Reject(()=>LivePairing.Parse(Config("127.0.0.1",27941)+"key=bad"));
        Need(LiveWire.Hex(LiveWire.Mac(Enumerable.Repeat((byte)0x0b,20).ToArray(),"Hi There"))=="b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7","HMAC RFC vector");
        string packed=LiveWire.Pack(pairing.Key,"T",1,"hello\nPSP 日本語");
        Need(LiveWire.Unpack(pairing.Key,"T",1,packed)=="hello\nPSP 日本語","Wire UTF8 roundtrip");
        Reject(()=>LiveWire.Unpack(pairing.Key,"P",1,packed));Reject(()=>LiveWire.Unpack(pairing.Key,"T",2,packed));
        Reject(()=>LiveWire.Unpack(new byte[32],"T",1,packed));Reject(()=>LiveWire.Unpack(pairing.Key,"T",1,packed.Substring(0,packed.Length-1)+"0"));
        Reject(()=>LiveWire.Pack(pairing.Key,"T",1,new string('x',32769)));
        var reader=new LiveReader(new MemoryStream(Encoding.ASCII.GetBytes("first\nsecond\n")));
        Need(reader.Read()=="first"&&reader.Pending&&reader.Read()=="second","Buffered coalesced frames");
        Reject(()=>new LiveReader(new MemoryStream(Encoding.ASCII.GetBytes("long\n"))).Read(3));
        Reject(()=>new LiveReader(new MemoryStream(new byte[]{0,10})).Read());
        Network(pairing.Key);
        Experiment();
        Console.WriteLine("PASS: live HMAC vectors, framing/authentication/replay rejection, real TCP worker roundtrip, bounded backpressure/stop, A/B ordering/cancellation and phase frame metrics.");
    }
    static void Network(byte[] secret) {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        int port=((IPEndPoint)listener.LocalEndpoint).Port;
        var ready=new ManualResetEventSlim();var finish=new ManualResetEventSlim();
        var server=Task.Run(()=> {
            using(var connection=listener.AcceptTcpClient()) {
                var stream=connection.GetStream();stream.ReadTimeout=4000;stream.WriteTimeout=4000;var reader=new LiveReader(stream);
                string pc=new string('a',64);LiveWire.Write(stream,"CHALLENGE "+pc);
                string[] hello=reader.Read(200).Split(' ');Need(hello.Length==3&&hello[0]=="HELLO","Handshake");
                string context=pc+"|"+hello[1];Need(LiveWire.Equal(hello[2],LiveWire.Hex(LiveWire.Mac(secret,"TV|"+context))),"Client authentication");
                byte[] key=LiveWire.Mac(secret,"SESSION|"+context);
                LiveWire.Write(stream,"WELCOME "+LiveWire.Hex(LiveWire.Mac(secret,"PC|"+context))+"\n"+LiveWire.Pack(key,"P",1,"PING")+"\n"+LiveWire.Pack(key,"P",2,"STATUS"));
                bool pong=false,control=false,log=false;ulong seq=0;
                while(!(pong&&control&&log)) {
                    string value=LiveWire.Unpack(key,"T",++seq,reader.Read());
                    pong|=value=="PONG";control|=value=="CHECK\n日本語";log|=value=="LOG\nfixture line";
                }
                ready.Set();finish.Wait(4000); // Stop reading so the bounded queue must shed load.
            }
        });
        var client=new LiveConnection(LivePairing.Parse(Config("127.0.0.1",port)));client.Start();
        try {
            string command=null;
            Need(SpinWait.SpinUntil(()=>client.TryCommand(out command),4000)&&command=="STATUS","Authenticated command delivery");
            Need(client.Connected&&client.Generation==1,"Connection generation");
            client.Send("CHECK\n日本語");client.OfferLog("fixture line");Need(ready.Wait(4000),"Server received signed messages");
            string large=new string('x',16000);for(int i=0;i<2000;i++)client.OfferLog(large);
            Need(client.Dropped>0,"Slow receiver must not grow the queue indefinitely");
            client.Dispose();Need(client.WaitStopped(4000),"Stop must interrupt blocked network IO");
            Need(!client.Connected&&!client.TryCommand(out command),"Disconnect clears queued commands");
        } finally {client.Dispose();finish.Set();listener.Stop();}
        Need(server.Wait(5000),"Server completion");server.GetAwaiter().GetResult();
    }
    static void Experiment() {
        double now=0;bool ready=true,supported=true,accept=true;var events=new List<string>();var choices=new List<bool>();var recording=new List<bool>();
        var e=new LiveExperiment(()=>now,()=>ready,()=>supported,b=>{choices.Add(b);return true;},b=>recording.Add(b),()=>new[]{"fixture"},s=>{events.Add(s);return accept;},()=>0);
        Need(e.Start(9,"ABBA",1)!=null&&!e.Active,"Invalid duration");supported=false;Need(e.Start(10,"ABBA",1)!=null&&!e.Active,"Unsupported context");supported=true;
        Need(e.Start(10,"ABBA",1)==null,"Capture start");Need(e.Start(10,"BAAB",1)!=null,"Busy capture");
        now=2.99;e.Tick(true,1);Need(events.Count==1,"Warmup exclusion");
        for(int i=0;i<4;i++){now=i*13+3;e.Tick(true,1);now+=10;e.Tick(true,1);}
        Need(!e.Active&&choices.SequenceEqual(new[]{false,true,true,false,true}),"ABBA sequence and default restore");
        Need(events.Count==10&&events[9].StartsWith("AB_DONE "),"Endpoint/done count");
        Need(recording.Count(x=>x)==4,"Four frame windows");
        foreach(string order in new[]{"ABBA","BAAB"}) {
            e.Start(10,order,1);ready=false;e.Tick(true,1);Need(!e.Active&&choices.Last()&&events.Last().Contains("game_paused"),"Pause restores B");ready=true;
            e.Start(10,order,1);e.Tick(true,2);Need(!e.Active&&choices.Last()&&events.Last().Contains("connection_lost"),"Reconnect aborts");
            e.Start(10,order,1);e.Cancel("requested");Need(!e.Active&&choices.Last(),"Cancel restores B");
        }
        accept=false;Need(e.Start(10,"ABBA",1)!=null&&!e.Active&&choices.Last(),"Backpressure restores default");
        var f=new LiveFrameWindow();f.Record(true);f.Observe(0,0);f.Observe(1,.01);f.Observe(0,.02);f.Observe(1,.03);f.Record(false);
        Need(f.Summary.Contains("runs=2")&&f.Summary.Contains("run_ms_p95=10.000")&&f.Summary.Contains("spacing_ms_p95=20.000"),"Exact phase metrics");
        f.Observe(0,100);f.Observe(1,101);Need(f.Summary.Contains("runs=2"),"Disabled frame collection");
        f.Record(true);for(int i=0;i<8200;i++){f.Observe(0,i);f.Observe(1,i+.01);}Need(f.Summary.Contains("runs=8192")&&!f.Summary.Contains("overflow=0"),"Bounded frames");
    }
}
