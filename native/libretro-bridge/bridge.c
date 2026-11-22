#include "bridge.h"
#include <stdarg.h>
extern void *memset(void *,int,size_t);
extern int vsnprintf(char *,size_t,const char *,va_list);
extern int snprintf(char *,size_t,const char *,...);
extern int strcmp(const char *,const char *);
extern char *strcpy(char *,const char *);

/* Keep repeated warnings/errors out of the managed callback. Exact matching,
 * fixed capacity, and fail-open on saturation preserve all new diagnostics.
 * The lock never covers formatting or the callback (which may be reentrant). */
static struct { char text[1024]; unsigned count; } repeats[32];
static volatile unsigned repeat_lock, native_suppressed;

static struct {
    struct retro_hw_render_callback hw; /* official header, never a hand-padded ABI */
    Evas_Object *view;
    void *gles;
    EGLContext context;
    GLuint target_fbo;
    GLuint validated_fbo;
    unsigned width,height,frames,duplicates,frame_usec,sample_rate;
    unsigned tick_requests,coalesced,unscheduled;
    uint32_t buttons,pending_buttons,frame_buttons,last_press,input_polls;
    int16_t axes[4],frame_axes[4];
    bool accepted,ready,scope,paused,closing,failed,supports_no_game;
    bool loaded,unloaded,invalidated,frame_requested,jit_capable;
    retro_audio_sample_batch_t audio;
    void (*observe_run)(unsigned phase);
    void (*run)(void);
    void (*unload)(void);
    bool (*ui)(void);
    void (*log)(const char *);
    retro_environment_t remaining;
} b;

static void fail(const char *message) {
    if(!b.failed && b.log) b.log(message);
    b.failed=true;
}
static bool ui(void) { return b.ui && b.ui(); }
static bool supported_geometry(unsigned width,unsigned height) {
    return (width==480 && height==272) || (width==960 && height==544);
}
static bool accept_av(const struct retro_system_av_info *info) {
    if(!info || !supported_geometry(info->geometry.base_width,info->geometry.base_height) ||
       (b.width && (b.width!=info->geometry.base_width || b.height!=info->geometry.base_height)) ||
       !(info->timing.fps>=1.0 && info->timing.fps<=120.0) ||
       !(info->timing.sample_rate>=8000.0 && info->timing.sample_rate<=192000.0)) return false;
    unsigned usec=(unsigned)(1000000.0/info->timing.fps),rate=(unsigned)info->timing.sample_rate;
    if(info->timing.sample_rate!=(double)rate || (b.frame_usec && (usec!=b.frame_usec || rate!=b.sample_rate)))return false;
    b.frame_usec=usec;b.sample_rate=rate;
    b.width=info->geometry.base_width;b.height=info->geometry.base_height;return true;
}

