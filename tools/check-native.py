"""Compile/run small fake-driver checks on Windows; no GPU/TV claims."""
import argparse,os,subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--zig',required=True);args=p.parse_args()
zig=Path(args.zig).resolve()
env=dict(os.environ,ZIG_GLOBAL_CACHE_DIR=str(root/'.cache/zig-global'),
         ZIG_LOCAL_CACHE_DIR=str(root/'.cache/zig-local'),ZIG_LIB_DIR=str(zig.parent/'lib'))
(root/'build').mkdir(exist_ok=True)
for test in ['native-graphics-tests','glview-clear-tests','libretro-bridge-tests']:
    out=root/'build'/(test+'.exe')
    subprocess.run([str(zig),'cc','-target','x86_64-windows-gnu','-O1','-Wall','-Wextra','-Werror',
                    str(root/'checks'/(test+'.c')),'-o',str(out)],env=env,check=True)
    subprocess.run([str(out)],check=True)
for detailed in (0, 1, 2):
    out=root/f'build/jit-diagnostics-tests-{detailed}.exe'
    subprocess.run([str(zig),'c++','-target','x86_64-windows-gnu','-O1','-Wall','-Wextra','-Werror',
                    f'-DTIZENPSP_DETAILED_PROFILE={detailed}',str(root/'checks/jit-diagnostics-tests.cpp'),str(root/'native/core-build/runtime/jit-diagnostics.cpp'),'-o',str(out)],env=env,check=True)
    subprocess.run([str(out),str(root/'build/jit-diagnostics-test.log')],check=True)
out=root/'build/uniform-cache-tests.exe'
subprocess.run([str(zig),'c++','-target','x86_64-windows-gnu','-O1','-Wall','-Wextra','-Werror',str(root/'checks/uniform-cache-tests.cpp'),'-o',str(out)],env=env,check=True)
subprocess.run([str(out)],check=True)
out=root/'build/guest-error-tests.exe'
subprocess.run([str(zig),'c++','-target','x86_64-windows-gnu','-std=c++17','-O1','-Wall','-Wextra','-Werror',str(root/'checks/guest-error-tests.cpp'),'-o',str(out)],env=env,check=True)
subprocess.run([str(out)],check=True)
out=root/'build/frame-pacing-tests.exe'
subprocess.run([str(zig),'c++','-target','x86_64-windows-gnu','-O1','-Wall','-Wextra','-Werror',
                str(root/'checks/frame-pacing-tests.cpp'),'-o',str(out)],env=env,check=True)
subprocess.run([str(out)],check=True)
import sys
subprocess.run([sys.executable,str(root/'tools/check-vertex-binding.py'),'--zig',str(zig)],check=True)
subprocess.run([sys.executable,str(root/'tools/check-arm-links.py'),'--zig',str(zig)],check=True)
subprocess.run([sys.executable,str(root/'tools/check-codeblock.py'),'--zig',str(zig)],check=True)
subprocess.run([sys.executable,str(root/'tools/check-codecs.py'),'--zig',str(zig)],check=True)
subprocess.run([sys.executable,str(root/'tools/check-parallel-loop.py'),'--zig',str(zig)],check=True)
subprocess.run([sys.executable,str(root/'tools/check-ir-invalidation.py'),'--zig',str(zig)],check=True)
subprocess.run([sys.executable,str(root/'tools/check-invalidation-probe.py'),'--zig',str(zig)],check=True)
subprocess.run([sys.executable,str(root/'tools/check-ir-retention.py'),'--zig',str(zig)],check=True)
