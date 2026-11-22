# GLView / Libretro hardware bridge

v0.11.0 forwards audio to a rooted, nonblocking managed PCM-copy callback and
observes retro_run start/end for monotonic CPU timings. See
[AUDIO_V0110.md](../../docs/AUDIO_V0110.md). The native bridge validates fixed AV
geometry/rate/cadence and exports the core's sample rate. One pending timer request
permits one retro_run; extra callbacks and coalesced requests are counted. Startup,
pause and resume have no accumulated catch-up loop. The PulseAudio feeder never
runs emulation or accesses the GL context.

Integrated into v0.8.0 using the cached actual PPSSPP core. See
[USB_GAME_BOOT_V080.md](../../docs/USB_GAME_BOOT_V080.md) for the current frontend,
constraints, controls and validation status. The notes below describe the initial
source skeleton and API contract; v0.8.0 now builds bridge.c into a mapped module.
Core and bridge mappings must remain for process lifetime, even after release,
because the actual C++ core registers destructor callbacks.

## Files and ownership

- `bridge.c`: frontend environment/video callbacks, dynamic FBO capture, symbol
  resolution, GLView creation, frame execution and ordered teardown.
- `core-request-example.c`: **core-side example**, showing the full official
  `retro_hw_render_callback` setup for `RETRO_HW_CONTEXT_OPENGLES3`. Do not call
  this instead of the real core's own hardware request.
- `include/libretro.h`: unmodified official header, MIT notice retained; pinned
  to libretro/libretro-common commit `52781c4a8b5aae19d858f4a2326c8f8308c70f54`.
- `platform.h`: scalar EFL declarations and standard GLES/EGL types. No Evas
  structure layouts or function-table offsets are assumed.

The core supplies `context_reset` and `context_destroy` in SET_HW_RENDER. The
frontend fills `get_current_framebuffer` and `get_proc_address` and keeps a copy.
The preferred context is GLES3. GLES2 minimum requests are accepted on the GLES3
context because PPSSPP's inspected `USING_GLES2` constructor requests GLES2.
GLES3 means ES 3.0; an explicit 3.2 requirement uses the distinct Libretro
OPENGLES_VERSION negotiation, not a rewritten GLES3 request. That negotiation is
not implemented here. The reported driver version alone is not core validation.

## Integration sequence

1. Resolve the core's exports through the existing native loading mechanism.
   Keep both core and bridge mappings alive throughout widget lifetime.
2. Call `psp_bridge_begin(retro_run, retro_unload_game, remaining_environment,
   is_ui_thread, log)`. Register `psp_bridge_environment` with
   `retro_set_environment` and `psp_bridge_video` with `retro_set_video_refresh`.
   Also provide input and audio callbacks, then call `retro_init`.
3. The remaining environment handler must implement the core's required services:
   options/default values, paths, logging, input and audio integration, and AV
   geometry/timing. This bridge alone is not a complete Libretro frontend.
4. Load valid content with `retro_load_game(&game)`, then obtain system AV info.
   Only after successful loading call `psp_bridge_create_view(parent,
   av.geometry.base_width, av.geometry.base_height)`. Place and show the returned
   Evas widget. Its fixed-size render surface scales to the displayed viewport.
5. Schedule `psp_bridge_tick` on the Ecore UI thread at the reported AV rate.
   The existing 30 Hz diagnostics timer is not suitable as an assumed emulator
   clock. This skeleton executes one `retro_run` per render callback; production
   timing must handle callback coalescing, repeats, audio pacing and suspension.
6. Stop scheduling before `psp_bridge_close`. The GLView delete callback runs
   `context_destroy` before `retro_unload_game`, while its context is current.
   Then `psp_bridge_release`, `retro_deinit`, and unload the modules. Do not
   unload the core a second time. If release fails, retain the mappings.

GLView itself calls `evas_gl_make_current` before invoking the render callback.
The bridge verifies `eglGetCurrentContext()` instead of trying to manufacture
Evas context/surface handles. The callback reads GL_FRAMEBUFFER_BINDING **before
calling the core**, caches the ID, and supplies it from get_current_framebuffer
throughout that frame. Querying live GL state in the getter would incorrectly
return a core-owned temporary FBO. No hardcoded FBO, glReadPixels, eglSwapBuffers,
or CPU pixel copy is used.