static uintptr_t RETRO_CALLCONV current_framebuffer(void) {
    /* NEVER query live GL state here: the core may currently have a temporary
     * offscreen FBO bound. The render callback captures the GLView target afresh. */
    return ui() && b.scope ? (uintptr_t)b.target_fbo : 0;
}
static retro_proc_address_t RETRO_CALLCONV proc_address(const char *name) {
    if(!name || !b.gles) return NULL;
    /* POSIX/Tizen supports conversion of dlsym function addresses to function
     * pointers. Query exports first, then the EGL extension resolver. */
    void *p=dlsym(b.gles,name);
    if(p) return (retro_proc_address_t)p;
    return (retro_proc_address_t)eglGetProcAddress(name);
}
bool psp_bridge_begin(void (*run)(void),void (*unload)(void),
    retro_environment_t remaining,bool (*is_ui)(void),void (*log)(const char *)) {
    if(b.gles || !run || !unload || !is_ui || !is_ui()) return false;
    memset(&b,0,sizeof(b)); b.run=run; b.unload=unload; b.remaining=remaining; b.ui=is_ui; b.log=log;
    memset(repeats,0,sizeof(repeats));repeat_lock=native_suppressed=0;
    b.gles=dlopen("libGLESv2.so.2",2); /* RTLD_NOW */
    if(!b.gles) b.gles=dlopen("libGLESv2.so",2);
    return b.gles!=NULL;
}
static void RETRO_CALLCONV core_log(enum retro_log_level,const char *,...);
bool RETRO_CALLCONV psp_bridge_environment(unsigned command,void *data) {
    switch(command) {
    case RETRO_ENVIRONMENT_SET_SYSTEM_AV_INFO:
        if(!accept_av(data)){fail("Unsupported AV timing/rate/geometry change; selected render size requires relaunch");return false;}
        return true;
    case RETRO_ENVIRONMENT_GET_LOG_INTERFACE:
        if(!data) return false;
        ((struct retro_log_callback *)data)->log=core_log; return true;
    case RETRO_ENVIRONMENT_SET_PIXEL_FORMAT:
        return data && *(enum retro_pixel_format *)data==RETRO_PIXEL_FORMAT_XRGB8888;
    case RETRO_ENVIRONMENT_SHUTDOWN:
        fail("CORE_BOOT_FAILED: core requested shutdown; see preceding core log"); return true;
    case RETRO_ENVIRONMENT_GET_JIT_CAPABLE:
        if(!data) return false;
        *(bool *)data=b.jit_capable; /* Only the separately pinned, probe-gated experiment opts in. */
        return true;
    case RETRO_ENVIRONMENT_GET_PREFERRED_HW_RENDER:
        if(!data) return false;
        *(enum retro_hw_context_type *)data=RETRO_HW_CONTEXT_OPENGLES3;
        return true;
    case RETRO_ENVIRONMENT_SET_SUPPORT_NO_GAME:
        if(!data) return false;
        b.supports_no_game=*(const bool *)data; return true;
    case RETRO_ENVIRONMENT_SET_HW_RENDER: {
        if(!data || !ui() || b.view || b.accepted) return false;
        struct retro_hw_render_callback *request=data;
        /* Prefer GLES3. Current PPSSPP's USING_GLES2 build requests GLES2 even
         * on a GLES3-capable driver, so accept that compatible minimum too.
         * Do not rewrite the core's requested context_type or callbacks. */
        if(request->context_type!=RETRO_HW_CONTEXT_OPENGLES3 &&
           request->context_type!=RETRO_HW_CONTEXT_OPENGLES2) return false;
        if(!request->context_reset || !request->bottom_left_origin || request->debug_context)
            return false; /* no orientation-blit/debug-context implementation here */
        if(request->stencil && !request->depth) return false;
        request->get_current_framebuffer=current_framebuffer;
        request->get_proc_address=proc_address;
        b.hw=*request; /* retain CORE-owned reset/destroy callbacks */
        b.accepted=true; return true;
    }
    default: return b.remaining ? b.remaining(command,data) : false;
    }
}
static void RETRO_CALLCONV core_log(enum retro_log_level level,const char *fmt,...) {
    if(level<(b.jit_capable?RETRO_LOG_INFO:RETRO_LOG_WARN) || !b.log) return;
    /* Preserve severity for bounded host-side aggregation and first-error capture. */
    char message[1024]="CORE_LOG ? ";
    message[9]=level==RETRO_LOG_ERROR?'E':level==RETRO_LOG_WARN?'W':level==RETRO_LOG_INFO?'I':'D';
    va_list args; va_start(args,fmt);
    int length=vsnprintf(message+11,sizeof(message)-11,fmt,args); va_end(args);
    unsigned count=1;
    /* Truncated messages cannot be compared exactly: always pass them through. */
    if(level>=RETRO_LOG_WARN && length>=0 && length<(int)sizeof(message)-11) {
        while(__sync_lock_test_and_set(&repeat_lock,1)) {}
        for(unsigned i=0;i<32;i++) {
            if(!repeats[i].count) {strcpy(repeats[i].text,message);repeats[i].count=1;break;}
            if(strcmp(repeats[i].text,message)==0) {count=++repeats[i].count;break;}
        }
        if(count>1)__sync_fetch_and_add(&native_suppressed,1);
        __sync_lock_release(&repeat_lock);
    }
    if(count==1)b.log(message);
    else if((count&(count-1))==0) {
        char summary[1152];
        snprintf(summary,sizeof(summary),"CORE_REPEAT count=%u message=%s",count,message);
        b.log(summary);
    }
}
unsigned psp_bridge_log_suppressed(void) {return __sync_fetch_and_add(&native_suppressed,0);}
void psp_bridge_set_jit_capable(bool capable) {if(ui() && !b.loaded)b.jit_capable=capable;}
void psp_bridge_set_host_services(retro_audio_sample_batch_t audio,void (*observe_run)(unsigned)) {
    if(ui() && !b.loaded){b.audio=audio;b.observe_run=observe_run;}
}
size_t psp_bridge_audio_batch(const int16_t *samples,size_t frames) {
    if(frames && !samples)return 0;
    if(b.audio && !b.paused && !b.closing && !b.failed)return b.audio(samples,frames);
    return frames; /* Deliberately consume when muted/paused; never block retro_run. */
}
void psp_bridge_audio(int16_t left,int16_t right) {int16_t samples[2]={left,right};psp_bridge_audio_batch(samples,1);}
void psp_bridge_input_poll(void) {
    b.frame_buttons=b.buttons|b.pending_buttons;b.pending_buttons=0;
    for(unsigned i=0;i<4;i++)b.frame_axes[i]=b.axes[i];
    ++b.input_polls;if(b.frame_buttons)b.last_press=b.frame_buttons;
}
int16_t psp_bridge_input(unsigned port,unsigned device,unsigned index,unsigned id) {
    if(port!=0)return 0;
    if(device==RETRO_DEVICE_ANALOG)return index<2 && id<2 ? b.frame_axes[index*2+id] : 0;
    return device==RETRO_DEVICE_JOYPAD && index==0 && id<16 ? ((b.frame_buttons>>id)&1) : 0;
}
void psp_bridge_buttons(uint32_t buttons) { b.pending_buttons|=buttons&~b.buttons;b.buttons=buttons; }
void psp_bridge_sticks(int lx,int ly,int rx,int ry) {
    int values[4]={lx,ly,rx,ry};
    for(unsigned i=0;i<4;i++)b.axes[i]=(int16_t)(values[i]<-32767?-32767:values[i]>32767?32767:values[i]);
}
int psp_bridge_input_axis(unsigned axis) { return axis<4?b.frame_axes[axis]:0; }
void psp_bridge_reset_input(void) { b.buttons=b.pending_buttons=b.frame_buttons=0;memset(b.axes,0,sizeof(b.axes));memset(b.frame_axes,0,sizeof(b.frame_axes)); }
unsigned psp_bridge_frame_usec(void (*get_info)(struct retro_system_av_info *)) {
    struct retro_system_av_info info; memset(&info,0,sizeof(info)); get_info(&info);
    return accept_av(&info)?b.frame_usec:0;
}
unsigned psp_bridge_sample_rate(void) {return b.sample_rate;}
unsigned psp_bridge_duplicates(void) {return b.duplicates;}
unsigned psp_bridge_tick_requests(void) {return b.tick_requests;}
unsigned psp_bridge_coalesced(void) {return b.coalesced;}
unsigned psp_bridge_unscheduled(void) {return b.unscheduled;}
unsigned psp_bridge_input_frame(void) { return b.frame_buttons; }
unsigned psp_bridge_input_last(void) { return b.last_press; }
unsigned psp_bridge_input_polls(void) { return b.input_polls; }
unsigned psp_bridge_frames(void) { return b.frames; }
unsigned psp_bridge_failed(void) { return b.failed; }
unsigned psp_bridge_fbo(void) { return b.target_fbo; }
bool psp_bridge_supports_no_game(void) { return b.supports_no_game; }

