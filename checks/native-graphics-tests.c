/* Host-side failure/lifetime checks. Fake EGL/GLES, not a GPU or TV test. */
#ifdef NDEBUG
#undef NDEBUG
#endif
#include <assert.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include "../native/triangle.c"
#define PTR(n) ((void *)(uintptr_t)(n))
enum { OK, DISPLAY_FAIL, CONTEXT_FAIL, SHADER_FAIL, READ_FAIL, RESTORE_FAIL, DESTROY_FAIL, NULL_STRINGS };
static int failure, frames, contextFailures, queryCalls, destroyedContexts, destroyedSurfaces;
static EGLDisplay currentDisplay;
static EGLContext currentContext;
static EGLSurface currentDraw,currentRead;
static unsigned int currentAPI,eglError,glError;
static void report(const char *text) {
    assert(text);
    if(!strcmp(text,"FRAME_RENDERED")) frames++;
    if(!strcmp(text,"CONTEXT_CREATE_FAILED")) contextFailures++;
}
static void reset(int mode,int host) {
    failure=mode; frames=contextFailures=queryCalls=destroyedContexts=destroyedSurfaces=0;
    currentDisplay=host?PTR(16):0; currentContext=host?PTR(1):0;
    currentDraw=host?PTR(2):0; currentRead=host?PTR(3):0;
    currentAPI=0x30A2; eglError=0x3000; glError=0;
}
EGLDisplay eglGetDisplay(void *unused) { (void)unused; return PTR(32); }
EGLBoolean eglInitialize(EGLDisplay d,EGLint *major,EGLint *minor) {
    (void)d; *major=1; *minor=4;
    if(failure==DISPLAY_FAIL) { eglError=0x3001; return 0; } return 1;
}
EGLBoolean eglChooseConfig(EGLDisplay d,const EGLint *a,EGLConfig *c,EGLint n,EGLint *count) {
    (void)d; (void)a; (void)n; *c=PTR(64); *count=1; return 1;
}
EGLBoolean eglBindAPI(unsigned int api) { currentAPI=api; return 1; }
unsigned int eglQueryAPI(void) { return currentAPI; }
EGLSurface eglCreatePbufferSurface(EGLDisplay d,EGLConfig c,const EGLint *a) {
    (void)d; (void)c; (void)a; return PTR(80);
}
EGLContext eglCreateContext(EGLDisplay d,EGLConfig c,EGLContext shared,const EGLint *a) {
    (void)d; (void)c; (void)shared; (void)a;
    if(failure==CONTEXT_FAIL) { eglError=0x3009; return 0; } return PTR(96);
}
EGLBoolean eglMakeCurrent(EGLDisplay d,EGLSurface draw,EGLSurface read,EGLContext c) {
    if(failure==RESTORE_FAIL && c==PTR(1)) { eglError=0x3006; return 0; }
    currentDisplay=c?d:0; currentContext=c; currentDraw=draw; currentRead=read; return 1;
}
EGLDisplay eglGetCurrentDisplay(void) { return currentDisplay; }
EGLContext eglGetCurrentContext(void) { return currentContext; }
EGLSurface eglGetCurrentSurface(EGLint kind) { return kind==0x3059?currentDraw:currentRead; }
EGLBoolean eglDestroyContext(EGLDisplay d,EGLContext c) {
    (void)d; assert(c==PTR(96)); destroyedContexts++;
    if(failure==DESTROY_FAIL) { eglError=0x3006; return 0; } return 1;
}
EGLBoolean eglDestroySurface(EGLDisplay d,EGLSurface s) {
    (void)d; assert(s==PTR(80)); destroyedSurfaces++; return 1;
}
EGLint eglGetError(void) { unsigned int e=eglError; eglError=0x3000; return (int)e; }
static const char *query(EGLDisplay d,EGLint token) { (void)d; (void)token; queryCalls++; return "fake EGL"; }
const char *glGetString(GLenum token) { (void)token; return failure==NULL_STRINGS?0:"fake GL"; }
GLuint glCreateShader(GLenum type) { return type; }
void glShaderSource(GLuint s,GLsizei n,const char *const *p,const GLint *len) { (void)s;(void)n;(void)p;(void)len; }
void glCompileShader(GLuint s) { (void)s; }
void glGetShaderiv(GLuint s,GLenum name,GLint *value) { (void)s;(void)name;*value=failure!=SHADER_FAIL; }
void glGetShaderInfoLog(GLuint s,GLsizei n,GLsizei *len,char *log) { (void)s;(void)n;(void)len;strcpy(log,"fake compile failure"); }
void glDeleteShader(GLuint s) { (void)s; }
GLuint glCreateProgram(void) { return 10; }
void glAttachShader(GLuint p,GLuint s) { (void)p;(void)s; }
void glBindAttribLocation(GLuint p,GLuint i,const char *name) { (void)p;(void)i;(void)name; }
void glLinkProgram(GLuint p) { (void)p; }
void glGetProgramiv(GLuint p,GLenum name,GLint *value) { (void)p;(void)name;*value=1; }
void glGetProgramInfoLog(GLuint p,GLsizei n,GLsizei *len,char *log) { (void)p;(void)n;(void)len;log[0]=0; }
void glUseProgram(GLuint p) { (void)p; }
void glDeleteProgram(GLuint p) { (void)p; }
void glViewport(GLint x,GLint y,GLsizei w,GLsizei h) { (void)x;(void)y;(void)w;(void)h; }
void glClearColor(GLfloat r,GLfloat g,GLfloat b,GLfloat a) { (void)r;(void)g;(void)b;(void)a; }
void glClear(GLenum bits) { (void)bits; }
void glVertexAttribPointer(GLuint i,GLint n,GLenum type,GLboolean normalized,GLsizei stride,const void *v) {
    (void)i;(void)n;(void)type;(void)normalized;(void)stride;(void)v;
}
void glEnableVertexAttribArray(GLuint i) { (void)i; }
void glDrawArrays(GLenum type,GLint first,GLsizei n) { (void)type;(void)first;(void)n; }
void glReadPixels(GLint x,GLint y,GLsizei w,GLsizei h,GLenum format,GLenum type,void *out) {
    (void)x;(void)y;(void)w;(void)h;(void)format;(void)type;
    unsigned int *p=out; p[0]=0x04030201; p[1]=0x08070605; p[2]=0x0c0b0a09; p[3]=0x100f0e0d;
    if(failure==READ_FAIL) glError=0x0502;
}
GLenum glGetError(void) { GLenum e=glError; glError=0; return e; }
int main(void) {
    unsigned int pixels[4],error;
    reset(OK,1);
    assert(tizenpsp_triangle_diagnostics(pixels,2,2,report,&error,query)==0);
    assert(!error && frames==1 && queryCalls==3 && destroyedContexts==1 && destroyedSurfaces==1);
    assert(currentContext==PTR(1) && currentDisplay==PTR(16) && currentDraw==PTR(2) && currentRead==PTR(3) && currentAPI==0x30A2);
    assert(pixels[0]==0xff090a0b && pixels[1]==0xff0d0e0f && pixels[2]==0xff010203 && pixels[3]==0xff050607);
    reset(OK,0);
    assert(tizenpsp_triangle_diagnostics(pixels,2,2,report,&error,0)==0);
    assert(queryCalls==0 && currentContext==0 && currentAPI==0x30A2);
    reset(CONTEXT_FAIL,1);
    assert(tizenpsp_triangle_diagnostics(pixels,2,2,report,&error,query)==3);
    assert(contextFailures==1 && frames==0 && error==0x3009 && destroyedContexts==0 && destroyedSurfaces==1 && currentContext==PTR(1));
    reset(DISPLAY_FAIL,1);
    assert(tizenpsp_triangle_diagnostics(pixels,2,2,report,&error,query)==1 && error==0x3001);
    reset(SHADER_FAIL,1);
    assert(tizenpsp_triangle_diagnostics(pixels,2,2,report,&error,query)==5 && frames==0 && destroyedContexts==1);
    reset(READ_FAIL,1);
    assert(tizenpsp_triangle_diagnostics(pixels,2,2,report,&error,query)==6 && error==0x502 && frames==0);
    reset(RESTORE_FAIL,1);
    assert(tizenpsp_triangle_diagnostics(pixels,2,2,report,&error,query)==7 && error==0x3006);
    reset(DESTROY_FAIL,1);
    assert(tizenpsp_triangle_diagnostics(pixels,2,2,report,&error,query)==8 && error==0x3006);
    reset(NULL_STRINGS,1);
    assert(tizenpsp_triangle_diagnostics(pixels,2,2,report,&error,query)==0);
    assert(tizenpsp_triangle_diagnostics(pixels,2,2,report,0,query)==10);
    puts("PASS: mocked native success, pixel conversion, optional query absence, null strings, context restoration, initialization/shader/readback/cleanup failures.");
    return 0;
}
