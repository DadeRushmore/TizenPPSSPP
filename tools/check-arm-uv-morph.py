"""Run the real emitted ARM/VFP UV morph instructions against scalar math.

Requires workspace test dependency: python -m pip install --target build/check-tools unicorn==2.1.4
The host generator compiles the actual PPSSPP ArmEmitter.cpp and extracts the
changed decoder method plus its existing prologue/loop/epilogue from source.
"""
import argparse
import json
import math
import os
import random
import struct
import subprocess
import sys
from pathlib import Path

root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--zig',required=True)
args=parser.parse_args()
sys.path.insert(0,str(root/'build/check-tools'))
import unicorn
from unicorn import arm_const as arm
assert unicorn.__version__=='2.1.4', 'Use the pinned workspace Unicorn 2.1.4'

source=(root/'vendor/ppsspp/GPU/Common/VertexDecoderArm.cpp').read_text()
name='Jit_TcU16PrescaleMorph'
start=source.index('void VertexDecoderJitCache::'+name+'()')
method=source[start:source.index('\n}\n',start)+3]
assert '{&VertexDecoder::Step_TcU16PrescaleMorph, &VertexDecoderJitCache::Jit_TcU16PrescaleMorph}' in source
compile=source[source.index('JittedVertexDecoder VertexDecoderJitCache::Compile('):source.index('static const ARMReg weightRegs')]
assert 'dec.steps_[i] == &VertexDecoder::Step_TcU16PrescaleMorph ||' in compile
assert 'BeginWrite(GetSpaceLeft());' in compile and 'ResetCodePtr(GetOffset(start));' in compile
prologue=compile[compile.index('\tPUSH(8,'):compile.index('\n\t// Add code to convert matrices')]
loop=compile[compile.index('\tJumpTarget loopStart'):compile.index('\tfor (int i = 0; i < dec.numSteps_; i++) {',compile.index('\tJumpTarget loopStart'))]
advance=compile[compile.index('\tADDI2R(srcReg,'):compile.index('\n\tif (dec.col)',compile.index('\tADDI2R(srcReg,'))]
epilogue=compile[compile.index('\tVPOP(D8, 8);'):compile.index('\n\tFlushLitPool();')]
assert 'GE_VTYPE_TC_16BIT && !morphPrescaleStep' in prologue
assert 'morphPrescaleStep = true;' in compile
body=method+'\nvoid VertexDecoderJitCache::Generate() {\nconst Decoder &dec=*dec_;\nbool prescaleStep=true,morphPrescaleStep=true;\n'+prologue+loop+'\tJit_TcU16PrescaleMorph();\n'+advance+epilogue+'\nFlushLitPool();\n}\n'
(root/'build').mkdir(exist_ok=True)
(root/'build/arm-uv-morph-method.h').write_text(body)
zig=Path(args.zig).resolve()
env=dict(os.environ,ZIG_GLOBAL_CACHE_DIR=str(root/'.cache/zig-global'),ZIG_LOCAL_CACHE_DIR=str(root/'.cache/zig-local'),ZIG_LIB_DIR=str(zig.parent/'lib'))
generator=root/'build/arm-uv-morph-emitter.exe'
subprocess.run([str(zig),'c++','-target','x86_64-windows-gnu','-std=c++17','-O1','-g',
                '-I'+str(root/'vendor/ppsspp'),'-I'+str(root/'vendor/ppsspp/Common'),
                str(root/'checks/arm-uv-morph-emitter.cpp'),str(root/'vendor/ppsspp/Common/ArmEmitter.cpp'),
                '-o',str(generator)],env=env,check=True)

CODE=0x100000;WEIGHTS=0x200000;SCALE=0x201000;SOURCE=0x300000;OUTPUT=0x400000;STACK=0x500000;STOP=CODE+0xf000
def f32(x):
    try:return struct.unpack('<f',struct.pack('<f',x))[0]
    except OverflowError:return math.copysign(math.inf,x)
rng=random.Random(0x40112)
cases=vertices=0

