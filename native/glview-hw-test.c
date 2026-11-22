/* Synthetic Libretro hardware core: exercises the callback contract on the TV.
 * Diagnostic only: shaders are rebuilt per frame for context-safe cleanup.
 * The exported entry point preserves the v0.3 managed/native ARM32 ABI.
 */
#define tizenpsp_glview_clear guarded_clear
#include "glview-clear.c"
#undef tizenpsp_glview_clear
#define RETRO_API
#include "libretro-bridge/include/libretro.h"
typedef unsigned int GLuint;
extern void (*eglGetProcAddress(const char *))(void);
extern GLuint glCreateShader(GLenum);
extern void glShaderSource(GLuint,int,const char *const *,const int *);
extern void glCompileShader(GLuint);
extern void glGetShaderiv(GLuint,GLenum,GLint *);
extern void glGetShaderInfoLog(GLuint,int,int *,char *);
extern void glDeleteShader(GLuint);
extern GLuint glCreateProgram(void);
extern void glAttachShader(GLuint,GLuint);
extern void glLinkProgram(GLuint);
extern void glGetProgramiv(GLuint,GLenum,GLint *);
extern void glGetProgramInfoLog(GLuint,int,int *,char *);
extern void glDeleteProgram(GLuint);
extern void glUseProgram(GLuint);
extern GLint glGetUniformLocation(GLuint,const char *);
extern void glUniform1i(GLint,GLint);
extern void glGenVertexArrays(int,GLuint *);
extern void glBindVertexArray(GLuint);
extern void glDeleteVertexArrays(int,const GLuint *);
extern void glBindFramebuffer(GLenum,GLuint);
extern void glDrawArrays(GLenum,GLint,int);
static GLuint host_fbo;
static unsigned frame;
#ifdef TIZENPSP_CPP_STARTUP_TEST
extern int tizenpsp_cpp_probe(void);
#endif
static uintptr_t RETRO_CALLCONV framebuffer(void) { return host_fbo; }
static retro_proc_address_t RETRO_CALLCONV resolve(const char *name) {
    return (retro_proc_address_t)eglGetProcAddress(name);
}
static GLuint shader(GLenum type,const char *source,Report report) {
    GLuint s=glCreateShader(type); GLint ok=0; char log[512];
    if(!s) return 0;
    glShaderSource(s,1,&source,0); glCompileShader(s);
    glGetShaderiv(s,0x8B81,&ok);
    if(!ok) { glGetShaderInfoLog(s,512,0,log); log[511]=0; report(log); glDeleteShader(s);return 0; }
    return s;
}
/* Core-side request; the real emulator supplies its own equivalent request. */
static void RETRO_CALLCONV reset(void) {}
static int synthetic_retro_run(const struct retro_hw_render_callback *hw,Report report) {
    static const char vs[]="#version 300 es\nprecision highp float; uniform int tick; out vec3 color; void main(){vec2 p[3]=vec2[3](vec2(-0.65,-0.5),vec2(0.65,-0.5),vec2(-0.25,0.65));vec3 c[3]=vec3[3](vec3(1,0.2,0.1),vec3(0.1,1,0.2),vec3(0.2,0.4,1));vec2 v=p[gl_VertexID];v.x+=0.15*sin(float(tick)*0.05235988);gl_Position=vec4(v,0,1);color=c[gl_VertexID];}";
    static const char fs[]="#version 300 es\nprecision mediump float; in vec3 color; out vec4 pixel; void main(){pixel=vec4(color,1);}";
    GLuint v=shader(0x8B31,vs,report),f=shader(0x8B30,fs,report),p=0,vao=0;
    GLint ok=0; int result=20; char log[512];
    if(!v || !f) goto cleanup;
    p=glCreateProgram(); if(!p) goto cleanup;
    glAttachShader(p,v); glAttachShader(p,f); glLinkProgram(p);glGetProgramiv(p,0x8B82,&ok);
    if(!ok) {glGetProgramInfoLog(p,512,0,log);log[511]=0;report(log);goto cleanup;}
    glGenVertexArrays(1,&vao);glBindVertexArray(vao);glUseProgram(p);
    glUniform1i(glGetUniformLocation(p,"tick"),(GLint)(frame++%120));
    /* This is the core using the frontend callback, never assuming framebuffer 1. */
    glBindFramebuffer(0x8D40,(GLuint)hw->get_current_framebuffer());
    glDrawArrays(0x0004,0,3); result=0;
cleanup:
    glUseProgram(0);glBindVertexArray(0);
    if(vao) glDeleteVertexArrays(1,&vao);
    if(p) glDeleteProgram(p);
    if(v) glDeleteShader(v);
    if(f) glDeleteShader(f);
    return result;
}
__attribute__((visibility("default")))
int tizenpsp_glview_clear(int diagnose,Report report,unsigned int *error,int *fbo) {
#ifdef TIZENPSP_CPP_STARTUP_TEST
    if(tizenpsp_cpp_probe()!=0x505350) {
        if(error)*error=0;
        if(fbo)*fbo=-1;
        if(report)report("CPP_STARTUP_FAIL: constructor absent or executed more than once");
        return 30;
    }
    if(diagnose)report("CPP_STARTUP_OK: C++ constructor executed exactly once before shader rendering");
#endif
    int stage=guarded_clear(diagnose,report,error,fbo);
    if(stage) return stage;
    GLint major=0,program=0,vao=0,read_fbo=0;GLboolean mask[4];
    glGetIntegerv(0x821B,&major);if(major<3) return 21;
    /* Capture GLView's FBO before entering the core. */
    glGetIntegerv(0x8CA6,fbo);host_fbo=(GLuint)*fbo;
    struct retro_hw_render_callback hw={0};
    hw.context_type=RETRO_HW_CONTEXT_OPENGLES3;hw.context_reset=reset;
    hw.bottom_left_origin=true;hw.get_current_framebuffer=framebuffer;hw.get_proc_address=resolve;
    if(!hw.get_proc_address("glCreateShader")) return 22;
    glGetIntegerv(0x8B8D,&program);glGetIntegerv(0x85B5,&vao);glGetIntegerv(0x8CAA,&read_fbo);
    glGetBooleanv(0x0C23,mask);
    const GLenum caps[]={0x0BE2,0x0B71,0x0B90,0x0B44,0x0C11,0x8C89};
    GLboolean enabled[6];
    for(int i=0;i<6;i++){enabled[i]=glIsEnabled(caps[i]);glDisable(caps[i]);}
    glColorMask(1,1,1,1);hw.context_reset();
    stage=synthetic_retro_run(&hw,report);
    *error=glGetError();
    glUseProgram((GLuint)program);glBindVertexArray((GLuint)vao);
    glBindFramebuffer(0x8CA9,host_fbo);glBindFramebuffer(0x8CA8,(GLuint)read_fbo);
    glColorMask(mask[0],mask[1],mask[2],mask[3]);
    for(int i=0;i<6;i++) if(enabled[i]) glEnable(caps[i]);
    GLenum restore=glGetError();if(!*error)*error=restore;
    if(diagnose && !stage && !*error) report("HW_BRIDGE_SHADER_OK synthetic Libretro GLES3; animated RGB triangle; dynamic FBO; readbacks=0; PPSSPP not included");
    return stage?stage:(*error?23:0);
}
