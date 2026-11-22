"""Test exact-byte sampling and extracted request propagation with modeled guest state."""
import argparse,os,subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--zig',required=True);a=p.parse_args()
s=(root/'vendor/ppsspp/Core/MIPS/MIPS.cpp').read_text()
def method(name):
 start=s.index('void MIPSState::'+name+'(');end=s.index('\n}\n',start)+3;return s[start:end]
(root/'build/invalidation-request-methods.h').write_text('\n'.join(method(n) for n in ('InvalidateICacheRangeImmediate','InvalidateICacheRangeDeferred','ProcessPendingInvalidates')))
# Use the real declarations, including the call-site default source expression.
h=(root/'vendor/ppsspp/Core/MIPS/MIPS.h').read_text()
import re
parts=[]
for n in ('Immediate','Deferred'):
 parts.append(re.search(r'void InvalidateICacheRange'+n+r'\(.*?\);',h,re.S)[0])
(root/'build/invalidation-request-declarations.h').write_text('\n'.join(parts))
ir=(root/'vendor/ppsspp/Core/MIPS/IR/IRJit.cpp').read_text()
start=ir.index('void IRJit::InvalidateCacheAt(');end=ir.index('\n}\n',start)+3
(root/'build/invalidation-observe-method.h').write_text(ir[start:end])
zig=Path(a.zig).resolve();env=dict(os.environ,ZIG_GLOBAL_CACHE_DIR=str(root/'.cache/zig-global'),ZIG_LOCAL_CACHE_DIR=str(root/'.cache/zig-local'),ZIG_LIB_DIR=str(zig.parent/'lib'))
out=root/'build/invalidation-probe-tests.exe'
subprocess.run([str(zig),'c++','-target','x86_64-windows-gnu','-std=c++17','-O1','-Wall','-Wextra','-Werror','-DTIZENPSP_ARM_JIT',str(root/'checks/invalidation-probe-tests.cpp'),'-o',str(out)],env=env,check=True)
subprocess.run([str(out)],check=True)
