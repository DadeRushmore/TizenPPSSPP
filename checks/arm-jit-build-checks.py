"""Audit the experimental core without weakening the interpreter artifact checks."""
import hashlib,json,re
from pathlib import Path
root=Path(__file__).resolve().parents[1]
r=json.loads((root/'build/ppsspp-arm-jit-elf.json').read_text())
assert hashlib.sha256((root/r['file']).read_bytes()).hexdigest()==r['sha256']
assert not r['arm_hard_float'] and not r['tls_segments'] and not r['wx_segments'] and not r['emutls_imports']
assert not any(s['type']==6 for s in r['tls_and_native_generator_symbols'])
assert any('ArmIRJit' in s['name'] for s in r['tls_and_native_generator_symbols']), 'ARM CPU JIT missing'
assert any('VertexDecoderJitCache' in s['name'] for s in r['tls_and_native_generator_symbols'])
assert set(r['diagnostic_exports'])=={'tizenpsp_jit_init','tizenpsp_jit_snapshot'}
assert set(r['experiment_exports'])=={'tizenpsp_set_vertex_binding','tizenpsp_vertex_binding_status','tizenpsp_set_command_queue','tizenpsp_command_queue_status'}
binding_imports = {s['name']: s for s in r['imports'] if s['name'] in {'glVertexAttribFormat','glVertexAttribBinding','glBindVertexBuffer'}}
assert len(binding_imports)==3 and all(s['binding']==2 for s in binding_imports.values()), 'Separate vertex binding must remain optional'
assert set(r['needed'])<= {'libc.so.6','libm.so.6','libpthread.so.0','libdl.so.2','librt.so.1'}
ninja=(root/'build/ppsspp-arm-jit/build.ninja').read_text()
assert '-DTIZENPSP_ARM_JIT' in ninja and '-DTIZENPSP_NO_VERTEX_JIT' not in ninja and '-DTIZENPSP_NO_JIT' not in ninja
assert '-DTIZENPSP_DETAILED_PROFILE=2' in ninja
assert 'MIPS/ARM/ArmIRJit.cpp.o' in ninja
assert '-DUSE_FFMPEG' in ninja and 'ffmpeg/libavcodec/h264.c.o' in ninja
assert 'ffmpeg/libavcodec/h264_parser.c.o' in ninja and 'ffmpeg/libavformat/mpeg.c.o' in ninja
assert {s['name'] for s in r['codec_symbols']}=={'avcodec_find_decoder','avcodec_open2','av_parser_init','ff_h264_decoder','ff_mpegps_demuxer'}
assert b'ffmpeg=1' in (root/r['file']).read_bytes()
assert not re.search(r'MIPS/(ARM64|x86|RiscV|LoongArch64)/[^\s]+\.cpp\.o',ninja)
assert 'Common/VertexDecoderArm.cpp.o' in ninja
assert not re.search(r'Common/VertexDecoder(Arm64|X86|RiscV|LoongArch64)\.cpp\.o',ninja)
memory=(root/'vendor/ppsspp/Common/MemoryUtil.cpp').read_text()
assert '#if defined(TIZENPSP_ARM_JIT) || PPSSPP_PLATFORM(IOS)' in memory
assert 'TizenJitFatal("mprotect_failed"' in memory and 'TizenJitFatal("rwx_rejected"' in memory
codeblock=(root/'vendor/ppsspp/Common/CodeBlock.h').read_text()
assert 'Future pages must remain RW' in codeblock and 'TizenJitWriteEstimate(sz)' in codeblock
arm=(root/'vendor/ppsspp/Core/MIPS/ARM/ArmIRJit.cpp').read_text()
assert 'jo.enableBlocklink = !jo.Disabled(JitDisable::BLOCKLINK);' in arm
assert 'jo.useBackJump = false;' in arm
assert '#ifndef TIZENPSP_ARM_JIT\n\tif (jo.enableBlocklink)' in arm
assert 'TizenJitExit(true)' in arm and 'TizenPerfScope compileTime' in arm
assert b'write_estimate_overruns=' in (root/r['file']).read_bytes()
assert hashlib.sha256((root/'app/res/ppsspp_libretro-arm32.so').read_bytes()).hexdigest()=='43af35a686c48473549aa6d76f49831c41b317be9d6e5cb835577c20da018bd8'
print('PASS: experimental ARM CPU generator present, ARM vertex generator present, W^X policy, diagnostics exports, no native TLS/RWX segments, interpreter artifact preserved.')
