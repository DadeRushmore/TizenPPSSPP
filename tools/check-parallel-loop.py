"""Compile the actual upstream synchronous loop entry point against task test doubles."""
import argparse, os, subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--zig', required=True);a=p.parse_args()
zig=Path(a.zig).resolve()
source=(root/'vendor/ppsspp/Common/Thread/ParallelLoop.cpp').read_text(encoding='utf-8')
function=source[source.index('void ParallelRangeLoop('):source.index('void ParallelMemcpy(')]
(root/'build/parallel-loop-under-test.h').write_text(function, encoding='utf-8')
env=dict(os.environ,ZIG_GLOBAL_CACHE_DIR=str(root/'.cache/zig-global'),ZIG_LOCAL_CACHE_DIR=str(root/'.cache/zig-local'),ZIG_LIB_DIR=str(zig.parent/'lib'))
target=root/'build/parallel-loop-tests.exe'
subprocess.run([str(zig),'c++','-target','x86_64-windows-gnu','-std=c++17','-O1','-Wall','-Wextra','-Werror',
               '-I'+str(root/'build'),str(root/'checks/parallel-loop-tests.cpp'),'-o',str(target)],env=env,check=True)
subprocess.run([str(target)],check=True)
