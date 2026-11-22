"""Generate the TV readiness input from a freshly audited, matching core ELF."""
import argparse,hashlib,json
from pathlib import Path
root=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--arm-jit',action='store_true');args=p.parse_args()
r=json.loads((root/('build/ppsspp-arm-jit-elf.json' if args.arm_jit else 'build/ppsspp-core-elf.json')).read_text())
assert hashlib.sha256((root/r['file']).read_bytes()).hexdigest()==r['sha256']
lines=['# PPSSPP ARM32 runtime imports; core is not executed','# core_sha256='+r['sha256']]
for s in r['imports']:
    lines.append('\t'.join([s['name'],s.get('version',''),s.get('library',''),'1' if s['binding']==2 else '0']))
data=('\n'.join(lines)+'\n').encode()
name='core-jit-runtime-imports' if args.arm_jit else 'core-runtime-imports'
(root/('app/res/'+name+'.tsv')).write_bytes(data)
(root/('app/res/'+name+'.sha256')).write_text(hashlib.sha256(data).hexdigest()+'\n')
print('Generated exact core import manifest:',sum(s['binding']!=2 for s in r['imports']),'required symbols.')
