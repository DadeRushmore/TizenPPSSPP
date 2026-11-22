"""Read-only ARM32 ELF audit; no execution and no external dependencies."""
import argparse, collections, hashlib, json, struct
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('elf',type=Path);p.add_argument('--output',type=Path,required=True);args=p.parse_args()
b=args.elf.read_bytes()
def u32(at):return struct.unpack_from('<I',b,at)[0]
assert b[:7]==b'\x7fELF\x01\x01\x01', 'ELF32 little endian required'
assert struct.unpack_from('<H',b,18)[0]==40, 'ARM required'
ph=u32(28);count=struct.unpack_from('<H',b,44)[0]
segments=[struct.unpack_from('<8I',b,ph+i*32) for i in range(count)]
loads=[s for s in segments if s[0]==1]
def off(va):
    for s in loads:
        if s[2]<=va<s[2]+s[4]:return s[1]+va-s[2]
    raise ValueError('Address not file backed: '+hex(va))
dyn=next(s for s in segments if s[0]==2);tags=collections.defaultdict(list)
for at in range(dyn[1],dyn[1]+dyn[4],8):
    tag,value=struct.unpack_from('<II',b,at)
    if not tag:break
    tags[tag].append(value)
def tag(n,default=0):return tags[n][0] if n in tags else default
strings=off(tag(5))
def string(index):
    at=strings+index;return b[at:b.index(0,at)].decode('utf-8','replace')
versions={}
if 0x6ffffffe in tags:
    at=off(tag(0x6ffffffe))
    for _ in range(tag(0x6fffffff)):
        version,auxcount,library,aux,next_entry=struct.unpack_from('<HHIII',b,at)
        cursor=at+aux
        for _ in range(auxcount):
            hash_value,flags,index,name,next_aux=struct.unpack_from('<IHHII',b,cursor)
            versions[index&0x7fff]={'version':string(name),'library':string(library)}
            cursor+=next_aux
        if not next_entry:break
        at+=next_entry
symbol_count=u32(off(tag(4))+4) if 4 in tags else None
imports=[];exports=[]
if symbol_count:
    symbols=off(tag(6))
    for i in range(symbol_count):
        name,value,size,info,other,section=struct.unpack_from('<IIIBBH',b,symbols+16*i)
        if not name:continue
        record={'name':string(name),'type':info&15,'binding':info>>4}
        if 0x6ffffff0 in tags:
            index=struct.unpack_from('<H',b,off(tag(0x6ffffff0))+i*2)[0]&0x7fff
            record.update(versions.get(index,{}))
        (imports if section==0 else exports).append(record)
relocations=collections.Counter()
for address,size in [(tag(17),tag(18)),(tag(23),tag(2))]:
    if address:
        for at in range(off(address),off(address)+size,8):relocations[str(u32(at+4)&255)]+=1
full_symbols=[];codec_symbols=[]
sh=u32(32);shcount=struct.unpack_from('<H',b,48)[0]
sections=[struct.unpack_from('<10I',b,sh+i*40) for i in range(shcount)]
for section in sections:
    if section[1]!=2:continue
    names=sections[section[6]];base=names[4]
    for at in range(section[4],section[4]+section[5],16):
        name,value,size,info,other,index=struct.unpack_from('<IIIBBH',b,at)
        if not name or not index:continue
        start=base+name;text=b[start:b.index(0,start)].decode('utf-8','replace')
        if text in {'avcodec_find_decoder','avcodec_open2','av_parser_init','ff_h264_decoder','ff_mpegps_demuxer'}:
            codec_symbols.append({'name':text,'type':info&15,'size':size})
        if info&15==6 or any(n in text for n in ['ArmIRJit','Arm64IRJit','X64IRJit','RiscVJit','LoongArch64Jit','VertexDecoderJitCache7Compile']):
            full_symbols.append({'name':text,'type':info&15,'size':size})
unsupported_tags={1:'DT_NEEDED',7:'DT_RELA',12:'DT_INIT',13:'DT_FINI',26:'DT_FINI_ARRAY',28:'DT_FINI_ARRAYSZ',32:'DT_PREINIT_ARRAY',0x6ffffff0:'DT_VERSYM',0x6ffffffe:'DT_VERNEED'}
report={'file':str(args.elf),'sha256':hashlib.sha256(b).hexdigest(),'bytes':len(b),
 'arm_hard_float':bool(u32(36)&0x400),'mapped_end':max(s[2]+s[5] for s in loads),
 'tls_segments':[{'file_bytes':s[4],'memory_bytes':s[5]} for s in segments if s[0]==7],
 'wx_segments':[s[2] for s in loads if s[6]&3==3],
 'needed':[string(v) for v in tags.get(1,[])],'initializer_count':tag(27)//4,
 'finalizer_count':tag(28)//4,'dynamic_symbol_count':symbol_count,'relocations':dict(relocations),
 'unsupported_dynamic_features':[name for number,name in unsupported_tags.items() if number in tags],
 'imports':imports,'export_count':len(exports),
 'diagnostic_exports':[s['name'] for s in exports if s['name'].startswith('tizenpsp_jit_')],
 'experiment_exports':[s['name'] for s in exports if s['name'] in {'tizenpsp_set_vertex_binding','tizenpsp_vertex_binding_status','tizenpsp_set_command_queue','tizenpsp_command_queue_status'}],
 'emutls_imports':[s['name'] for s in imports if 'emutls' in s['name']],
 'tls_and_native_generator_symbols':full_symbols,
 'codec_symbols':codec_symbols,
 'native_jit_exports':[s['name'] for s in exports if any(n in s['name'] for n in ['ArmIRJit','Arm64IRJit','X64IRJit','RiscVJit','LoongArch64Jit'])]}
args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps({k:v for k,v in report.items() if k not in ['imports']},indent=2))
