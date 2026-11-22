/* TizenPSP milestone 2 diagnostics, GPL-2.0. Minimal EGL 1.4 / GLES 2 ABI subset.
 * No Evas_GL_API layout, emulation, JIT, constructors, TLS or libc dependency.
 * EGL/GLES symbols are resolved by the existing ARM32 manual ELF loader.
 */
typedef void *EGLDisplay;
typedef void *EGLConfig;
typedef void *EGLSurface;
typedef void *EGLContext;
typedef unsigned int EGLBoolean;
typedef int EGLint;
typedef unsigned int GLenum;
typedef unsigned int GLuint;
typedef int GLint;
typedef int GLsizei;
typedef unsigned char GLboolean;
typedef float GLfloat;
extern EGLDisplay eglGetDisplay(void *);
extern EGLBoolean eglInitialize(EGLDisplay,EGLint *,EGLint *);
extern EGLBoolean eglChooseConfig(EGLDisplay,const EGLint *,EGLConfig *,EGLint,EGLint *);
extern EGLBoolean eglBindAPI(unsigned int);
extern unsigned int eglQueryAPI(void);
extern EGLSurface eglCreatePbufferSurface(EGLDisplay,EGLConfig,const EGLint *);
extern EGLContext eglCreateContext(EGLDisplay,EGLConfig,EGLContext,const EGLint *);
extern EGLBoolean eglMakeCurrent(EGLDisplay,EGLSurface,EGLSurface,EGLContext);
extern EGLDisplay eglGetCurrentDisplay(void);
extern EGLContext eglGetCurrentContext(void);
extern EGLSurface eglGetCurrentSurface(EGLint);
extern EGLBoolean eglDestroyContext(EGLDisplay,EGLContext);
extern EGLBoolean eglDestroySurface(EGLDisplay,EGLSurface);
extern EGLint eglGetError(void);
extern const char *glGetString(GLenum);
extern GLuint glCreateShader(GLenum);
extern void glShaderSource(GLuint,GLsizei,const char *const *,const GLint *);
extern void glCompileShader(GLuint);
extern void glGetShaderiv(GLuint,GLenum,GLint *);
extern void glGetShaderInfoLog(GLuint,GLsizei,GLsizei *,char *);
extern void glDeleteShader(GLuint);
extern GLuint glCreateProgram(void);
extern void glAttachShader(GLuint,GLuint);
extern void glBindAttribLocation(GLuint,GLuint,const char *);
extern void glLinkProgram(GLuint);
extern void glGetProgramiv(GLuint,GLenum,GLint *);
extern void glGetProgramInfoLog(GLuint,GLsizei,GLsizei *,char *);
extern void glUseProgram(GLuint);
extern void glDeleteProgram(GLuint);
extern void glViewport(GLint,GLint,GLsizei,GLsizei);
extern void glClearColor(GLfloat,GLfloat,GLfloat,GLfloat);
extern void glClear(GLenum);
extern void glVertexAttribPointer(GLuint,GLint,GLenum,GLboolean,GLsizei,const void *);
extern void glEnableVertexAttribArray(GLuint);
extern void glDrawArrays(GLenum,GLint,GLsizei);
extern void glReadPixels(GLint,GLint,GLsizei,GLsizei,GLenum,GLenum,void *);
extern GLenum glGetError(void);
typedef void (*Report)(const char *);
/* Optional core EGL query resolved separately; absent export never gets called. */
typedef const char *(*QueryString)(EGLDisplay,EGLint);
static void hex_report(Report report,const char *name,unsigned int value) {
    char text[11]; const char digits[]="0123456789ABCDEF";
    text[0]='0'; text[1]='x'; text[10]=0;
    for(int i=0;i<8;i++) text[2+i]=digits[(value>>(28-i*4))&15];
    report(name); report(text);
}
static void gl_string(Report report,const char *name,GLenum token) {
    const char *text=glGetString(token); GLenum error=glGetError();
    report(name); report(text?text:"(unavailable)");
    if(error) { report("DIAGNOSTIC_ERROR"); hex_report(report,"GL_QUERY_ERROR",error); }
}
static void egl_string(Report report,QueryString query,EGLDisplay d,const char *name,EGLint token) {
    if(!query) { report(name); report("(eglQueryString export unavailable)"); return; }
    const char *text=query(d,token); EGLint error=eglGetError();
    report(name); report(text?text:"(unavailable)");
    if(error!=0x3000) { report("DIAGNOSTIC_ERROR"); hex_report(report,"EGL_QUERY_ERROR",error); }
}
static GLuint shader(GLenum type,const char *source,Report report) {
    GLuint s=glCreateShader(type); GLint ok=0; char log[512];
    if (!s) return 0;
    glShaderSource(s,1,&source,0); glCompileShader(s);
    glGetShaderiv(s,0x8B81,&ok);
    if (!ok) { glGetShaderInfoLog(s,sizeof(log),0,log); log[511]=0; report(log); glDeleteShader(s); return 0; }
    return s;
}
/* Returns 0 only after readback and restoration of the host's current context.
 * out: width*height ARGB8888 words, top row first (Evas native pixel layout).
 * error: EGL or GLES error when available; stage is encoded in return value.
 */
