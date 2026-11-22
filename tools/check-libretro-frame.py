"""Exercise the actual libretro EmuFrame ordering against a render-pass contract.

This is a host regression test, not a Mali/Evas runtime test. The first mutation
removes the new early pass; the second restores the destructive final clear.
Both must reproduce the failure the production method now avoids.
"""
import argparse
import os
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--zig', required=True)
args = parser.parse_args()
source = (root / 'vendor/ppsspp/libretro/libretro.cpp').read_text()
start = source.index('   static void EmuFrame() {')
end = source.index('\n   static void EmuThreadFunc()', start)
body = source[start:end]
early = 'if (draw && skipBufferEffects) {'
late = 'if (draw && !skipBufferEffects) {'
assert body.count(early) == body.count(late) == 1
missing_pass = body.replace('EmuFrame()', 'EmuFrameMissingPass()', 1).replace(early, 'if (false) {', 1)
late_clear = body.replace('EmuFrame()', 'EmuFrameLateClear()', 1).replace(late, 'if (draw) {', 1)
(root / 'build').mkdir(exist_ok=True)
(root / 'build/libretro-emuframe-methods.h').write_text(body + missing_pass + late_clear)
zig = Path(args.zig).resolve()
env = dict(os.environ, ZIG_GLOBAL_CACHE_DIR=str(root / '.cache/zig-global'),
           ZIG_LOCAL_CACHE_DIR=str(root / '.cache/zig-local'), ZIG_LIB_DIR=str(zig.parent / 'lib'))
for tizen in (False, True):
    target = root / 'build' / ('libretro-frame-tizen-tests.exe' if tizen else 'libretro-frame-tests.exe')
    command = [str(zig), 'c++', '-target', 'x86_64-windows-gnu', '-std=c++17', '-O1',
               '-Wall', '-Wextra', '-Werror']
    if tizen:
        command += ['-DTIZENPSP_ARM_JIT']
    subprocess.run(command + [str(root / 'checks/libretro-frame-tests.cpp'), '-o', str(target)], env=env, check=True)
    subprocess.run([str(target)], check=True)
