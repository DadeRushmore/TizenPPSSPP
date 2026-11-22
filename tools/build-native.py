"""Build only the small graphics probe; no downloads and no emulator sources."""
import argparse, hashlib, json, os, subprocess
from pathlib import Path

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--zig', default=os.environ.get('TIZENPSP_ZIG'))
parser.add_argument('--verify-baseline',action='store_true',help='Rebuild the preserved source separately and check its TV-tested hash')
args = parser.parse_args()
if not args.zig:
    parser.error('pass --zig PATH or set TIZENPSP_ZIG to Zig 0.13.0')
zig = str(Path(args.zig).resolve())
if subprocess.check_output([zig, 'version'], text=True).strip() != '0.13.0':
    raise SystemExit('This build is pinned to Zig 0.13.0')
env = dict(os.environ, ZIG_GLOBAL_CACHE_DIR=str(root/'.cache/zig-global'),
           ZIG_LOCAL_CACHE_DIR=str(root/'.cache/zig-local'),
           ZIG_LIB_DIR=str(Path(zig).parent/'lib'))
base = [zig,'cc','-target','arm-linux-gnueabi.2.17','-mcpu=cortex_a9','-marm',
       '-O2','-fPIC','-g0','-fno-builtin','-fno-stack-protector',
       '-fno-unwind-tables','-fno-asynchronous-unwind-tables','-nostdlib','-shared',
       '-Wall','-Wextra','-Werror','-Wl,--hash-style=sysv','-Wl,-z,max-page-size=4096']
(root/'build').mkdir(exist_ok=True)
cpp_source=root/'native/core-probes/cpp-startup.cpp'
cpp_object=root/'build/cpp-startup.o'
cpp_command=[zig,'c++','-target','arm-linux-gnueabi.2.17','-mcpu=cortex_a9','-marm',
             '-O2','-fPIC','-fno-exceptions','-fno-rtti','-fno-stack-protector',
             '-fno-unwind-tables','-fno-asynchronous-unwind-tables',
             '-Wall','-Wextra','-Werror','-c',str(cpp_source),'-o',str(cpp_object)]
cpp_key=hashlib.sha256(cpp_source.read_bytes()+json.dumps(cpp_command).encode()).hexdigest()
cpp_record=root/'build/cpp-startup.key'
if not cpp_object.exists() or not cpp_record.exists() or cpp_record.read_text()!=cpp_key:
    subprocess.run(cpp_command,env=env,check=True,cwd=root)
    cpp_record.write_text(cpp_key)
baseline=json.loads((root/'docs/milestone1-baseline.json').read_text())
baseline_source=root/'native/baseline/triangle.c'
if hashlib.sha256(baseline_source.read_bytes()).hexdigest()!=baseline['source_sha256']:
    raise SystemExit('Preserved v0.1.0 native source changed; refusing to replace baseline')
if args.verify_baseline:
    verification=root/'build/baseline-reproduction.so'
    subprocess.run(base+['-Wl,-soname,libtizenpsp_triangle.so','-o',str(verification),str(baseline_source)],env=env,check=True,cwd=root)
    if hashlib.sha256(verification.read_bytes()).hexdigest()!=baseline['native_sha256']:
        raise SystemExit('Baseline source does not reproduce the TV-tested ELF')
    print('PASS: preserved source reproduces the exact TV-tested v0.1.0 native ELF')
for source,name,hash_name,pinned in [
    (baseline_source,'libtizenpsp_triangle_v010.so','triangle-v010.sha256',baseline['native_sha256']),
    (root/'native/triangle.c','libtizenpsp_triangle.so','triangle.sha256',None),
    (root/'native/glview-hw-test.c','libtizenpsp_glview.so','glview.sha256',None),
    (root/'native/libretro-bridge/bridge.c','libtizenpsp_corebridge.so','corebridge.sha256',None)]:
    output=root/'app/res'/name
    output.parent.mkdir(parents=True,exist_ok=True)
    soname=name if name in ['libtizenpsp_glview.so','libtizenpsp_corebridge.so'] else 'libtizenpsp_triangle.so'
    cmd=base+['-Wl,-soname,'+soname,'-o',str(output),str(source)]
    if source.name=='glview-hw-test.c':
        cmd+=['-DTIZENPSP_CPP_STARTUP_TEST',str(cpp_object)]
    dependencies=(root/'native/glview-clear.c').read_bytes()+(root/'native/libretro-bridge/include/libretro.h').read_bytes() if source.name=='glview-hw-test.c' else b''
    if source.name=='glview-hw-test.c': dependencies+=cpp_object.read_bytes()
    if source.name=='bridge.c': dependencies=b''.join((source.parent/n).read_bytes() for n in ['bridge.h','platform.h','include/libretro.h'])
    key=hashlib.sha256(source.read_bytes()+dependencies+json.dumps(cmd).encode()).hexdigest()
    record=root/'build'/(name+'.json')
    previous=json.loads(record.read_text()) if record.exists() else {}
    existing=hashlib.sha256(output.read_bytes()).hexdigest() if output.exists() else None
    cached=(pinned and existing==pinned) or (previous.get('input_key')==key and previous.get('sha256')==existing and existing)
    if not cached: subprocess.run(cmd, env=env, check=True, cwd=root)
    digest=hashlib.sha256(output.read_bytes()).hexdigest()
    if pinned and digest!=pinned:
        raise SystemExit('Rebuilt baseline differs from TV-tested ELF; refusing to label it original')
    (output.parent/hash_name).write_text(digest+'\n',encoding='ascii')
    record.write_text(json.dumps({'command':cmd,'input_key':key,'sha256':digest},indent=2)+'\n')
    print(('Reused' if cached else 'Built'),name,output.stat().st_size,'bytes; SHA256',digest)
