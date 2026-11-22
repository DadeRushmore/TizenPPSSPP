using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ElmSharp;
using Tizen.Applications;

internal sealed partial class TizenPspApp
{
    LiveConnection live;LiveExperiment experiment;IntPtr liveTimer;
    int liveGeneration;string liveNotice;
    string LiveLabel=>"Live connection: "+(live==null?"Off":"On");
    string PairingPath=>Path.Combine(Application.Current.DirectoryInfo.Data,"tizenpsp-live.txt");
    string LiveEnabledPath=>Path.Combine(Application.Current.DirectoryInfo.Data,"live-enabled.txt");
    bool LiveGameReady()=>game!=null&&game.LiveReady&&!paused&&!terminating&&!reportVisible&&!mapperVisible;
    string[] LiveSnapshot() {
        var rows=new List<string>{session.Context,"LIVE_STATE app_version="+AppVersion.Current+" ready="+(LiveGameReady()?1:0)+" connection="+(live?.Generation??0)+" dropped="+(live?.Dropped??0)};
        if(game!=null){rows.AddRange(game.Diagnostics);rows.Add(game.LiveFramesSummary);}return rows.ToArray();
    }
    bool LiveEmit(string data){Log("LIVE "+data.Split('\n')[0]);return live!=null&&live.Send(data);}
    void SendLiveStatus() {
        if(experiment.Active){live.Send("OK "+experiment.Summary);return;}
        live.Send("STATUS\n"+string.Join("\n",LiveSnapshot()));
        // Fixed app-owned trace tails only, outside measured experiments. This
        // also recovers the last native failure after an interrupted app session.
        string data=Application.Current.DirectoryInfo.Data;
        live.Send("TRACE\n--- jit-native.log ---\n"+DiagnosticFiles.ReadTail(Path.Combine(data,"jit-native.log"),8192)+
            "\n--- jit-native.previous.log ---\n"+DiagnosticFiles.ReadTail(Path.Combine(data,"jit-native.previous.log"),4096)+
            "\n--- jit-attempt.pending ---\n"+DiagnosticFiles.ReadTail(Path.Combine(data,"jit-attempt.pending"),2048)+
            "\n--- core-first-errors.log ---\n"+DiagnosticFiles.ReadTail(Path.Combine(data,"core-first-errors.log"),4096));
    }
    void InitializeLiveConnection(){if(File.Exists(LiveEnabledPath))try{StartLiveConnection(false);}catch(Exception e){liveNotice="Live connection: "+e.Message;}}
    void ToggleLiveConnection() {
        try {
            if(live!=null){StopLiveConnection();File.Delete(LiveEnabledPath);liveNotice="Live connection off";return;}
            StartLiveConnection(true);File.WriteAllText(LiveEnabledPath,"enabled");liveNotice=null;
        } catch(Exception e){StopLiveConnection();liveNotice="Live connection: "+e.Message;Log("LIVE_SETUP_FAILED "+e.GetType().Name);}
    }
    void StartLiveConnection(bool import) {
        string configuration=null;
        if(import)foreach(string folder in allItems.Select(x=>Path.GetDirectoryName(x.Path)).Distinct().Take(32)) {
            string path=Path.Combine(folder,"tizenpsp-live.txt");if(!File.Exists(path))continue;
            if(new FileInfo(path).Length>4096)throw new IOException("Pairing file is too large");
            configuration=File.ReadAllText(path);LivePairing.Parse(configuration);File.WriteAllText(PairingPath,configuration);break;
        }
        if(configuration==null){if(!File.Exists(PairingPath))throw new IOException("Copy the PC pairing file tizenpsp-live.txt to USB /PSP once, then select this again");configuration=File.ReadAllText(PairingPath);}
        var pairing=LivePairing.Parse(configuration);live=new LiveConnection(pairing);liveGeneration=0;
        experiment=new LiveExperiment(()=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency,LiveGameReady,
            ()=>game!=null&&game.LiveSupported,b=>game!=null&&game.SelectLiveVariant(b),b=>game?.RecordLiveFrames(b),LiveSnapshot,LiveEmit,()=>live?.Dropped??0,"command_queue");
        session.LiveLine=live.OfferLog;live.Start();
        liveTimer=EcoreMainloop.AddTimer(.1,()=> {
            try {
                if(live==null||terminating){liveTimer=IntPtr.Zero;return false;}
                experiment.Tick(live.Connected,live.Generation);
                if(live.Connected && liveGeneration!=live.Generation){liveGeneration=live.Generation;SendLiveStatus();Log("LIVE_CONNECTED generation="+liveGeneration);}
                string command;int commandGeneration;
                // Bounded UI work; workers only authenticate and queue commands.
                if(live.TryCommand(out command,out commandGeneration)&&live.Connected&&commandGeneration==live.Generation) {
                    if(command=="STATUS")SendLiveStatus();
                    else if(command=="CANCEL"){experiment.Cancel("requested");live.Send("OK cancelled");}
                    else {
                        string[] parts=command.Split(' ');int duration;
                        string error=parts.Length==3&&parts[0]=="AB"&&int.TryParse(parts[1],out duration)?experiment.Start(duration,parts[2],commandGeneration):"Unknown command";
                        live.Send(error==null?"OK capture_started":"ERROR "+error);
                    }
                }
            } catch(Exception e){experiment?.Cancel("host_error");live?.Send("ERROR live_host_failure");Log("LIVE_HOST_ERROR "+e.GetType().Name);}
            return true;
        });
        Log("LIVE_ENABLED paired_host="+pairing.Host+"; authenticated LAN diagnostics; existing settings retained");
    }
    void StopLiveConnection() {
        experiment?.Cancel("disabled");
        if(liveTimer!=IntPtr.Zero){EcoreMainloop.RemoveTimer(liveTimer);liveTimer=IntPtr.Zero;}
        if(session!=null)session.LiveLine=null;
        live?.Dispose();live=null;experiment=null;Log("LIVE_DISABLED");
    }
}
