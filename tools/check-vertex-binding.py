"""Compare actual before/after DRAW bodies against a GL vertex-state model."""
import argparse,os,subprocess,json,hashlib,tempfile
from pathlib import Path
root=Path(__file__).resolve().parents[1]
(root/'build').mkdir(exist_ok=True)
p=argparse.ArgumentParser();p.add_argument('--zig',required=True);a=p.parse_args()
new=(root/'vendor/ppsspp/Common/GPU/OpenGL/GLQueueRunner.cpp').read_text(encoding='utf-8')
# Reconstruct the released baseline in a private scratch directory so a full
# source release can run these checks without also downloading the old ZIP.
# Hash verified against GLQueueRunner.cpp in the released 0.23.9 source ZIP.
baseline_hash='7c3638b0e72c16cba15666ad34c683cffdf09c6e00d48d75f80182c68957e76c'
with tempfile.TemporaryDirectory(prefix='vertex-baseline-',dir=root/'build') as temporary:
    scratch=Path(temporary).resolve();assert scratch.is_relative_to((root/'build').resolve())
    target=scratch/'Common/GPU/OpenGL/GLQueueRunner.cpp';target.parent.mkdir(parents=True)
    target.write_bytes(new.encode('utf-8'))
    for patch_name in ('0026-gles-core-vertex-array-lifecycle.patch','0025-live-vertex-binding-experiment.patch','0024-sampled-execution-presentation-costs.patch','0023-separate-vertex-binding.patch'):
        patch=(root/'native/ppsspp-patches'/patch_name).read_text(encoding='utf-8')
        start=patch.index('--- a/Common/GPU/OpenGL/GLQueueRunner.cpp\n')
        end=patch.find('\n--- a/',start+1)
        gl_patch=patch[start:end+1] if end>=0 else patch[start:]
        subprocess.run(['git','-c','core.autocrlf=false','apply','--reverse','--ignore-space-change','--directory='+scratch.relative_to(root).as_posix(),'-'],input=gl_patch.encode('utf-8'),cwd=root,check=True)
    old=target.read_text(encoding='utf-8')
    assert hashlib.sha256(old.encode('utf-8')).hexdigest()==baseline_hash,'Released baseline changed'
probe_start=new.index('\ttizenSeparateVertexBinding=false;')
probe_end=new.index('\tINFO_LOG(Log::G3D,"TIZEN_VERTEX_BINDING',probe_start)
create_start=new.index('\t// GLES 3.1 has core VAOs')
create_end=new.index('\n#ifdef TIZENPSP_ARM_JIT\n\ttizenSeparateVertexBinding',create_start)
methods=['void CreateBindingVAO(GLuint &globalVAO_) {\n'+new[create_start:create_end]+'}\n']
for name,needle in [('BindBindingVAO','if (globalVAO_) {\n\t\t\tglBindVertexArray'),('DeleteBindingVAO','if (globalVAO_) {\n\t\tglDeleteVertexArrays'),('UnbindBindingVAO','if (last && globalVAO_) {')]:
    start=new.index(needle);end=new.index('}',start)+1
    methods.append('void '+name+'(GLuint &globalVAO_,bool last=true) {\n(void)last;\n'+new[start:end]+'\n}\n')
methods+=['void ProbeBinding(GLuint globalVAO_) {\n'+new[probe_start:probe_end]+'}\n']
for name,source in [('BaselineDraw',old),('OptimizedDraw',new)]:
    start=source.index('\t\tcase GLRRenderCommand::DRAW:')
    block=source[start:source.index('\t\tcase GLRRenderCommand::GENMIPS:',start)]
    block=block[block.index('{')+1:block.rindex('}')]
    assert block.count('\t\t\tbreak;')==1
    block=block.replace('\t\t\tbreak;','')
    methods.append('void '+name+'(const GLRRenderData &c,Pass &pass) {\n auto &attrMask=pass.attrMask;auto &curArrayBuffer=pass.curArrayBuffer;auto &curElemArrayBuffer=pass.curElemArrayBuffer;\n'+(' auto &vertexBindings=pass.vertexBindings;const bool separateVertexBinding=tizenSeparateVertexBinding && (tizenpsp_vertex_binding_status()&1)!=0;\n' if name=='OptimizedDraw' else '')+block+'\n}\n')
(root/'build/vertex-binding-draw-methods.h').write_text('\n'.join(methods))
zig=Path(a.zig).resolve();env=dict(os.environ,ZIG_GLOBAL_CACHE_DIR=str(root/'.cache/zig-global'),ZIG_LOCAL_CACHE_DIR=str(root/'.cache/zig-local'),ZIG_LIB_DIR=str(zig.parent/'lib'))
results={}
for mode in (0,2):
    exe=root/f'build/vertex-binding-tests-{mode}.exe'
    sources=[str(root/'checks/vertex-binding-tests.cpp')]
    if mode:
        # Compile diagnostics without the ARM-only movie decoder include.
        # The DRAW fixture itself needs the production ARM feature branches.
        obj=root/'build/vertex-binding-diagnostics.o'
        subprocess.run([str(zig),'c++','-target','x86_64-windows-gnu','-std=c++17','-O2',f'-DTIZENPSP_DETAILED_PROFILE={mode}','-c',str(root/'native/core-build/runtime/jit-diagnostics.cpp'),'-o',str(obj)],env=env,check=True)
        sources.append(str(obj))
    subprocess.run([str(zig),'c++','-target','x86_64-windows-gnu','-std=c++17','-O2','-Wall','-Wextra','-Werror','-DTIZENPSP_ARM_JIT',f'-DTIZENPSP_DETAILED_PROFILE={mode}',*sources,'-o',str(exe)],env=env,check=True)
    output=subprocess.check_output([str(exe)],text=True);print(f'Profile mode {mode}:\n'+output)
    results[str(mode)]=output.splitlines()
(root/'docs/vertex-binding-validation-v0242.json').write_text(json.dumps({'baseline_source_sha256_lf':baseline_hash,'profile_modes':results,'scope':'Actual production DRAW bodies with modeled GL state; not physical GPU execution or FPS.'},indent=2)+'\n')
