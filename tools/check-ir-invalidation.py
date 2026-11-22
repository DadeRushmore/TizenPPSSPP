"""Run production page lookup/removal methods against a brute-force overlap oracle."""
import argparse,os,re,subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--zig',required=True);a=p.parse_args()
s=(root/'vendor/ppsspp/Core/MIPS/IR/IRJit.cpp').read_text()
def method(signature):
 start=s.index(signature+'(');end=s.index('\n}\n',start)+3
 return s[start:end]
body='\n'.join(method(x) for x in ('std::vector<int> JitBlockTable::FindInvalidatedBlockNumbers','void JitBlockTable::FinalizeBlock','void JitBlockTable::RemoveBlockFromPageLookup','u32 JitBlockTable::AddressToPage','bool JitBlock::OverlapsRange'))
zig=Path(a.zig).resolve();env=dict(os.environ,ZIG_GLOBAL_CACHE_DIR=str(root/'.cache/zig-global'),ZIG_LOCAL_CACHE_DIR=str(root/'.cache/zig-local'),ZIG_LIB_DIR=str(zig.parent/'lib'))
header=root/'build/ir-invalidation-methods.h';exe=root/'build/ir-invalidation-tests.exe'
try:
 for negative in (False,True):
  code=body
  if negative:
   code,n=re.subn(r'page == std::max\(startPage, AddressToPage\(blocks_\[i\].GetOriginalStart\(\)\)\) &&\s*','',code)
   assert n==2
  header.write_text(code)
  subprocess.run([str(zig),'c++','-target','x86_64-windows-gnu','-std=c++17','-O2','-Wall','-Wextra','-Werror',str(root/'checks/ir-invalidation-tests.cpp'),'-o',str(exe)],env=env,check=True)
  calls=subprocess.check_output([str(exe),'--count'],text=True).strip()
  assert int(calls)==(20480 if negative else 4096),calls
  print(('Original' if negative else 'Fixed')+' stress invalidation calls: '+calls)
  result=subprocess.run([str(exe)],capture_output=True,text=True)
  if negative:
   assert result.returncode!=0 and 'duplicate invalidation' in result.stderr,result.stderr
   print('PASS: negative control reproduces duplicate invalidation')
  else:
   print(result.stdout,end='');assert result.returncode==0,result.stderr
finally:
 header.write_text(body)