void RETRO_CALLCONV psp_bridge_video(const void *data,unsigned width,unsigned height,size_t pitch) {
    (void)pitch;
    if(!ui() || !b.scope) { fail("HW video callback outside owning GLView frame"); return; }
    if(!data) {++b.duplicates;return;} /* duplicate frame; do not clear the previous image */
    if(data!=RETRO_HW_FRAME_BUFFER_VALID) { fail("Software video not supported by this HW bridge"); return; }
    /* Evas composites the GLView texture after this callback returns.
     * No glReadPixels, Evas image copy or eglSwapBuffers here. */
    if(width!=b.width || height!=b.height) {
        fail("Unsupported video geometry; frame must match selected PSP render size"); return;
    }
    ++b.frames;
    if(b.frames==1 && b.log) b.log("CORE_HW_VIDEO: first hardware video callback (visual confirmation required)");
}

static void on_init(Evas_Object *view) {
    (void)view;
    /* GLView has already made its EGL context current. This may also signal
     * resource invalidation. Do not try to free resources in a lost context. */
    if(b.ready) { b.invalidated=true; fail("GLView reinitialized; relaunch required for core resource recovery"); }
    b.ready=false;
}
static void on_render(Evas_Object *view) {
    (void)view;
    if(!ui() || b.scope) { fail("Wrong-thread/reentrant GLView render"); return; }
    if(b.failed || b.paused || b.closing || !b.accepted || !b.loaded) return;
    if(!b.frame_requested){++b.unscheduled;return;}
    b.frame_requested=false; /* Consume one scheduler request, including coalesced ticks. */

    /* GLView's implementation calls evas_gl_make_current BEFORE on_render.
     * EGLContext and Evas_GL_Context are NOT interchangeable handles. */
    EGLContext context=eglGetCurrentContext();
    if(!context) { fail("GLView callback has no current EGL context"); return; }
    GLint fbo=0,read_fbo=0,viewport[4],major=0,depth=0,stencil=0;
    glGetIntegerv(GL_FRAMEBUFFER_BINDING,&fbo); /* dynamic, once per host frame */
    if(glGetError() || fbo<=0) { fail("Expected GLES3 and a nonzero GLView framebuffer"); return; }
    if(glCheckFramebufferStatus(GL_FRAMEBUFFER)!=GL_FRAMEBUFFER_COMPLETE) { fail("Incomplete GLView FBO"); return; }
    /* Context version and attachment sizes are stable until the target changes.
     * Still capture/check the actual target and preserve GL state every frame. */
    if(!b.ready || b.validated_fbo!=(GLuint)fbo || b.tick_requests%120==0) {
        glGetIntegerv(GL_MAJOR_VERSION,&major);
        glGetIntegerv(GL_DEPTH_BITS,&depth); glGetIntegerv(GL_STENCIL_BITS,&stencil);
        if(glGetError() || major<3 || (b.hw.depth && depth==0) || (b.hw.stencil && stencil==0)) {
            fail("Required GLES3 depth/stencil attachment missing"); return;
        }
        b.validated_fbo=(GLuint)fbo;
    }
    glGetIntegerv(GL_READ_FRAMEBUFFER_BINDING,&read_fbo); glGetIntegerv(GL_VIEWPORT,viewport);
    if(glGetError()) { fail("GLView framebuffer/state query failed"); return; }

    b.target_fbo=(GLuint)fbo;
    b.scope=true;
    if(b.ready && b.context!=context) { b.invalidated=true; fail("GL context changed; relaunch required"); b.scope=false;return; }
    if(!b.ready) {
        b.context=context;
        if(b.log) b.log("CORE_STAGE context_reset");
        b.hw.context_reset(); /* current context and stable target FBO available */
        b.ready=true;
        if(b.log) b.log("CORE_STAGE context_reset returned");
    }
    glBindFramebuffer(GL_FRAMEBUFFER,b.target_fbo);
    glViewport(0,0,(GLsizei)b.width,(GLsizei)b.height);
    if(!b.failed) {
        if(!b.frames && b.log) b.log("CORE_STAGE retro_run");
        if(b.observe_run)b.observe_run(0);
        b.run(); /* loaded core's retro_run(), on the Ecore UI thread */
        if(b.observe_run)b.observe_run(1);
    }
    /* A compliant core renders its final image into current_framebuffer().
     * Merely rebinding this FBO cannot copy output from an unrelated core FBO. */
    glBindFramebuffer(GL_DRAW_FRAMEBUFFER,b.target_fbo);
    glBindFramebuffer(GL_READ_FRAMEBUFFER,(GLuint)read_fbo);
    glViewport(viewport[0],viewport[1],viewport[2],viewport[3]);
    if(glGetError()) fail("GL error during core frame or framebuffer restoration");
    b.scope=false;
}

