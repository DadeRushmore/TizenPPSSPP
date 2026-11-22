/* CORE-SIDE example only. A frontend must not replace a real core's request.
 * PPSSPP already issues SET_HW_RENDER in LibretroHWRenderContext::InitHW().
 */
#include "bridge.h"
extern void example_core_context_reset(void);
extern void example_core_context_destroy(void);

bool example_core_request_gles3(retro_environment_t environment) {
    static struct retro_hw_render_callback hw;
    hw=(struct retro_hw_render_callback){0};
    hw.context_type=RETRO_HW_CONTEXT_OPENGLES3; /* ES 3.0; not a demand for ES 3.2 */
    hw.version_major=3; hw.version_minor=0;
    hw.context_reset=example_core_context_reset;
    hw.context_destroy=example_core_context_destroy;
    hw.depth=true; hw.stencil=true; hw.bottom_left_origin=true;
    hw.cache_context=false;
    /* Frontend fills get_current_framebuffer and get_proc_address here. */
    return environment(RETRO_ENVIRONMENT_SET_HW_RENDER,&hw);
}
