"""Compare isolated managed input work with v0.23.8; not a TV/FPS benchmark."""
import hashlib
import json
import subprocess
import zipfile
from pathlib import Path

root = Path(__file__).resolve().parents[1]
files = ['ControllerActivity.cs', 'StickInput.cs', 'PspInputMap.cs', 'ControllerNavigation.cs']
harness = r'''
using System;
using System.Diagnostics;
using System.Globalization;
class Bench {
    static long sink;
    static void Work(bool diagnostics,int count) {
        var activity=new ControllerActivity();activity.Slots(1,0);
        var map=new PspInputMap();for(int i=0;i<13;i++)map.Assign(i,"button:"+i);
        var nav=new ControllerNavigation();var sticks=new StickInput();var buffer=new short[4];
        for(int i=0;i<count;i++) {
            double now=i*.004;int code=(i%4==3?5:i%4),value=(i*17)%256;
            if(diagnostics) {
                activity.Record(1,3,code,value,now);
                if(i%1250==0)sink+=activity.Snapshot(now).Length;
            } else {
                int type=i%4==0?1:3;code=type==1?(i/8)%13:code;value=type==1?(i/4)%2:value;
                map.ObserveRaw(type,code,value);sticks.Observe(type,code,value);nav.Observe(map,type,code,value);
                nav.Next(now,false,false,ControllerNavigation.Horizontal);
                // Event update plus the existing periodic/main-loop update.
                for(int repeat=0;repeat<2;repeat++) {
#if AFTER
                    sticks.WriteValues(buffer);var axes=buffer;
#else
                    var axes=sticks.Values;
#endif
                    sink+=nav.GameButtons(map.Held)|sticks.Route(axes);sink+=axes[0]+axes[1]+axes[2]+axes[3];
                }
            }
        }
    }
    static void Main() {
        CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("en-US");
        foreach(bool diagnostics in new[]{true,false}) {
            Work(diagnostics,10000);
            for(int trial=0;trial<5;trial++) {
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                long before=GC.GetAllocatedBytesForCurrentThread();var timer=Stopwatch.StartNew();
                Work(diagnostics,100000);timer.Stop();long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
                Console.WriteLine((diagnostics?"diagnostics":"mapping")+","+trial+","+timer.Elapsed.TotalMilliseconds.ToString("F3",CultureInfo.InvariantCulture)+","+bytes);
            }
        }
        Console.WriteLine("checksum,"+sink);
    }
}
'''
results = {'scope': 'Windows .NET Core 3.1 isolated managed work; 100000 events/trial; includes 5-second diagnostic snapshots at 250 events/s. No SDK I/O, native calls, TV rendering or game FPS measured.', 'runs': {}}
with zipfile.ZipFile(root/'releases/v0.23.8/TizenPSP-0.23.8-full-source.zip') as old:
    for label in ['before', 'after']:
        folder=root/'build'/('input-benchmark-v0239-'+label)
        folder.mkdir(parents=True, exist_ok=True)
        hashes={}
        for name in files:
            data=old.read('app/'+name) if label=='before' else (root/'app'/name).read_bytes()
            (folder/name).write_bytes(data);hashes[name]=hashlib.sha256(data).hexdigest()
        (folder/'Program.cs').write_text(harness)
        (folder/'Bench.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>netcoreapp3.1</TargetFramework><DefineConstants>'+('AFTER' if label=='after' else 'BEFORE')+'</DefineConstants></PropertyGroup></Project>')
        subprocess.run(['dotnet','build',str(folder/'Bench.csproj'),'-c','Release','--nologo','-v','quiet'],cwd=root,check=True)
        output=subprocess.check_output(['dotnet',str(folder/'bin/Release/netcoreapp3.1/Bench.dll')],cwd=root,text=True)
        results['runs'][label]={'source_sha256':hashes,'samples':output.strip().splitlines()}
assert results['runs']['before']['samples'][-1]==results['runs']['after']['samples'][-1], 'Mapped output or diagnostics changed'
target=root/'docs/input-benchmark-v0239.json'
target.write_text(json.dumps(results,indent=2)+'\n')
print(json.dumps(results,indent=2))