def run(code,morphs,onesize,tcoff,uvoff,stride,count,weights,scale,values,fpscr=0):
    global cases,vertices
    uc=unicorn.Uc(unicorn.UC_ARCH_ARM,unicorn.UC_MODE_ARM)
    uc.ctl_set_cpu_model(arm.UC_CPU_ARM_CORTEX_A9)
    for address in (CODE,WEIGHTS,SOURCE,OUTPUT,STACK):uc.mem_map(address,0x10000)
    uc.mem_write(CODE,code)
    uc.reg_write(arm.UC_ARM_REG_C1_C0_2,0xf<<20)
    uc.reg_write(arm.UC_ARM_REG_FPEXC,1<<30)
    uc.reg_write(arm.UC_ARM_REG_FPSCR,fpscr)
    def flush(x):
        if fpscr & (1<<24) and 0<abs(x)<2.0**-126:return math.copysign(0.0,x)
        return x
    def mul(a,b):return flush(f32(flush(a)*flush(b)))
    def add(a,b):return flush(f32(flush(a)+flush(b)))
    # Unaligned-to-16 (but u16-aligned) source and padded destination expose
    # accidental wide loads/stores and assumptions about attribute offsets.
    src=SOURCE+0x102;dst=OUTPUT+0x104
    raw=bytearray([0xa5])*(count*morphs*onesize)
    expected=bytearray([0xcd])*(count*stride+32)
    allowed_reads=set()
    for vertex in range(count):
        uv=[0.0,0.0]
        for morph in range(morphs):
            pair=values[vertex][morph]
            offset=(vertex*morphs+morph)*onesize+tcoff
            struct.pack_into('<HH',raw,offset,*pair)
            allowed_reads.update(range(src+offset,src+offset+4))
            for axis in range(2):uv[axis]=add(uv[axis],mul(pair[axis],weights[morph]))
        for axis in range(2):
            result=add(mul(mul(uv[axis],scale[axis]),1.0/32768.0),scale[axis+2])
            struct.pack_into('<f',expected,vertex*stride+uvoff+4*axis,result)
    uc.mem_write(src,bytes(raw));uc.mem_write(dst,bytes([0xcd])*len(expected))
    uc.mem_write(WEIGHTS,struct.pack('<8f',*(weights+[0.0]*(8-morphs))))
    uc.mem_write(SCALE,struct.pack('<4f',*scale))
    gprs={getattr(arm,'UC_ARM_REG_R'+str(i)):0x77100000+i for i in range(4,12)}
    doubles={getattr(arm,'UC_ARM_REG_D'+str(i)):0x3ff0000011110000+i for i in range(32)}
    for reg,value in {**gprs,**doubles}.items():uc.reg_write(reg,value)
    for reg,value in ((arm.UC_ARM_REG_R0,src),(arm.UC_ARM_REG_R1,dst),(arm.UC_ARM_REG_R2,count),(arm.UC_ARM_REG_R3,SCALE),(arm.UC_ARM_REG_SP,STACK+0xf000),(arm.UC_ARM_REG_LR,STOP)):
        uc.reg_write(reg,value)
    writes=[]
    def memory(emu,access,address,size,value,user):
        if SOURCE<=address<SOURCE+0x10000:
            assert access==unicorn.UC_MEM_READ and all(x in allowed_reads for x in range(address,address+size)), ('vertex over-read/write',address,size)
        if OUTPUT<=address<OUTPUT+0x10000:
            assert access==unicorn.UC_MEM_WRITE, ('unexpected output read',address,size)
            writes.extend(range(address,address+size))
    uc.hook_add(unicorn.UC_HOOK_MEM_READ|unicorn.UC_HOOK_MEM_WRITE,memory)
    uc.emu_start(CODE,STOP,count=100000)
    assert uc.reg_read(arm.UC_ARM_REG_PC)==STOP, 'Decoder did not return'
    result=bytes(uc.mem_read(dst,len(expected)))
    # NaN payload/sign propagation is implementation-specific; all other values,
    # including infinities and signed zeros, must match the scalar bits exactly.
    comparable=bytearray(result)
    for vertex in range(count):
        for axis in range(2):
            offset=vertex*stride+uvoff+axis*4
            reference=struct.unpack_from('<f',expected,offset)[0]
            if math.isnan(reference):
                assert math.isnan(struct.unpack_from('<f',result,offset)[0]), 'NaN classification differs'
                comparable[offset:offset+4]=expected[offset:offset+4]
    assert comparable==expected, ('scalar mismatch',morphs,onesize,tcoff,uvoff,stride,count,fpscr,result.hex(),expected.hex())
    assert bytes(uc.mem_read(src,len(raw)))==raw, 'Input vertices changed'
    expected_writes=[dst+v*stride+uvoff+i for v in range(count) for i in range(8)]
    assert sorted(writes)==expected_writes, 'UV decoder wrote outside its output fields'
    assert all(uc.reg_read(reg)==value for reg,value in gprs.items()), 'Callee-saved GPR changed'
    assert all(uc.reg_read(getattr(arm,'UC_ARM_REG_D'+str(i)))==doubles[getattr(arm,'UC_ARM_REG_D'+str(i))] for i in range(8,32)), 'Preserved/unneeded NEON register changed'
    assert uc.reg_read(arm.UC_ARM_REG_SP)==STACK+0xf000, 'Stack not restored'
    assert uc.reg_read(arm.UC_ARM_REG_FPSCR)&0x07c00000==fpscr&0x07c00000, 'FP control flags changed'
    assert uc.reg_read(arm.UC_ARM_REG_R0)==src+len(raw) and uc.reg_read(arm.UC_ARM_REG_R1)==dst+count*stride and uc.reg_read(arm.UC_ARM_REG_R2)==0, 'Vertex loop stride/count wrong'
    cases+=1;vertices+=count

