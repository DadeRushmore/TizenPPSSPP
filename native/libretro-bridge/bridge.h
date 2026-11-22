/* GPL-2.0. Single-core, single-GLView reference bridge, not a full frontend. */
#ifndef TIZENPSP_LIBRETRO_BRIDGE_H
#define TIZENPSP_LIBRETRO_BRIDGE_H
#define RETRO_API /* This frontend consumes types; it does not export the core API. */
#include "include/libretro.h"
#include "platform.h"
#ifdef __cplusplus
extern "C" {
#endif
/* run/unload_game must be the loaded core's retro_run/retro_unload_game exports.
 * remaining_environment supplies directories, logging, options, geometry, etc.
 * All functions except proc lookup must run on the owning Ecore UI thread.
 */
bool psp_bridge_begin(void (*run)(void),void (*unload_game)(void),
    retro_environment_t remaining_environment,bool (*is_ui_thread)(void),
    void (*log)(const char *));
bool RETRO_CALLCONV psp_bridge_environment(unsigned command,void *data);
/* Install before create_view; callback owners must remain rooted for core life.
 * Audio is copied synchronously by the host and must never block on playback.
 * Observer phases 0/1 bracket retro_run, excluding context_reset and restoration.
 */
void psp_bridge_set_host_services(retro_audio_sample_batch_t audio,void (*observe_run)(unsigned phase));
void RETRO_CALLCONV psp_bridge_video(const void *data,unsigned width,unsigned height,size_t pitch);
bool psp_bridge_supports_no_game(void);
/* After successful retro_load_game(), create this widget, then place/show it.
 * The owner must schedule invalidation at the core's AV-info rate, not arbitrarily
 * tie emulation to the v0.3.0 diagnostic timer's 30 Hz.
 */
Evas_Object *psp_bridge_create_view(Evas_Object *parent,unsigned render_width,unsigned render_height);
void psp_bridge_tick(void);
void psp_bridge_set_paused(bool paused);
/* Signal actual context loss before the next render callback, if notified. */
void psp_bridge_context_lost(void);
/* Request close on UI thread. Keep core and bridge code loaded until release succeeds.
 * The delete callback calls context_destroy BEFORE retro_unload_game while current.
 * Call core retro_deinit() after release, then unload the core/bridge modules.
 */
void psp_bridge_close(void);
bool psp_bridge_release(void);
#ifdef __cplusplus
}
#endif
#endif
