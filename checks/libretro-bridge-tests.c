#undef NDEBUG
#include <assert.h>
#include <stdio.h>
#include <string.h>
#ifdef _WIN32
#include <windows.h>
#endif
#include "../native/libretro-bridge/bridge.c"
static Elm_GLView_Func_Cb render_cb,delete_cb;
static GLint draw=7,read_target=7;
static unsigned resets,runs,destroys,unloads,changes;
static unsigned audio_frames,starts,ends;
static unsigned render_width=480,render_height=272;
static bool mismatched_video;
static int expected_error;
static char last_log[1024];
static unsigned log_count;
static unsigned attachment_queries;
static volatile unsigned test_log_lock;
static EGLContext current_context=(void *)1;
static struct retro_hw_render_callback request;
EGLContext eglGetCurrentContext(void) { return current_context; }
static void proc(void) {}
EGLProc eglGetProcAddress(const char *name) { (void)name; return proc; }
void *dlopen(const char *name,int mode) { (void)name;(void)mode;return (void *)1; }
void *dlsym(void *handle,const char *name) { (void)handle;(void)name;return NULL; }
int dlclose(void *handle) { (void)handle;return 0; }
void glGetIntegerv(GLenum token,GLint *value) {
    switch(token) {
    case GL_FRAMEBUFFER_BINDING:*value=draw;break;
    case GL_READ_FRAMEBUFFER_BINDING:*value=read_target;break;
    case GL_MAJOR_VERSION:*value=3;break;
    case GL_DEPTH_BITS:*value=24;++attachment_queries;break;
    case GL_STENCIL_BITS:*value=8;break;
    case GL_VIEWPORT:value[0]=0;value[1]=0;value[2]=(GLint)render_width;value[3]=(GLint)render_height;break;
    default:assert(0);
    }
}
void glBindFramebuffer(GLenum target,GLuint fbo) {
    if(target==GL_FRAMEBUFFER || target==GL_DRAW_FRAMEBUFFER) draw=(GLint)fbo;
    if(target==GL_FRAMEBUFFER || target==GL_READ_FRAMEBUFFER) read_target=(GLint)fbo;
}
void glViewport(GLint x,GLint y,GLsizei w,GLsizei h) {(void)x;(void)y;(void)w;(void)h;}
GLenum glCheckFramebufferStatus(GLenum target) {(void)target;return GL_FRAMEBUFFER_COMPLETE;}
GLenum glGetError(void) {return 0;}
Evas_Object *elm_glview_version_add(Evas_Object *parent,int version) {assert(parent && version==3);return (void *)2;}
Eina_Bool elm_glview_mode_set(Evas_Object *v,int mode) {(void)v;assert(mode==12);return 1;}
Eina_Bool elm_glview_resize_policy_set(Evas_Object *v,int policy) {(void)v;assert(policy==2);return 1;}
void elm_glview_size_set(Evas_Object *v,int w,int h) {(void)v;assert(w==(int)render_width && h==(int)render_height);}
void elm_glview_init_func_set(Evas_Object *v,Elm_GLView_Func_Cb cb) {(void)v;(void)cb;}
void elm_glview_render_func_set(Evas_Object *v,Elm_GLView_Func_Cb cb) {(void)v;render_cb=cb;}
void elm_glview_del_func_set(Evas_Object *v,Elm_GLView_Func_Cb cb) {(void)v;delete_cb=cb;}
void elm_glview_changed_set(Evas_Object *v) {(void)v;++changes;}
void evas_object_del(Evas_Object *v) {delete_cb(v);}
static bool is_ui(void) {return true;}
static void log_failure(const char *s) {
    while(__sync_lock_test_and_set(&test_log_lock,1)) {}
    snprintf(last_log,sizeof(last_log),"%s",s);++log_count;
    __sync_lock_release(&test_log_lock);
    if(strncmp(s,"CORE_",5)==0 || expected_error)return;
    fprintf(stderr,"Unexpected bridge error: %s\n",s);assert(0);
}
#ifdef _WIN32
static DWORD WINAPI parallel_flood(void *unused) {
    (void)unused;
    for(unsigned i=0;i<5000;i++)core_log(RETRO_LOG_ERROR,"parallel repeat");
    return 0;
}
#endif
static void reset(void) {++resets;assert(request.get_current_framebuffer()==7);draw=99;}
static void run(void) {
    GLuint expected=runs?12:7;
    assert(draw==(GLint)expected);
    draw=99; /* core's temporary offscreen framebuffer */
    assert(request.get_current_framebuffer()==expected);
    glBindFramebuffer(GL_FRAMEBUFFER,expected);
    psp_bridge_video(RETRO_HW_FRAME_BUFFER_VALID,mismatched_video?480:render_width,render_height,0);++runs;
}
static void destroy(void) {++destroys;}
static void unload(void) {assert(destroys>unloads);++unloads;}
static size_t audio_sink(const int16_t *samples,size_t frames) {assert(samples && samples[0]==123 && samples[1]==-456);audio_frames+=(unsigned)frames;return frames;}
static void observe(unsigned phase) {if(!phase)++starts;else ++ends;}
static void av_info(struct retro_system_av_info *info) {memset(info,0,sizeof(*info));info->geometry.base_width=480;info->geometry.base_height=272;info->timing.fps=60.0/1.001;info->timing.sample_rate=44100;}
int main(void) {
    assert(psp_bridge_begin(run,unload,NULL,is_ui,log_failure));
    struct retro_log_callback logger;
    assert(psp_bridge_environment(RETRO_ENVIRONMENT_GET_LOG_INTERFACE,&logger));
    logger.log(RETRO_LOG_WARN,"warning %d",42);assert(strcmp(last_log,"CORE_LOG W warning 42")==0);
    logger.log(RETRO_LOG_ERROR,"error %s","sema");assert(strcmp(last_log,"CORE_LOG E error sema")==0);
    unsigned flood_start=log_count;
    for(unsigned i=0;i<10000;i++) {
        logger.log(RETRO_LOG_ERROR,"error %s","sema");
        logger.log(RETRO_LOG_WARN,"warning %d",42);
    }
    assert(psp_bridge_log_suppressed()==20000 && log_count-flood_start==26);
#ifdef _WIN32
    core_log(RETRO_LOG_ERROR,"parallel repeat");
    HANDLE workers[4];
    for(unsigned i=0;i<4;i++) {workers[i]=CreateThread(NULL,0,parallel_flood,NULL,0,NULL);assert(workers[i]);}
    assert(WaitForMultipleObjects(4,workers,TRUE,10000)==WAIT_OBJECT_0);
    for(unsigned i=0;i<4;i++)CloseHandle(workers[i]);
    assert(psp_bridge_log_suppressed()==40000);
#endif
    // New errors still pass, even after the fixed table fills.
    for(unsigned i=0;i<40;i++)logger.log(RETRO_LOG_ERROR,"distinct error %u",i);
    assert(strcmp(last_log,"CORE_LOG E distinct error 39")==0);
    unsigned before=log_count;logger.log(RETRO_LOG_INFO,"muted info");assert(log_count==before);
    psp_bridge_set_jit_capable(true);logger.log(RETRO_LOG_INFO,"jit info");assert(strcmp(last_log,"CORE_LOG I jit info")==0);
    char long_text[2000];memset(long_text,'x',sizeof(long_text)-1);long_text[sizeof(long_text)-1]=0;
    logger.log(RETRO_LOG_ERROR,"%s",long_text);assert(strlen(last_log)==1023 && strncmp(last_log,"CORE_LOG E ",11)==0);
    psp_bridge_set_jit_capable(false);
    psp_bridge_set_host_services(audio_sink,observe);
    assert(psp_bridge_frame_usec(av_info)==16683 && psp_bridge_sample_rate()==44100);
    struct retro_system_av_info av;av_info(&av);
    assert(psp_bridge_environment(RETRO_ENVIRONMENT_SET_SYSTEM_AV_INFO,&av));
    av.timing.sample_rate=0;assert(!accept_av(&av));av.timing.sample_rate=44100.5;assert(!accept_av(&av));
    av.timing.sample_rate=48000;assert(!accept_av(&av));av.timing.sample_rate=44100;av.timing.fps=30;assert(!accept_av(&av));
    int16_t pcm[2]={123,-456};assert(psp_bridge_audio_batch(pcm,1)==1);psp_bridge_audio(123,-456);assert(audio_frames==2);
    assert(psp_bridge_audio_batch(NULL,1)==0);
    assert(!psp_bridge_supports_no_game());
    bool jit_capable=true;
    assert(psp_bridge_environment(RETRO_ENVIRONMENT_GET_JIT_CAPABLE,&jit_capable));
    assert(!jit_capable);
    psp_bridge_set_jit_capable(true);assert(psp_bridge_environment(RETRO_ENVIRONMENT_GET_JIT_CAPABLE,&jit_capable) && jit_capable);
    psp_bridge_set_jit_capable(false);
    request.context_type=RETRO_HW_CONTEXT_OPENGLES3;
    request.context_reset=reset;request.context_destroy=destroy;
    request.depth=true;request.stencil=true;request.bottom_left_origin=true;
    assert(psp_bridge_environment(RETRO_ENVIRONMENT_SET_HW_RENDER,&request));
    assert(request.get_proc_address("glExample")==proc);
    assert(psp_bridge_create_view((void *)1,480,272));
    psp_bridge_tick();psp_bridge_tick();assert(psp_bridge_coalesced()==1);
    render_cb((void *)2);assert(draw==7 && runs==1 && resets==1);
    draw=12;read_target=12;psp_bridge_tick();render_cb((void *)2);
    assert(draw==12 && runs==2 && resets==1);
    render_cb((void *)2);assert(runs==2 && psp_bridge_unscheduled()==1 && starts==2 && ends==2);
    assert(request.get_current_framebuffer()==0); /* outside callback scope */
    psp_bridge_set_paused(true);psp_bridge_tick();assert(changes==3);
    assert(psp_bridge_audio_batch(pcm,1)==1 && audio_frames==2);
    psp_bridge_set_paused(false);psp_bridge_tick();assert(changes==4);
    psp_bridge_close();assert(destroys==1 && unloads==1);
    assert(psp_bridge_frames()==2);
    psp_bridge_buttons(1u<<8); psp_bridge_input_poll(); assert(psp_bridge_input(0,1,0,8)==1); assert(psp_bridge_input(1,1,0,8)==0);
    assert(psp_bridge_audio_batch(pcm,1)==1 && audio_frames==2);
    psp_bridge_reset_input();psp_bridge_buttons(1u<<3);psp_bridge_buttons(0);
    psp_bridge_input_poll();assert(psp_bridge_input(0,1,0,3)==1);assert(psp_bridge_input_frame()==8 && psp_bridge_input_last()==8);
    psp_bridge_input_poll();assert(psp_bridge_input(0,1,0,3)==0);
    psp_bridge_buttons((1u<<2)|(1u<<3));psp_bridge_input_poll();
    assert(psp_bridge_input(0,1,0,2)==1 && psp_bridge_input(0,1,0,3)==1);
    psp_bridge_reset_input();psp_bridge_input_poll();assert(psp_bridge_input(0,1,0,3)==0);
    // Both sticks are snapshotted once per core poll; buttons remain independent.
    psp_bridge_sticks(-32767,12345,32767,-12345);
    assert(psp_bridge_input(0,RETRO_DEVICE_ANALOG,0,0)==0);
    psp_bridge_buttons(1u<<8);psp_bridge_input_poll();
    assert(psp_bridge_input(0,RETRO_DEVICE_ANALOG,0,0)==-32767);
    assert(psp_bridge_input(0,RETRO_DEVICE_ANALOG,0,1)==12345);
    assert(psp_bridge_input(0,RETRO_DEVICE_ANALOG,1,0)==32767);
    assert(psp_bridge_input(0,RETRO_DEVICE_ANALOG,1,1)==-12345);
    assert(psp_bridge_input(0,RETRO_DEVICE_JOYPAD,0,8)==1);
    assert(psp_bridge_input_axis(2)==32767 && psp_bridge_input_axis(4)==0);
    assert(psp_bridge_input(1,RETRO_DEVICE_ANALOG,0,0)==0);
    assert(psp_bridge_input(0,RETRO_DEVICE_ANALOG,2,0)==0);
    assert(psp_bridge_input(0,RETRO_DEVICE_ANALOG,0,2)==0);
    psp_bridge_sticks(-99999,99999,0,0);
    assert(psp_bridge_input_axis(1)==12345);psp_bridge_input_poll();
    assert(psp_bridge_input_axis(0)==-32767 && psp_bridge_input_axis(1)==32767);
    psp_bridge_set_paused(true);assert(psp_bridge_input_axis(0)==0 && psp_bridge_input_frame()==0);
    psp_bridge_set_paused(false);psp_bridge_input_poll();assert(psp_bridge_input_axis(1)==0);
    psp_bridge_sticks(20000,10000,-20000,-10000);psp_bridge_reset_input();psp_bridge_input_poll();
    for(unsigned i=0;i<4;i++)assert(psp_bridge_input_axis(i)==0);
    puts("PASS: left/right analog delivery, poll snapshots, clamp, invalid queries, pause and reset");
    // Raw shortcut prefixes can arrive before one input_poll. Merely masking the
    // completed chord does not remove those latched edges. The host must reset
    // the native tap latch on accepted Select+L+R, then restore other held bits.
    const uint32_t select=1u<<2,left=1u<<10,right=1u<<11,cross=1u;
    psp_bridge_buttons(select|cross);psp_bridge_buttons(select|left|cross);
    psp_bridge_buttons(cross); // completed chord is masked by the host
    assert((b.pending_buttons&(select|left))==(select|left));
    psp_bridge_reset_input();psp_bridge_buttons(cross);psp_bridge_input_poll();
    assert(psp_bridge_input_frame()==cross);
    // The host's consumed-chord latch still filters each partial release.
    for(unsigned released=0;released<3;released++) {
        psp_bridge_buttons(cross);psp_bridge_input_poll();
        assert((psp_bridge_input_frame()&(select|left|right))==0 && psp_bridge_input(0,1,0,0)==1);
    }
    psp_bridge_buttons(0);psp_bridge_input_poll();assert(psp_bridge_input_frame()==0);
    psp_bridge_buttons(left);psp_bridge_input_poll();assert(psp_bridge_input_frame()==left);
    psp_bridge_reset_input();
    assert(psp_bridge_release());
    // A changed context must stop, not reuse stale core GPU resources.
    assert(psp_bridge_begin(run,unload,NULL,is_ui,log_failure));
    request.context_type=RETRO_HW_CONTEXT_OPENGLES3;
    assert(psp_bridge_environment(RETRO_ENVIRONMENT_SET_HW_RENDER,&request));
    assert(psp_bridge_create_view((void *)1,480,272));
    b.ready=true;b.context=(void *)1;current_context=(void *)3;expected_error=1;
    psp_bridge_tick();render_cb((void *)2);assert(psp_bridge_failed() && runs==2);
    // Do not attempt stale-context cleanup after a real context loss.
    b.loaded=false;psp_bridge_close();assert(psp_bridge_release());
    assert(psp_bridge_begin(run,unload,NULL,is_ui,log_failure));
    request.context_type=RETRO_HW_CONTEXT_VULKAN;
    assert(!psp_bridge_environment(RETRO_ENVIRONMENT_SET_HW_RENDER,&request));
    assert(!psp_bridge_create_view((void *)1,480,272));assert(psp_bridge_release());
    // The 2x surface must be negotiated and rendered at exactly 960x544.
    expected_error=0;draw=read_target=7;current_context=(void *)1;runs=resets=0;
    render_width=960;render_height=544;
    assert(psp_bridge_begin(run,unload,NULL,is_ui,log_failure));
    av_info(&av);av.geometry.base_width=960;av.geometry.base_height=544;
    assert(accept_av(&av));
    av.geometry.base_width=480;av.geometry.base_height=272;assert(!accept_av(&av));
    av.geometry.base_width=1440;av.geometry.base_height=816;assert(!accept_av(&av));
    request.context_type=RETRO_HW_CONTEXT_OPENGLES3;
    assert(psp_bridge_environment(RETRO_ENVIRONMENT_SET_HW_RENDER,&request));
    assert(!psp_bridge_create_view((void *)1,480,272));
    assert(!psp_bridge_create_view((void *)1,960,540));
    assert(psp_bridge_create_view((void *)1,960,544));
    psp_bridge_tick();render_cb((void *)2);assert(psp_bridge_frames()==1 && runs==1 && !psp_bridge_failed());
    draw=read_target=12;psp_bridge_tick();render_cb((void *)2);
    unsigned checked=attachment_queries;
    psp_bridge_tick();render_cb((void *)2);
    assert(attachment_queries==checked && psp_bridge_frames()==3);
    // Periodic validation also catches reused framebuffer identifiers.
    b.tick_requests=119;psp_bridge_tick();render_cb((void *)2);
    assert(attachment_queries==checked+1 && psp_bridge_frames()==4);
    draw=read_target=12;expected_error=1;mismatched_video=true;
    psp_bridge_tick();render_cb((void *)2);assert(psp_bridge_failed() && psp_bridge_frames()==4);
    psp_bridge_close();assert(psp_bridge_release());
    puts("PASS: GLView FBO/context ownership, audio/AV validation, 1x/2x render sizes and mismatch rejection, frame requests, pause and cleanup order");
    return 0;
}
