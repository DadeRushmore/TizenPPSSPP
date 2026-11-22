# Third-party notices

PPSSPP GPL-2.0-or-later; frontend GPL-2.0; FFmpeg LGPL-2.1-or-later and vendored dependencies under their retained notices.

This source publication preserves individual copyright and license notices in the vendored trees. Its top-level GPL text does not relicense components with different terms. Core source location: `vendor/ppsspp/`. SDK/toolchain binaries are not distributed. The current distribution scope is in [DISTRIBUTION.md](DISTRIBUTION.md).

The historical notes below may describe earlier private or public binary packages, paths relative to an older source root, or assets omitted here. They are retained for attribution; they do not assert that this source snapshot includes those binaries or has passed a fresh rebuild. Current paths are in BUILD.md, and current exclusions are in PUBLIC-SOURCE.json.

# TizenPSP provenance

TizenPSP is distributed under GNU GPL version 2; see LICENSE.

- v0.16.2 adds independently written C# DualSense face-button decoding. Report
  layouts and masks were cross-checked against SDL's PS5 HID backend and Linux's
  Sony-authored hid-playstation driver. No SDL or kernel code is bundled for this
  path. Protocol/ABI references and limits are recorded in docs/DUALSENSE_V0162.md.

- v0.14.0 adds original generated menu backgrounds and PSP icon artwork; the
  generation prompts/provenance are recorded in `docs/v0140-art-prompts.json`.
  Existing USB game cover images remain user-provided and are not redistributed
  in the installer or source archive. No official GTA marketing artwork or
  release-date announcement is included.

- v0.13.3 enables PPSSPP's bundled FFmpeg 3.0.2 subset in the opt-in ARM JIT
  core for H.264 movie decoding. Its source, PPSSPP modifications and LGPL-2.1-or-later
  notices are in `vendor/ppsspp/ext/ffmpeg`, with build configuration in
  `vendor/ppsspp/ext/ffmpeg-build`. Both directories are included in the full source
  archive. The packaged interpreter remains unchanged and has no movie decoder.

- v0.12.0 adds a separately built PPSSPP ARM CPU JIT experiment from the same
  pinned revision. Conditional patches 0005/0006 enable the upstream ARM IR-native
  backend under W^X, keep vertex JIT excluded and add diagnostics. The existing
  interpreter binary is preserved. Both artifacts, the tracked patches and full
  corresponding source/dependencies are included in the release source archive.
  New diagnostic/probe/log-export code is part of TizenPSP under GPLv2.

- v0.11.0 `app/PulseAudioOutput.cs` adapts PulseAudio public ABI declarations
  and stream buffering from TizenPS v0.3.33 `AudioOutput.cs` (GPLv2), inspected
  read-only. The feeder ownership approach was informed by `AudioSound.cs`.
  The PSP queue, timing instrumentation and lifecycle tests are new. PulseAudio
  is a system dependency on the TV; no PulseAudio library is redistributed.

- `app/DynamicElf.cs` was originally copied from TizenPS v0.3.33,
  `roelofs-psx-native/DynamicElf.cs`. TizenPSP now extends it with guarded constructor/core loading; the original project is untouched.
- `app/NativeGraphics.cs` adapts the mapping, relocation, import resolution,
  cache-flush and segment-protection approach from TizenPS `Native.cs`.
  Adaptation date: 2026-11-08. Graphics imports and a one-call triangle ABI
  replace the emulator entry point. Library handles are explicitly released.
- `app/Program.cs` adapts the ElmSharp/CoreUIApplication startup and Evas image
  presentation technique. The application and package identities are new.
- `native/triangle.c`, build/check tooling and the simple triangle icon are new
  for TizenPSP. The early graphics prototypes did not contain emulator code.
- Milestone 2 (2026-11-09) extends the native probe and managed diagnostics.
  `native/baseline/triangle.c` retains the original v0.1.0 source byte-for-byte.
  Its separately bundled native ELF retains the exact TV-tested SHA-256.
  `app/DynamicElf.cs` and the loader's mapping/relocation/protection algorithms
  remain unchanged. The new diagnostic entry adds an optional EGL query pointer.
- GLView prototype v0.3.0 (2026-11-09) adds a small native clear function and a
  persistent managed module owner using the same loader algorithm. Elementary's
  public scalar entry points manage the widget; no Evas structure layout is used.
  Historical EFL source was inspected as ABI evidence, not compiled or copied
  into this application. No PPSSPP code is included in this prototype.
- TizenPS attributes its application and loader under GPLv2. The source archive
  and exact copied-loader hashes are recorded in `docs/reference.json`.
- Tizen .NET, ElmSharp and Newtonsoft.Json are SDK/package dependencies and
  retain their upstream licenses. The SDK supplies Newtonsoft.Json.dll in the
  TPK (MIT, James Newton-King); its license is included as LICENSE-Newtonsoft.txt.
- Zig 0.13.0 is an external build prerequisite, not redistributed. Its use here
  is freestanding: no Zig runtime or glibc source is bundled into the probe.

Distribute this source tree and build instructions with the test TPK. No private
signing credentials are included. The SDK's development signatures do not imply
Samsung TV distribution approval.

## Libretro API header

Official libretro.h, commit 52781c4a8b5aae19d858f4a2326c8f8308c70f54.

Copyright (C) 2010-2024 The RetroArch team

Permission is hereby granted, free of charge,
to any person obtaining a copy of this software and associated documentation files (the "Software"),
to deal in the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software,
and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Actual PPSSPP core in v0.7.0

- PPSSPP a7d80b680a63ffb72be6e9596da680a51c081a2f is included as an ARM32
  interpreter-only Libretro core. PPSSPP is GPL-2.0-or-later; upstream copyright
  and license notices are retained in the complete corresponding source tree.
- Required PPSSPP dependencies and packaged flash0 assets retain their upstream
  licenses. License/NOTICE/COPYING files are included under res/core-licenses.
- The linked C++ runtime and LLVM emulated TLS implementation use LLVM licensing
  (Apache-2.0 WITH LLVM-exception). The full LLVM license is included.
- Zig compiler runtime components retain their upstream MIT/LLVM notices;
  LICENSE-Zig.txt and LICENSE-LLVM.txt are included. The earlier freestanding
  graphics probe's runtime statement does not apply to the actual C++ core.
- No commercial PSP game or proprietary BIOS is bundled. The package performs
  actual core startup/shutdown only, with native JIT generators disabled.
- The matching source ZIP includes the patched PPSSPP/dependency sources, build
  scripts, patch history and dependency/version manifests. Zig 0.13.0 remains
  an external compiler prerequisite; the project-local tool versions are pinned.

- v0.9.0 input mapping adapts raw SDK event binding and per-input state ideas from
  TizenPS v0.3.33 EventPadReader.cs, KeyInput.cs, PadMapping.cs and VisualMappingView.cs
  (GPLv2). The original ZIP/repository was read only. The TizenPSP implementation
  uses PSP action IDs, a background reader, native tap latching and a new layout.

- v0.10.0 LibraryView/LibraryMenu adapt the native card-composition approach of
  TizenPS v0.3.33 LibraryScenes.cs (GPLv2). All PSP artwork is optional user-provided
  USB sidecar content; no TizenPS PS1 cover images are bundled.
