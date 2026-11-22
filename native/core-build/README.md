# Actual PPSSPP ARM32 core build

## v0.23.3 vertex morph decoder and transfer counts

Patch 0016 adds the missing ARM32 U16 texture-coordinate morph/prescale decoder.
It uses scalar VFP arithmetic in the reference order, preserving subnormal and
extreme-value behavior. The focused checker runs emitted ARM instructions under
workspace-only Unicorn 2.1.4; see `tools/check-arm-uv-morph.py`. Patch 0017 mirrors
logical framebuffer transfer counts with relaxed atomics and no timing calls.
See `docs/FIXES_V0233.md` and `docs/FRAMEBUFFER_TRANSFER_COUNTERS.md`.

Source preparation now also compares reversed patched originals with the pinned
archive before reapplying, so a patch based on a stray local edit is rejected.

## v0.21.0 cost attribution

The ARM target now includes inclusive GPU/GL cost scopes, per-thread schedstat
samples and whole-process CPU time. This is a profiling release, not a new
performance claim. Frequent commands are sampled; the analysis retains divisors
and reports unknown costs when there are no timed samples. See
`docs/PROFILING_V0210.md` for measurement boundaries and overhead.

## v0.20.0 ARM vertex JIT and effective compute capacity

The ARM JIT target now includes the upstream ARM/NEON vertex decoder, using W^X
transitions that reopen a rewound failed-compilation tail safely. Non-ARM vertex
generators stay excluded. The interpreter artifact remains unchanged. The compute
pool follows effective affinity, and synchronous parallel loops avoid pool dispatch
on one available CPU. Diagnostics count generated/scalar/SIMD vertices and sparsely
sample decode time. See `docs/OPTIMIZATION_V0200.md` for limits and test coverage.

## v0.19.0 frontend scheduling change

The v0.19.0 frontend reuses the exact v0.18.0 native binary. It requests broader
own-thread CPU affinity before native initialization so new core workers inherit
the OS-permitted mask. Native per-thread events verify the actual result. See
`docs/OPTIMIZATION_V0190.md` for the measured single-CPU constraint and limits.

## v0.18.0 cache and wait investigation

The Tizen ARM cache is 32 MiB minus 64 KiB, staying within ARM branch reach.
The frontend offers per-game fast memory for comparison with the default checked
mode. Detailed native timings separate dispatcher work, frame-slot/queue/sync
waits, GL task execution and readbacks. See `docs/OPTIMIZATION_V0180.md` for the
confirmed cache-full stall, counter semantics, validation and TV test steps.

## v0.17.0 performance comparison

The ARM build now emits links to existing compiled destinations without deferred
exit rewriting. Checked memory and W^X remain enabled. New wall/thread CPU counters
cover emulation, render processing and compilation. See
`docs/OPTIMIZATION_V0170.md` for the six-session baseline, tests and TV limitations.

## v0.13.3 movie decoder

`-ArmJit` now enables the pinned bundled FFmpeg subset via `USE_FFMPEG=ON` and
`USE_SYSTEM_FFMPEG=OFF`. No new download or TV shared-library dependency is added.
The core audit checks that H.264 decoder/parser and MPEG-PS demuxer symbols are
actually linked. The runtime summary calls `AvcDecoder::IsAvailable()` and exposes
`ffmpeg=1`. Decoder initialization is checked on Windows by
`python tools/check-codecs.py --zig PATH`; real movie playback requires the TV.
FFmpeg's LGPL notices are shipped in `app/res/core-licenses` and the source archive.

The non-ArmJit build retains `USE_FFMPEG=OFF`, and the packaged interpreter binary
remains the previously pinned fallback. Movie support therefore requires selecting
ARM JIT test for that launch. The historical scope notes below describe the older
interpreter configuration.

## v0.12.0 ARM experiment

The current package contains the unchanged, pinned interpreter core plus a
separate opt-in ARM IR-native JIT core. The historical notes below describe the
original interpreter build. For the experiment, after preparing the cached source:

```powershell
python tools/prepare-core.py
./tools/build-ppsspp.ps1 -ArmJit -ZigPath 'C:/path/to/zig.exe'
python tools/stage-jit-core.py
./tools/build.ps1 -ZigPath 'C:/path/to/zig.exe'
python tools/package-release.py
```

The ARM variant builds into build/ppsspp-arm-jit and has a separate ELF audit,
import manifest and managed checksum pin. Stage only after its policy audit;
the final application build also validates this artifact through the real loader.
Default builds still define TIZENPSP_NO_JIT; the ARM variant defines
TIZENPSP_ARM_JIT and includes its ARM vertex generator. Other host CPU backends remain excluded.
The new conditional patches and diagnostics are included in the full source ZIP.
Source preparation validates overlapping patches by reversing/reapplying them in
an isolated scratch tree and preserves actual source line endings.

The existing app/res interpreter binary is never replaced by this procedure.
Rebuilding the interpreter from updated sources may yield a different checksum
and still requires separate artifact review before changing its pin.
See docs/ARM_JIT_V0120.md for runtime gates, logging and remaining TV tests.