for morphs in range(2,9):
    for onesize,tcoff,uvoff,stride in ((12,0,0,24),(20,2,8,36),(64,28,20,64)):
        binary=root/f'build/arm-uv-morph-{morphs}-{onesize}.bin'
        subprocess.run([str(generator),str(binary),str(morphs),str(onesize),str(tcoff),str(uvoff),str(stride)],check=True)
        code=binary.read_bytes()
        for case in range(16):
            count=(1,2,9,31)[case%4]
            weights=[f32(rng.uniform(-0.5,1.5)) for _ in range(morphs)]
            if case==0:weights=[1.0]+[0.0]*(morphs-1)
            if case==1:weights=[0.0]*morphs
            scale=[f32(rng.uniform(-8,8)),f32(rng.uniform(-8,8)),f32(rng.uniform(-3,3)),f32(rng.uniform(-3,3))]
            values=[[(rng.randrange(65536),rng.randrange(65536)) for _ in range(morphs)] for _ in range(count)]
            values[0][0]=(0,65535)
            if morphs>1:values[0][1]=(65535,0)
            run(code,morphs,onesize,tcoff,uvoff,stride,count,weights,scale,values)

    # Formerly scalar-only inputs must retain scalar VFP's order and denormal
    # behavior. In particular, normalize AFTER raw scaling, and do not silently
    # inherit NEON's mandatory flush-to-zero behavior for tiny morph weights.
    code=(root/f'build/arm-uv-morph-{morphs}-12.bin').read_bytes()
    tiny=f32(2.0**-149)
    edge_cases=[
        ([1.0]+[0.0]*(morphs-1),[f32(1e35),f32(-1e35),0.0,0.0]),
        ([1.0]+[0.0]*(morphs-1),[tiny*4,-tiny*4,0.0,-0.0]),
        ([tiny]+[0.0]*(morphs-1),[1.0,1.0,0.0,0.0]),
        ([tiny*64]+[0.0]*(morphs-1),[f32(1e30),f32(-1e30),0.0,0.0]),
        ([1.0]+[0.0]*(morphs-1),[f32(2.0**-126),f32(2.0**-126),tiny,-tiny]),
        ([1.0]+[0.0]*(morphs-1),[math.inf,-math.inf,0.0,0.0]),
        ([math.inf]+[0.0]*(morphs-1),[1.0,0.0,0.0,0.0]),
        ([math.nan]+[0.0]*(morphs-1),[1.0,1.0,0.0,0.0]),
        ([1.0]+[0.0]*(morphs-1),[math.nan,1.0,math.inf,math.nan]),
        ([-0.0]+[0.0]*(morphs-1),[-1.0,1.0,-0.0,0.0]),
    ]
    values=[[(65535,1)]*morphs,[(0,65535)]*morphs]
    for weights,scale in edge_cases:
        for fpscr in (0,1<<24):run(code,morphs,12,0,0,24,2,weights,scale,values,fpscr)
report={'checks':'Actual PPSSPP ARM emitter and VFP UV method executed under Unicorn; scalar bit equality (NaN classification), morph 2..8, UV extremes, weights, scale overflow/underflow, subnormals with FPSCR FZ off/on, infinities, NaNs, offsets, strides, no source overreads/output overwrites, input preservation, ARM callee-saved registers, FP controls and stack',
        'unicorn_version':unicorn.__version__,'cases':cases,'vertices':vertices,'limits':'CPU instruction emulation only; not TV GPU, whole-game performance, or full vertex-decoder integration execution.'}
(root/'build/arm-uv-morph-validation.json').write_text(json.dumps(report,indent=2)+'\n')
print('PASS: ARM/VFP UV morph instructions:',cases,'cases,',vertices,'vertices; scalar arithmetic order, FP edge cases and memory/register boundaries match.')
