"""Stage only a policy-audited ARM experiment; retain the pinned interpreter core."""
import hashlib,json,re,shutil,subprocess,sys
from pathlib import Path
root=Path(__file__).resolve().parents[1]
subprocess.run([sys.executable,str(root/'checks/arm-jit-build-checks.py')],cwd=root,check=True)
r=json.loads((root/'build/ppsspp-arm-jit-elf.json').read_text())
artifact=root/r['file']
assert hashlib.sha256(artifact.read_bytes()).hexdigest()==r['sha256']
shutil.copy2(artifact,root/'app/res/ppsspp_libretro-arm32-jit.so')
(root/'app/res/core-jit.sha256').write_text(r['sha256']+'\n')
subprocess.run([sys.executable,str(root/'tools/make-core-import-manifest.py'),'--arm-jit'],cwd=root,check=True)
path=root/'app/NativeJitExperiment.cs'
with path.open('r',newline='') as f:text=f.read()
text,n=re.subn(r'ExpectedJitHash="(?:PENDING_ARM_JIT_BUILD|[0-9a-f]{64})"','ExpectedJitHash="'+r['sha256']+'"',text)
assert n==1
with path.open('w',newline='') as f:f.write(text)
(root/'docs/ppsspp-arm-jit-build-result.json').write_text(json.dumps(r,indent=2)+'\n')
print('Staged experimental core and pinned SHA256:',r['sha256'])
