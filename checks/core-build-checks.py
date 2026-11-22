"""Build-artifact policy checks; these do not execute ARM code or validate TV loading."""
import hashlib,json,re
from pathlib import Path
root=Path(__file__).resolve().parents[1]
r=json.loads((root/'build/ppsspp-core-elf.json').read_text())
artifact=root/r['file']
assert hashlib.sha256(artifact.read_bytes()).hexdigest()==r['sha256']
assert not r['arm_hard_float'] and not r['tls_segments'] and not r['wx_segments']
assert not r['emutls_imports'] and not r['tls_and_native_generator_symbols']
ninja=(root/'build/ppsspp-core/build.ninja').read_text()
assert '-DTIZENPSP_NO_JIT' in ninja
assert not re.search(r'MIPS/(ARM|ARM64|x86|RiscV|LoongArch64)/[^\s]+\.cpp\.o',ninja)
assert not re.search(r'Common/VertexDecoder(Arm|Arm64|X86|RiscV|LoongArch64)\.cpp\.o',ninja)
policy=(root/'vendor/ppsspp/libretro/libretro.cpp').read_text()
assert 'g_Config.iCpuCore = (int)CPUCore::IR_INTERPRETER;' in policy
assert re.search(r'#ifndef TIZENPSP_ARM_JIT\s+// TizenPSP: pin CPU mode after every core-option update\.\s+g_Config.iCpuCore = \(int\)CPUCore::IR_INTERPRETER;\s+#endif',policy)
capability=policy.split('case SYSPROP_CAN_JIT:',1)[1].split('case SYSPROP_ENOUGH_RAM_FOR_FULL_ISO:',1)[0]
assert '#ifdef TIZENPSP_ARM_JIT' in capability and '#else\n      // TizenPSP interpreter-only milestone: includes vertex-decoder JIT.\n      return false;\n#endif' in capability
memory=(root/'vendor/ppsspp/Common/MemoryUtil.cpp').read_text()
assert 'if (memProtFlags & MEM_PROT_EXEC) return false;' in memory
assert 'return nullptr; // Host ELF loading is separate;' in memory
print('PASS: ARM32 soft-float, no native TLS/W+X, bundled emutls, native-generator source/symbol exclusion, interpreter policy and executable-allocation guards.')
print('Artifact policy check only; real-loader checks and device validation are separate.')