__attribute__((visibility("default")))
int tizenpsp_triangle_diagnostics(unsigned int *out,int width,int height,Report report,unsigned int *error,QueryString query) {
    if (!error || !report) return 10;
    *error=0;
    if (!out || width<1 || height<1 || width>1920 || height>1080) return 10;
    EGLDisplay d=0,oldD=eglGetCurrentDisplay();
    EGLContext c=0,oldC=eglGetCurrentContext();
    EGLSurface s=0,oldDraw=eglGetCurrentSurface(0x3059),oldRead=eglGetCurrentSurface(0x305A);
    unsigned int oldAPI=eglQueryAPI();
    EGLConfig config=0; EGLint count=0,major=0,minor=0;
    GLuint vs=0,fs=0,p=0; GLint linked=0; int result=1,current=0;
    const EGLint attrs[]={0x3033,1,0x3040,4,0x3024,8,0x3023,8,0x3022,8,0x3021,8,0x3038};
    const EGLint surfaceAttrs[]={0x3057,width,0x3056,height,0x3038};
    const EGLint contextAttrs[]={0x3098,2,0x3038};
    const GLfloat vertices[]={0.0f,0.75f, -0.75f,-0.65f, 0.75f,-0.65f};
    const char *vsrc="attribute vec2 position; void main(){gl_Position=vec4(position,0.0,1.0);}";
    const char *fsrc="precision mediump float; void main(){gl_FragColor=vec4(0.15,0.85,1.0,1.0);}";
    report(oldC?"HOST_CONTEXT=present":"HOST_CONTEXT=none");
    /* Report/consume stale host error once; keep it separate from our errors. */
    hex_report(report,"EGL_ERROR_AT_ENTRY",(unsigned int)eglGetError());
    d=eglGetDisplay(0); if (!d || !eglInitialize(d,&major,&minor)) goto done;
    egl_string(report,query,d,"EGL_VENDOR",0x3053);
    egl_string(report,query,d,"EGL_VERSION",0x3054);
    egl_string(report,query,d,"EGL_EXTENSIONS",0x3055);
    hex_report(report,"EGL_INITIALIZED_MAJOR",(unsigned int)major);
    hex_report(report,"EGL_INITIALIZED_MINOR",(unsigned int)minor);
    result=2; if (!eglBindAPI(0x30A0)) goto done;
    if (!eglChooseConfig(d,attrs,&config,1,&count) || count!=1) { report("CONFIG_SELECTION_FAILED"); goto done; }
    result=3; s=eglCreatePbufferSurface(d,config,surfaceAttrs); if (!s) goto done;
    c=eglCreateContext(d,config,0,contextAttrs);
    if (!c) { report("CONTEXT_CREATE_FAILED"); goto done; }
    report("CONTEXT_CREATED");
    result=4; if (!eglMakeCurrent(d,s,s,c)) goto done; current=1;
    gl_string(report,"GL_VENDOR",0x1F00);
    gl_string(report,"GL_RENDERER",0x1F01);
    gl_string(report,"GL_VERSION",0x1F02);
    gl_string(report,"GL_SHADING_LANGUAGE_VERSION",0x8B8C);
    /* Full extension list includes FBO, EGL-image and texture capabilities.
     * Reporting extension names never implies usable EvasGL interoperation. */
    gl_string(report,"GL_EXTENSIONS",0x1F03);
    result=5; vs=shader(0x8B31,vsrc,report); fs=shader(0x8B30,fsrc,report); if (!vs || !fs) goto done;
    p=glCreateProgram(); if (!p) goto done;
    glAttachShader(p,vs); glAttachShader(p,fs); glBindAttribLocation(p,0,"position"); glLinkProgram(p);
    glGetProgramiv(p,0x8B82,&linked);
    if (!linked) { char log[512]; glGetProgramInfoLog(p,sizeof(log),0,log); log[511]=0; report(log); goto done; }
    glUseProgram(p); glViewport(0,0,width,height);
    glClearColor(0.03f,0.04f,0.09f,1.0f); glClear(0x4000);
    glVertexAttribPointer(0,2,0x1406,0,0,vertices); glEnableVertexAttribArray(0); glDrawArrays(4,0,3);
    glReadPixels(0,0,width,height,0x1908,0x1401,out);
    result=6; *error=glGetError(); if (*error) goto done;
    report("FRAME_RENDERED");
    /* GLES RGBA bytes -> opaque Evas ARGB words, and bottom-up -> top-down. */
    for (int y=0;y<(height+1)/2;y++) for (int x=0;x<width;x++) {
        int a=y*width+x,b=(height-1-y)*width+x; unsigned int u=out[a],v=out[b];
        out[a]=0xff000000u|((v&255)<<16)|(v&0xff00)|((v>>16)&255);
        out[b]=0xff000000u|((u&255)<<16)|(u&0xff00)|((u>>16)&255);
    }
    result=0;
done:
    if (result && !*error) *error=current?glGetError():(unsigned int)eglGetError();
    hex_report(report,"PRIMARY_RESULT_STAGE",(unsigned int)result);
    hex_report(report,current?"PRIMARY_GL_ERROR":"PRIMARY_EGL_ERROR",*error);
    if (current) {
        if(p) glDeleteProgram(p); if(vs) glDeleteShader(vs); if(fs) glDeleteShader(fs);
        /* Unbind our context before restoring the original client API/context. */
        if (!eglMakeCurrent(d,0,0,0)) {
            unsigned int e=eglGetError(); hex_report(report,"UNBIND_ERROR",e);
            if(!result) { result=7; *error=e; }
        }
    }
    if (!eglBindAPI(oldAPI)) {
        unsigned int e=eglGetError(); hex_report(report,"RESTORE_API_ERROR",e);
        if(!result) { result=7; *error=e; }
    }
    if (oldC && !eglMakeCurrent(oldD,oldDraw,oldRead,oldC)) {
        unsigned int e=eglGetError(); hex_report(report,"RESTORE_CONTEXT_ERROR",e);
        if(!result) { result=7; *error=e; }
    }
    if(c && !eglDestroyContext(d,c)) {
        unsigned int e=eglGetError(); hex_report(report,"DESTROY_CONTEXT_ERROR",e);
        if(!result) { result=8; *error=e; }
    }
    if(s && !eglDestroySurface(d,s)) {
        unsigned int e=eglGetError(); hex_report(report,"DESTROY_SURFACE_ERROR",e);
        if(!result) { result=8; *error=e; }
    }
    hex_report(report,"FINAL_RESULT_STAGE",(unsigned int)result);
    /* Do not eglTerminate: the default display may also belong to ElmSharp. */
    return result;
}
