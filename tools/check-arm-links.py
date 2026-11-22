"""Exercise the actual ARM exit/invalidation methods with a recording emitter.
This tests code-generation decisions on Windows, not ARM instruction execution.
"""
import argparse
import os
import subprocess
from pathlib import Path
root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--zig', required=True)
args = parser.parse_args()
source = (root/'vendor/ppsspp/Core/MIPS/ARM/ArmIRJit.cpp').read_text()
def method(name):
    start = source.index('void ArmJitBackend::' + name + '(')
    end = source.index('\n}\n', start) + 3
    return source[start:end]
body = method('WriteConstExit') + method('InvalidateBlock') + method('OverwriteExit') + method('ClearAllBlocks')
common = (root/'vendor/ppsspp/Core/MIPS/IR/IRNativeCommon.cpp').read_text()
for signature in ('const IRNativeBlock *IRNativeBackend::GetNativeBlock',
                  'void IRNativeBackend::SetBlockCheckedOffset',
                  'void IRNativeBackend::SetBlockEntry',
                  'int IRNativeBackend::FindBlockByStart',
                  'void IRNativeBackend::EraseAllLinks'):
    start = common.index(signature + '(')
    end = common.index('\n}\n', start) + 3
    body += common[start:end].replace('IRNativeBackend::', 'ArmJitBackend::')
(root/'build/arm-link-methods.h').write_text(body)
# The checked entry must precede normal entry, and invalidation must overwrite
# normal entry. Incoming links then reach the invalidation trampoline too.
compile = source[source.index('bool ArmJitBackend::CompileBlock'):source.index('void ArmJitBackend::WriteConstExit')]
assert compile.index('SetBlockCheckedOffset') < compile.index('CMP(DOWNCOUNTREG') < compile.index('SetBlockEntry')
assert 'B_CC(CC_PL)' in compile and 'B(outerLoopPCInSCRATCH1_)' in compile
assert 'jo.useBackJump = false;' in source
capacity = 32 * 1024 * 1024 - 64 * 1024
header = (root/'vendor/ppsspp/Core/MIPS/IR/IRNativeCommon.h').read_text()
start = header.index('struct IRNativeBlock {')
(root/'build/arm-link-block.h').write_text(header[start:header.index('\n};', start)+3])
branches = (root/'vendor/ppsspp/Core/MIPS/ARM/ArmIRCompBranch.cpp').read_text()
assert branches.count('WriteConstExit(inst.constant);') == 3
assert 'B(dispatcherPCInSCRATCH1_);' in branches and 'B(dispatcherCheckCoreState_);' in branches
assert 'if (!nativeBlock->tizenHasIncomingBranch)' in source
assert source.count('nativeBlocks_[block_num].tizenHasIncomingBranch = true;') == 2
assert 'tizenHasIncomingBranch = false' not in common
irjit = (root/'vendor/ppsspp/Core/MIPS/IR/IRJit.cpp').read_text()
invalidate = irjit[irjit.index('void IRJit::InvalidateBlock('):irjit.index('void IRJit::InvalidateCacheExcept(')]
assert invalidate.index('InvalidateTargetBlock') < invalidate.index('block->Destroy')
assert 'space.ClearBlockShadow(origAddr_, entry_);' in irjit
assert 'constexpr int NATIVE_CODE_SPACE_SIZE = 32 * 1024 * 1024 - 64 * 1024;' in header
# Both directions must fit ARM B's signed imm24 * 4, including PC + 8.
assert -(1 << 25) <= -capacity - 8 and capacity - 8 <= (1 << 25) - 4
zig = Path(args.zig).resolve()
env = dict(os.environ, ZIG_GLOBAL_CACHE_DIR=str(root/'.cache/zig-global'),
           ZIG_LOCAL_CACHE_DIR=str(root/'.cache/zig-local'), ZIG_LIB_DIR=str(zig.parent/'lib'))
target = root/'build/arm-link-tests.exe'
subprocess.run([str(zig), 'c++', '-target', 'x86_64-windows-gnu', '-std=c++17', '-O1',
                '-Wall', '-Wextra', '-Werror', '-Wno-unused-variable',
                str(root/'checks/arm-link-tests.cpp'), '-o', str(target)], env=env, check=True)
subprocess.run([str(target)], check=True)