This build is separate from the TV-tested v0.5.0 graphics/startup application.
It does not automatically copy a core into the TPK or claim it can be loaded.

PPSSPP revision: `a7d80b680a63ffb72be6e9596da680a51c081a2f`.
Exact archive/dependency hashes are recorded in `docs/ppsspp-source.json` and
`docs/ppsspp-dependencies.json`. Official Khronos/OpenXR headers are kept in
`headers/` with their original notices and recorded source hashes.

## Reproduce

From the project root, with Python 3.12+, git and the installed Zig 0.13.0:

```powershell
python -m pip install --disable-pip-version-check --no-warn-script-location --target build/core-tools cmake==3.31.6 ninja==1.11.1.3
python tools/prepare-core.py
./tools/build-ppsspp.ps1 -ZigPath 'C:/path/to/zig.exe'
```

Skip the pip command when those project-local tools already exist. Preparation
reuses verified archives and only fetches missing dependencies. Patched source
stays in `vendor/ppsspp`, build products in `build/ppsspp-core`, and Zig caches in
`.cache`. Nothing modifies TizenPS or installs compilers system-wide.

Configuration and compilation output go to `build/core-configure.log` and
`build/core-build.log`; errors print only their tail. The build is incremental,
uses four workers, and targets only `ppsspp_libretro`. Precompiled headers are
disabled because Zig 0.13's driver failed the upstream glslang PCH command.

## Interpreter policy

- ARMv7, little-endian, 32-bit, soft-float calling convention remains unchanged.
- The CPU option is pinned to IR_INTERPRETER after each option update.
- SYSPROP_CAN_JIT returns false, including the vertex-decoder capability path.
- Native architecture CPU JIT translation units are excluded from Core.
- Architecture vertex-decoder generator translation units are excluded from GPU;
  the existing null backend is used, with its call site guarded correctly.
- CreateNativeJit is redirected at compile time to the IR interpreter fallback.
- Executable allocation/protection requests inside the core fail under
  TIZENPSP_NO_JIT. The host's existing executable ELF mapping remains separate.

These are tracked source/build patches, not undocumented upstream CMake flags.
Do not infer final JIT exclusion solely from dynamic exports: the link script
hides internal symbols. Inspect the final full symbol table and build source list.

## Deliberate initial scope

GLES is selected. FFmpeg, VR, UPnP helper, Discord, HTTPS frontend, tests and
standalone application targets are disabled. Upstream common code still requires
some VR/network/platform declarations; those are not proof the features are active.
The first intended content is a small PSP homebrew program, not a commercial game
or the standalone PPSSPP menu. Video/movie decoding is absent with FFmpeg disabled.

Graphics imports are left for the frontend to resolve from the TV's GLES/EGL
libraries. Runtime system-library links are not disguised as freestanding code.
`-femulated-tls` is applied to core/dependency sources. LLVM 18.1.7's emutls
implementation is linked into the module. A small __cxa_thread_atexit adapter
requires the TV's __cxa_thread_atexit_impl and replaces libc++abi's native-TLS
fallback. The final inspected artifact has no PT_TLS, TLS relocation or unresolved
emutls helper. This is compile/link evidence, not a thread-runtime execution test.
The eventual host must retain the core mapping until process exit; arbitrary
unload remains unsupported. Despite -nostartfiles, this Zig-linked artifact still
contains DT_INIT/DT_FINI, which the loader must explicitly validate and handle.

## Required before TV packaging

`tools/inspect-core-elf.py` produces an import/relocation/dependency/TLS audit.
Successful compilation is not successful loading. The currently validated loader
only supports a narrow local initializer path, not arbitrary DT_NEEDED, versioning,
TLS, finalizers or C++ runtime lifetime. Those features must be resolved explicitly
against the final artifact. Do not weaken its guards merely to accept the core.

The frontend still needs actual core mapping/import resolution, Libretro services,
content boot and graphics/audio/input/lifetime integration. This build does not
alter or replace the user's working v0.5.0 installer.

## Current artifact and TV prerequisite

The build succeeded. `build/ppsspp-core/ppsspp_libretro-arm32.so` is the compact
16,901,552-byte artifact (not installable). Its audit is recorded in
`docs/ppsspp-core-build-result.json`. Build-policy checks pass; mapped size is
16,835,061 bytes, above the loader's current 12 MiB cap. There are 117 array
initializers, five system-library dependencies, versioned imports and three
R_ARM_ABS32 relocations that the current loader does not yet handle. No native
TLS or native CPU/vertex generator symbols were found in the full symbol table.

v0.6.0 is a small TV import-readiness package generated from this exact artifact,
not a PPSSPP execution package. It checks 364 required imports and one optional
weak import using dlsym/dlvsym, reports missing symbols/versions and does not map
or execute the core. A PASS is only import availability. Initialization, runtime
lifetimes and actual core loading remain separate checks before homebrew boot.
