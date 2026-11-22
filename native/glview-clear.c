/* GPL-2.0. GLView callback experiment: clear its already-current FBO.
 * No Evas structs, Evas_GL_API offsets, EGL context creation, readback or swap.
 * Mode must be ELM_GLVIEW_NONE (no ELM_GLVIEW_DIRECT) in the owner.
 */
typedef unsigned int GLenum;
typedef unsigned char GLboolean;
typedef int GLint;
typedef float GLfloat;
extern void *eglGetCurrentContext(void);
extern const char *glGetString(GLenum);
extern GLenum glGetError(void);
extern void glGetIntegerv(GLenum,GLint *);
extern void glGetFloatv(GLenum,GLfloat *);
extern void glGetBooleanv(GLenum,GLboolean *);
extern GLboolean glIsEnabled(GLenum);
extern void glEnable(GLenum);
extern void glDisable(GLenum);
extern void glColorMask(GLboolean,GLboolean,GLboolean,GLboolean);
extern void glClearColor(GLfloat,GLfloat,GLfloat,GLfloat);
extern void glClear(GLenum);
extern GLenum glCheckFramebufferStatus(GLenum);
typedef void (*Report)(const char *);

__attribute__((visibility("default")))
int tizenpsp_glview_clear(int diagnose,Report report,unsigned int *error,int *framebuffer) {
    if(!error || !framebuffer || !report) return 10;
    *error=0; *framebuffer=-1;
    if(!eglGetCurrentContext()) return 1;
    *error=glGetError(); if(*error) return 2;
    glGetIntegerv(0x8CA6,framebuffer); /* GL_FRAMEBUFFER_BINDING */
    *error=glGetError(); if(*error) return 3;
    /* Do not touch a default framebuffer or assume a native window layout. */
    if(*framebuffer<=0) return 4;
    GLenum complete=glCheckFramebufferStatus(0x8D40);
    *error=glGetError(); if(*error || complete!=0x8CD5) return 5;
    if(diagnose) {
        const char *s;
        report("GLVIEW_GL_VENDOR"); s=glGetString(0x1F00); report(s?s:"(null)");
        report("GLVIEW_GL_RENDERER"); s=glGetString(0x1F01); report(s?s:"(null)");
        report("GLVIEW_GL_VERSION"); s=glGetString(0x1F02); report(s?s:"(null)");
        *error=glGetError(); if(*error) return 6;
    }
    GLfloat oldColor[4]; GLboolean oldMask[4];
    GLboolean scissor=glIsEnabled(0x0C11);
    glGetFloatv(0x0C22,oldColor); glGetBooleanv(0x0C23,oldMask);
    *error=glGetError(); if(*error) return 7;
    if(scissor) glDisable(0x0C11);
    glColorMask(1,1,1,1);
    glClearColor(0.08f,0.65f,0.28f,1.0f);
    glClear(0x4000);
    *error=glGetError();
    glClearColor(oldColor[0],oldColor[1],oldColor[2],oldColor[3]);
    glColorMask(oldMask[0],oldMask[1],oldMask[2],oldMask[3]);
    if(scissor) glEnable(0x0C11);
    GLenum restoreError=glGetError();
    if(*error) return 8;
    *error=restoreError;
    return restoreError?9:0;
}
