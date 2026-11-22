"""Initialize the bundled video decoder on Windows; no TV/movie playback claim."""
import argparse, os, subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--zig',required=True);args=p.parse_args()
zig=Path(args.zig).resolve()
work=root/'build/codec-check';work.mkdir(parents=True,exist_ok=True)
env=dict(os.environ,ZIG_GLOBAL_CACHE_DIR=str(root/'.cache/zig-global'),ZIG_LOCAL_CACHE_DIR=str(root/'.cache/zig-local'),ZIG_LIB_DIR=str(zig.parent/'lib'))
for name,command in [('cc','cc -target x86_64-windows-gnu'),('ar','ar'),('ranlib','ranlib')]:
    (work/(name+'.cmd')).write_bytes(('@echo off\r\n"'+str(zig)+'" '+command+' %*\r\n').encode())
cmake=str(root/'build/core-tools/cmake/data/bin/cmake.exe')
commands=[
    [cmake,'-S',str(root/'checks/ffmpeg'),'-B',str(work),'-G','Ninja','-DCMAKE_MAKE_PROGRAM='+str(root/'build/core-tools/bin/ninja.exe'),
     '-DCMAKE_BUILD_TYPE=Release','-DCMAKE_C_COMPILER='+str(work/'cc.cmd'),'-DCMAKE_AR='+str(work/'ar.cmd'),'-DCMAKE_RANLIB='+str(work/'ranlib.cmd')],
    [cmake,'--build',str(work),'--target','codec-check','-j','2']]
for command in commands:
    with (work/'build.log').open('w') as log:
        result=subprocess.run(command,env=env,stdout=log,stderr=subprocess.STDOUT)
    if result.returncode:
        print((work/'build.log').read_text(errors='replace')[-7000:]);raise SystemExit(result.returncode)
subprocess.run([str(work/'codec-check.exe')],check=True)
