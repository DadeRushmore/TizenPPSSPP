/* Public scalar ABI subset only; no native Evas structures. GPL-2.0.
 * EFL signatures: upstream v1.20.7 (docs/glview-abi-evidence.json).
 * GLES/EGL types and tokens are the standard Khronos C ABI.
 */
#ifndef TIZENPSP_BRIDGE_PLATFORM_H
#define TIZENPSP_BRIDGE_PLATFORM_H
typedef void Evas_Object;
typedef void *EGLContext;
typedef unsigned int GLenum;
typedef unsigned int GLuint;
typedef int GLint;
typedef int GLsizei;
typedef unsigned char Eina_Bool;
typedef void (*Elm_GLView_Func_Cb)(Evas_Object *);
typedef void (*EGLProc)(void);
#define GL_FRAMEBUFFER 0x8D40
#define GL_FRAMEBUFFER_BINDING 0x8CA6
#define GL_DRAW_FRAMEBUFFER 0x8CA9
#define GL_READ_FRAMEBUFFER 0x8CA8
#define GL_READ_FRAMEBUFFER_BINDING 0x8CAA
#define GL_FRAMEBUFFER_COMPLETE 0x8CD5
#define GL_VIEWPORT 0x0BA2
#define GL_MAJOR_VERSION 0x821B
#define GL_MINOR_VERSION 0x821C
#define GL_DEPTH_BITS 0x0D56
#define GL_STENCIL_BITS 0x0D57
#define ELM_GLVIEW_DEPTH 4
#define ELM_GLVIEW_STENCIL 8
#define ELM_GLVIEW_RESIZE_POLICY_SCALE 2
#define EVAS_GL_GLES_3_X 3
#ifdef __cplusplus
extern "C" {
#endif
extern EGLContext eglGetCurrentContext(void);
extern EGLProc eglGetProcAddress(const char *);
extern void glGetIntegerv(GLenum,GLint *);
extern void glBindFramebuffer(GLenum,GLuint);
extern void glViewport(GLint,GLint,GLsizei,GLsizei);
extern GLenum glCheckFramebufferStatus(GLenum);
extern GLenum glGetError(void);
extern void *dlopen(const char *,int);
extern void *dlsym(void *,const char *);
extern int dlclose(void *);
extern Evas_Object *elm_glview_version_add(Evas_Object *,int);
extern Eina_Bool elm_glview_mode_set(Evas_Object *,int);
extern Eina_Bool elm_glview_resize_policy_set(Evas_Object *,int);
extern void elm_glview_size_set(Evas_Object *,int,int);
extern void elm_glview_init_func_set(Evas_Object *,Elm_GLView_Func_Cb);
extern void elm_glview_render_func_set(Evas_Object *,Elm_GLView_Func_Cb);
extern void elm_glview_del_func_set(Evas_Object *,Elm_GLView_Func_Cb);
extern void elm_glview_changed_set(Evas_Object *);
extern void evas_object_del(Evas_Object *);
#ifdef __cplusplus
}
#endif
#endif
