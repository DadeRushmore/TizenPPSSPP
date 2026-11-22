"""Compile production retention/loop methods against controlled guest/frontend state."""
import argparse, os, subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--zig',required=True);a=p.parse_args()
s=(root/'vendor/ppsspp/Core/MIPS/IR/IRJit.cpp').read_text()
def method(signature):
 start=s.index(signature+'(');end=s.index('\n}\n',start)+3;return s[start:end]
body='\n'.join(method(x) for x in ('static bool TizenRetainableIR','bool IRJit::TizenRetentionEnabled',
 'TizenIRRetention::Context IRJit::TizenRetentionContext','bool IRJit::TizenTryRetainBlock',
 'void IRJit::InvalidateCacheAt','void IRJit::InvalidateBlock','void IRJit::ClearCache'))
(root/'build').mkdir(exist_ok=True)
header=root/'build/retention-methods.h'; header.write_text(body)
ir=(root/'vendor/ppsspp/Core/MIPS/IR/IRInst.h').read_text()
types='using IRReg = uint8_t;\n'
for sig in ('enum class IROp : uint8_t {','struct IRInst {'):
 start=ir.index(sig);end=ir.index('\n};',start)+3;types+=ir[start:end]+'\n'
(root/'build/retention-ir-types.h').write_text(types)
compile=method('IRJit::CompileResult IRJit::CompileBlock')
assert compile.index('FinalizeTargetBlock') < compile.index('tizenRetention_.Capture')
assert 'TizenRetainableIR(instructions)' in compile and 'TizenRetentionEnabled()' in compile
zig=Path(a.zig).resolve();env=dict(os.environ,ZIG_GLOBAL_CACHE_DIR=str(root/'.cache/zig-global'),ZIG_LOCAL_CACHE_DIR=str(root/'.cache/zig-local'),ZIG_LIB_DIR=str(zig.parent/'lib'))
exe=root/'build/ir-retention-tests.exe'
try:
 for negative in (False,True):
  # Negative control: skipping the IR dependency comparison must fail unchanged-byte/lookahead test.
  code=body if not negative else body.replace('!tizenRetention_.IRMatches(block_num, instructions.data(), instructions.size() * sizeof(IRInst))','false')
  assert not negative or code!=body
  header.write_text(code)
  subprocess.run([str(zig),'c++','-target','x86_64-windows-gnu','-std=c++17','-O1','-Wall','-Wextra','-Werror','-DTIZENPSP_ARM_JIT',str(root/'checks/ir-retention-tests.cpp'),'-o',str(exe)],env=env,check=True)
  r=subprocess.run([str(exe)],capture_output=True,text=True)
  if negative:
   assert r.returncode!=0 and 'seenRetained==0' in r.stderr,r.stderr
   print('PASS negative control: byte-only retention rejected by changed-lookahead test.')
  else:
   print(r.stdout,end='');assert r.returncode==0,r.stderr
finally:
 header.write_text(body)
