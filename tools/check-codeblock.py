"""Test PPSSPP's actual code-write policy with OS page protections, without executing ARM."""
import argparse, os, subprocess
from pathlib import Path
root = Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser(); p.add_argument('--zig', required=True); p.add_argument('--legacy-only', action='store_true'); args = p.parse_args()
zig = Path(args.zig).resolve()
env = dict(os.environ, ZIG_GLOBAL_CACHE_DIR=str(root/'.cache/zig-global'), ZIG_LOCAL_CACHE_DIR=str(root/'.cache/zig-local'), ZIG_LIB_DIR=str(zig.parent/'lib'))
for tizen in ([False] if args.legacy_only else [False, True]):
    target = root/'build'/('codeblock-tizen-tests.exe' if tizen else 'codeblock-legacy-tests.exe')
    command = [str(zig), 'c++', '-target', 'x86_64-windows-gnu', '-std=c++17', '-O1', '-Wall', '-Wextra', '-Werror', '-I'+str(root/'checks/codeblock-stubs')]
    if tizen: command += ['-DTIZENPSP_ARM_JIT']
    subprocess.run(command + [str(root/'checks/codeblock-write-tests.cpp'), '-o', str(target)], env=env, check=True)
    subprocess.run([str(target)] + ([] if tizen else ['--expect-old-failure']), check=True)