The core must actually render its final image into the supplied FBO. Binding it
after retro_run cannot transfer pixels from a different FBO. A core that ignores
the callback requires an explicit GPU blit/adaptation. Evas handles composition
after returning. The bridge restores read/draw framebuffer bindings and viewport;
full state interaction with Evas and PPSSPP still needs device tests. Only
bottom-left-origin core requests are accepted; verify orientation with asymmetric
content. Hardware video dimensions must match the surface. Geometry changes stop
this skeleton and need deferred surface reconfiguration; they are not silently
stretched by pretending a different render-target size.

`get_proc_address` tries `dlsym` on libGLESv2.so.2 (then .so), followed by
`eglGetProcAddress` for extensions. This is ordinary GLES access to the guarded,
nonzero GLView FBO, with ELM_GLVIEW_DIRECT disabled. EvasGL API-table layouts are
not involved. Requested depth and stencil attachments are checked in the callback.

Pause only suppresses frame requests. Call `psp_bridge_context_lost` if the host
receives a real resource-loss notification; reset occurs in the next current
context. Context handle equality alone cannot detect every driver reset. Do not
call old-context deletion routines after context loss. Widget creation or cleanup
failure after content loading is a fatal prototype path: stop and restart the
application, retaining native mappings until process exit. No guessed GL cleanup
or retry through a destroyed widget is attempted.

## No-content blue menu: unsupported by this core

Inspected PPSSPP commit: `a7d80b680a63ffb72be6e9596da680a51c081a2f`.
Its `libretro/libretro.cpp` reads `game->path` in `retro_load_game` and sets
`coreParam.headLess = true`. It does not advertise SET_SUPPORT_NO_GAME. There is
**no core option or initialization flag here that opens the standalone blue UI**.
Do not pass NULL to retro_load_game. A standalone PPSSPP UI port would be separate
frontend work. Use valid, legally obtained PSP homebrew ELF/PBP content to validate
the actual core shader/output path. A synthetic GLES triangle can validate the
bridge before adding any emulator at all.

The inspected runtime option `ppsspp_cpu_core = "IR JIT"` maps to
`CPUCore::IR_INTERPRETER`, despite its confusing display name. It is a CPU setting,
not a menu switch or proof that all machine-code generation is compiled out.
Compile-time JIT exclusion and other generated-code paths need a separate audit.

Sources:
- https://github.com/hrydgard/ppsspp/blob/a7d80b680a63ffb72be6e9596da680a51c081a2f/libretro/libretro.cpp
- https://github.com/hrydgard/ppsspp/blob/a7d80b680a63ffb72be6e9596da680a51c081a2f/libretro/LibretroGLContext.h
- https://github.com/libretro/libretro-common/blob/52781c4a8b5aae19d858f4a2326c8f8308c70f54/include/libretro.h
- EFL scalar/context ownership reference: `docs/glview-abi-evidence.json` in this project.

## Reproduce the compile checks

From the project root in PowerShell, using the already installed Zig 0.13.0:

```powershell
$zig = 'C:/path/to/user/Documents/Codex/2026-10-30/i-x20/work/native-build-tools/zig/zig-windows-x86_64-0.13.0/zig.exe'
$env:ZIG_GLOBAL_CACHE_DIR = "$PWD/.cache/zig-global"
$env:ZIG_LOCAL_CACHE_DIR = "$PWD/.cache/zig-local"
& $zig cc -Wall -Wextra -Werror checks/libretro-bridge-tests.c -o build/libretro-bridge-tests.exe
& ./build/libretro-bridge-tests.exe
& $zig cc -target arm-linux-gnueabi.2.17 -mcpu=cortex_a9 -marm -O2 -fPIC -Wall -Wextra -Werror -c native/libretro-bridge/bridge.c -o build/libretro-bridge.o
& $zig cc -target arm-linux-gnueabi.2.17 -mcpu=cortex_a9 -marm -O2 -fPIC -Wall -Wextra -Werror -c native/libretro-bridge/core-request-example.c -o build/libretro-core-request.o
```

These produce a mocked Windows test and ARM32 **relocatable objects**, not a
deployable ELF module or TPK. Linking the bridge for the custom loader must avoid
unsupported DT_NEEDED dependencies and resolve its libc/EGL/GLES/EFL imports via
the existing import resolver. That managed host/import wiring is not implemented
by this source-only task. The current native-loading ABI remains unchanged.
