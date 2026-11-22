/* Mocked current-context/FBO tests, not an EFL or TV simulation. */
#ifdef NDEBUG
#undef NDEBUG
#endif
#include <assert.h>
#include <stdio.h>
#include <string.h>
#include "../native/glview-clear.c"
static int hasContext,fbo,complete,clears,failClear;
static unsigned int error;
static GLboolean scissor,mask[4];
static GLfloat color[4];
static void reset(void) {
    hasContext=1; fbo=7; complete=1; clears=failClear=0; error=0;
    scissor=1;
    mask[0]=mask[2]=1; mask[1]=mask[3]=0;
    color[0]=0.25f; color[1]=0.5f; color[2]=0.75f; color[3]=0.0f;
}
void *eglGetCurrentContext(void) { return hasContext?(void *)1:0; }
const char *glGetString(GLenum name) { (void)name; return "mock"; }
GLenum glGetError(void) { unsigned int e=error; error=0; return e; }
void glGetIntegerv(GLenum name,GLint *p) { assert(name==0x8CA6); *p=fbo; }
void glGetFloatv(GLenum name,GLfloat *p) { assert(name==0x0C22); memcpy(p,color,sizeof(color)); }
void glGetBooleanv(GLenum name,GLboolean *p) { assert(name==0x0C23); memcpy(p,mask,sizeof(mask)); }
GLboolean glIsEnabled(GLenum name) { assert(name==0x0C11); return scissor; }
void glEnable(GLenum name) { assert(name==0x0C11); scissor=1; }
void glDisable(GLenum name) { assert(name==0x0C11); scissor=0; }
void glColorMask(GLboolean r,GLboolean g,GLboolean b,GLboolean a) { mask[0]=r; mask[1]=g; mask[2]=b; mask[3]=a; }
void glClearColor(GLfloat r,GLfloat g,GLfloat b,GLfloat a) { color[0]=r; color[1]=g; color[2]=b; color[3]=a; }
void glClear(GLenum bits) {
    assert(bits==0x4000 && fbo==7 && !scissor && mask[0] && mask[1] && mask[2] && mask[3]);
    assert(color[1]==0.65f); clears++; if(failClear) error=0x502;
}
GLenum glCheckFramebufferStatus(GLenum target) { assert(target==0x8D40); return complete?0x8CD5:0x8CD6; }
static void report(const char *text) { assert(text); }
static void state_restored(void) {
    assert(scissor && mask[0] && !mask[1] && mask[2] && !mask[3]);
    assert(color[0]==0.25f && color[1]==0.5f && color[2]==0.75f && color[3]==0.0f);
}
int main(void) {
    unsigned int e; int framebuffer;
    reset(); assert(tizenpsp_glview_clear(1,report,&e,&framebuffer)==0 && !e && framebuffer==7 && clears==1); state_restored();
    reset(); hasContext=0; assert(tizenpsp_glview_clear(0,report,&e,&framebuffer)==1 && !clears);
    reset(); error=0x501; assert(tizenpsp_glview_clear(0,report,&e,&framebuffer)==2 && e==0x501 && !clears);
    reset(); fbo=0; assert(tizenpsp_glview_clear(0,report,&e,&framebuffer)==4 && !clears);
    reset(); complete=0; assert(tizenpsp_glview_clear(0,report,&e,&framebuffer)==5 && !clears);
    reset(); failClear=1; assert(tizenpsp_glview_clear(0,report,&e,&framebuffer)==8 && e==0x502); state_restored();
    puts("PASS: native GLView clear rejects missing context/default/incomplete FBO, preserves state on success/failure; no readback.");
    return 0;
}