static void on_delete(Evas_Object *view) {
    (void)view;
    /* Evas will destroy the widget even if core cleanup cannot run. Never leave
     * a dangling widget pointer. In that failure case retain loaded code and
     * refuse release; the host must stop/restart the process. */
    b.view=NULL;
    /* GLView makes the context current before its delete callback as well.
     * A real frontend must route this callback before unloading the core. */
    if(!ui()) { fail("GLView delete callback on wrong thread"); return; }
    if(b.loaded) {
        if(!b.ready || b.invalidated || !eglGetCurrentContext() || b.context!=eglGetCurrentContext()) { fail("Cannot cleanly unload core without its initialized context; retain until exit"); return; }
        GLint fbo=0; glGetIntegerv(GL_FRAMEBUFFER_BINDING,&fbo);
        b.target_fbo=fbo>0?(GLuint)fbo:0; b.scope=true;
        if(b.ready && b.hw.context_destroy) b.hw.context_destroy();
        /* PPSSPP's context_destroy dereferences its ctx object, so it MUST run
         * before retro_unload_game deletes that object. */
        b.ready=false; if(b.log) b.log("CORE_STAGE retro_unload_game"); b.unload(); b.loaded=false; b.unloaded=true; b.scope=false;
    }
}
Evas_Object *psp_bridge_create_view(Evas_Object *parent,unsigned width,unsigned height) {
    if(!ui() || !b.accepted || b.view || !parent || !supported_geometry(width,height) ||
       (b.width && (b.width!=width || b.height!=height))) return NULL;
    /* Caller has successfully loaded content; do not use this after load failure. */
    b.loaded=true; b.width=width; b.height=height;
    b.view=elm_glview_version_add(parent,EVAS_GL_GLES_3_X);
    if(!b.view) { fail("GLES3 GLView creation failed; caller must handle core-load rollback"); return NULL; }
    elm_glview_del_func_set(b.view,on_delete);
    int mode=(b.hw.depth?ELM_GLVIEW_DEPTH:0)|(b.hw.stencil?ELM_GLVIEW_STENCIL:0);
    /* No ELM_GLVIEW_DIRECT; core GL functions refer to a genuine nonzero FBO. */
    if(!elm_glview_mode_set(b.view,mode) ||
       !elm_glview_resize_policy_set(b.view,ELM_GLVIEW_RESIZE_POLICY_SCALE)) {
        fail("GLView configuration rejected"); psp_bridge_close(); return NULL;
    }
    elm_glview_size_set(b.view,(int)width,(int)height);
    elm_glview_init_func_set(b.view,on_init);
    elm_glview_render_func_set(b.view,on_render);
    return b.view; /* caller places/shows widget; fixed surface size scales visually */
}
void psp_bridge_tick(void) {
    if(ui() && b.view && !b.paused && !b.closing && !b.failed) {
        ++b.tick_requests;if(b.frame_requested)++b.coalesced;
        b.frame_requested=true;elm_glview_changed_set(b.view);
    }
}
void psp_bridge_set_paused(bool paused) { if(ui()) {b.paused=paused;b.frame_requested=false;psp_bridge_reset_input();} }
void psp_bridge_context_lost(void) { if(ui()) { b.ready=false; b.context=NULL; } }
void psp_bridge_close(void) {
    if(!ui() || b.scope || b.closing) return;
    b.closing=true;
    if(b.view) {
        elm_glview_render_func_set(b.view,NULL);
        elm_glview_init_func_set(b.view,NULL);
        /* Retain delete callback so core cleanup runs while the context is current. */
        evas_object_del(b.view);
    }
}
bool psp_bridge_release(void) {
    if(!ui() || b.view || b.scope || b.loaded) return false;
    if(b.gles) dlclose(b.gles);
    memset(&b,0,sizeof(b)); return true;
}
