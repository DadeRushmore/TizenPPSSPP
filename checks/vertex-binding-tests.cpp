#undef NDEBUG
#include <cassert>
#include <cstdint>
#include <cstdio>
#include <vector>
#include <array>
#include <random>
#include "../native/core-build/runtime/tizenpsp_vertex_binding.h"
#include "../native/core-build/runtime/tizenpsp_jit_diagnostics.h"
#include "../native/core-build/runtime/tizenpsp_experiments.h"
#if !TIZENPSP_DETAILED_PROFILE
static unsigned testRequested=1;
void TizenVertexBindingCapability(bool) {}
extern "C" unsigned tizenpsp_vertex_binding_status(){return testRequested;}
extern "C" unsigned tizenpsp_set_vertex_binding(unsigned value){testRequested=value;return value;}
#endif
using GLuint=unsigned;using GLenum=unsigned;using GLint=int;
constexpr unsigned GL_ARRAY_BUFFER=1,GL_ELEMENT_ARRAY_BUFFER=2;
constexpr unsigned GL_FLOAT=0x1406,GL_UNSIGNED_BYTE=0x1401,GL_SHORT=0x1402;
#define _dbg_assert_(x) assert(x)
#define CHECK_GL_ERROR_IF_DEBUG() ((void)0)
struct Extensions {
    bool IsGLES=true,GLES3=true,ARB_vertex_array_object=false;int version=31;
    bool VersionGEThan(int major,int minor)const{return version>=major*10+minor;}
} gl_extensions;
struct GLRBuffer {unsigned buffer_;bool Mapped()const{return false;}};
struct GLRInputLayout {
    struct Entry {int location,count;unsigned type;unsigned char normalized;intptr_t offset;int stream;};
    std::vector<Entry> entries;int stride,stride1=0,semanticsMask_=0;
};
struct GLRRenderData {struct Draw {
    GLRInputLayout *inputLayout;
    GLRBuffer *vertexBuffer,*vertexBuffer1=nullptr,*indexBuffer=nullptr;
    uint32_t vertexOffset=0,vertexOffset1=0,indexOffset=0;
    int instances=1,maxIndex=12,count=12,first=0;
    unsigned mode=4,indexType=GL_SHORT;
} draw;};
struct State {
    struct Attribute {int count=0;unsigned type=0;unsigned char normalized=0;unsigned relative=0,binding=0;};
    struct Binding {unsigned buffer=0;intptr_t offset=0;int stride=0;};
    std::array<Attribute,16> attrs{};std::array<Binding,16> bindings{};
    unsigned array=0,element=0;int enabled=0;
    unsigned long long pointerCalls=0,formatCalls=0,bindingCalls=0,arrayCalls=0;
    std::vector<uint64_t> observed;
};
static State *active;
static int Width(unsigned type){return type==GL_FLOAT?4:type==GL_SHORT?2:1;}
static void glBindBuffer(unsigned target,unsigned buffer){if(target==GL_ARRAY_BUFFER){active->array=buffer;active->arrayCalls++;}else active->element=buffer;}
static void glVertexAttribPointer(unsigned a,int count,unsigned type,unsigned char norm,int stride,const void *ptr){
    active->attrs[a]={count,type,norm,0,a};active->bindings[a]={active->array,(intptr_t)ptr,stride?stride:count*Width(type)};active->pointerCalls++;
}
static void Format(unsigned a,int count,unsigned type,unsigned char norm,unsigned offset){auto &v=active->attrs[a];v.count=count;v.type=type;v.normalized=norm;v.relative=offset;active->formatCalls++;}
static void AttributeBinding(unsigned a,unsigned b){active->attrs[a].binding=b;active->bindingCalls++;}
static void BufferBinding(unsigned b,unsigned buffer,intptr_t offset,int stride){active->bindings[b]={buffer,offset,stride};active->bindingCalls++;}
static TizenVertexBindingAPI tizenBindingAPI{Format,AttributeBinding,BufferBinding};
static bool tizenSeparateVertexBinding=true;
static void EnableDisableVertexArrays(int,int mask){active->enabled=mask;}
static void Observe(unsigned mode,int first,int count,unsigned type,intptr_t indexOffset,int instances,int kind,int maxIndex){
    auto &o=active->observed;o.clear();o={mode,(unsigned)first,(unsigned)count,type,(uint64_t)indexOffset,(unsigned)instances,(unsigned)kind,(unsigned)maxIndex,active->element,(unsigned)active->enabled};
    for(int a=0;a<16;a++)if(active->enabled&(1<<a)) {
        auto &v=active->attrs[a];auto &b=active->bindings[v.binding];
        o.push_back(a);o.push_back(v.count);o.push_back(v.type);o.push_back(v.normalized);o.push_back(b.buffer);
        // Compare actual fetch addresses, not implementation-specific binding indices.
        for(int vertex:{0,1,7,123})o.push_back(b.offset+v.relative+(intptr_t)vertex*b.stride);
    }
}
static void glDrawRangeElements(unsigned mode,unsigned,int maxIndex,int count,unsigned type,const void *offset){Observe(mode,0,count,type,(intptr_t)offset,1,0,maxIndex);}
static void glDrawElements(unsigned mode,int count,unsigned type,const void *offset){Observe(mode,0,count,type,(intptr_t)offset,1,1,-1);}
static void glDrawElementsInstanced(unsigned mode,int count,unsigned type,const void *offset,int instances){Observe(mode,0,count,type,(intptr_t)offset,instances,2,-1);}
static void glDrawArrays(unsigned mode,int first,int count){Observe(mode,first,count,0,0,1,3,-1);}
struct Pass {int attrMask=0;GLuint curArrayBuffer=0,curElemArrayBuffer=0;TizenVertexBindingState vertexBindings;};
constexpr unsigned GL_VERTEX_ARRAY_BINDING=0x85B5,GL_FALSE=0,GL_NO_ERROR=0;
static State frontVAO,coreVAO;
static unsigned boundVAO=42,probeError=0,generatedVAOs=0,deletedVAOs=0;
static bool failAllocation=false;
static void glGenVertexArrays(int count,unsigned *vao){assert(count==1);generatedVAOs++;*vao=failAllocation?0:77;}
static void glDeleteVertexArrays(int count,const unsigned *vao){assert(count==1 && *vao==77);deletedVAOs++;}
static void glGetIntegerv(unsigned name,int *value){assert(name==GL_VERTEX_ARRAY_BINDING);*value=boundVAO;}
static void glBindVertexArray(unsigned vao){assert(vao==0 || vao==42 || vao==77);boundVAO=vao;active=vao==42?&frontVAO:&coreVAO;}
static unsigned glGetError(){unsigned e=probeError;probeError=0;return e;}
#include "../build/vertex-binding-draw-methods.h"
static void Mask(GLRInputLayout &l){for(auto &e:l.entries)l.semanticsMask_|=1<<e.location;}
int main(){
    // Exercise the actual initialization probe: unsupported APIs/context,
    // driver error and success all preserve the frontend's VAO and its state.
    for(int own=0;own<2;own++)for(int gles=0;gles<2;gles++)for(int es31=0;es31<2;es31++)for(int missing=0;missing<4;missing++)for(int error=0;error<2;error++) {
        frontVAO=State{};coreVAO=State{};frontVAO.attrs[0].count=2;frontVAO.bindings[0]={999,100,8};
        glBindVertexArray(42);probeError=error?0x0502:0;
        gl_extensions.IsGLES=gles;gl_extensions.version=es31?31:30;
        tizenBindingAPI={Format,AttributeBinding,BufferBinding};
        if(missing==1)tizenBindingAPI.format=nullptr;
        if(missing==2)tizenBindingAPI.attributeBinding=nullptr;
        if(missing==3)tizenBindingAPI.bufferBinding=nullptr;
        ProbeBinding(own?77:0);
        assert(tizenSeparateVertexBinding==bool(own && gles && es31 && !missing && !error));
        assert(boundVAO==42 && frontVAO.attrs[0].count==2 && frontVAO.bindings[0].buffer==999 && frontVAO.bindings[0].offset==100 && frontVAO.bindings[0].stride==8);
        assert(frontVAO.formatCalls==0 && frontVAO.bindingCalls==0);
    }
    // Actual production allocation/bind/unbind/delete blocks, including GLES
    // without ARB_vertex_array_object: the integration case missing in 0.24.1.
    unsigned lifecycleCases=0;
    for(int arb=0;arb<2;arb++)for(int gles=0;gles<2;gles++)for(int version:{20,30,31,32})for(int missing=0;missing<4;missing++)for(int fail=0;fail<2;fail++) {
        gl_extensions.ARB_vertex_array_object=arb;gl_extensions.IsGLES=gles;gl_extensions.version=version;
        tizenBindingAPI={Format,AttributeBinding,BufferBinding};
        if(missing==1)tizenBindingAPI.format=nullptr;
        if(missing==2)tizenBindingAPI.attributeBinding=nullptr;
        if(missing==3)tizenBindingAPI.bufferBinding=nullptr;
        generatedVAOs=deletedVAOs=0;failAllocation=fail;GLuint vao=0;
        frontVAO=State{};coreVAO=State{};glBindVertexArray(42);probeError=0;
        CreateBindingVAO(vao);
        bool allocate=arb || (gles && version>=31 && !missing);
        assert(generatedVAOs==unsigned(allocate) && vao==(allocate && !fail?77u:0u));
        ProbeBinding(vao);assert(boundVAO==42);
        assert(tizenSeparateVertexBinding==bool(vao && gles && version>=31 && !missing));
        BindBindingVAO(vao);assert(boundVAO==(vao?77u:42u));
        UnbindBindingVAO(vao,false);assert(boundVAO==(vao?77u:42u));
        UnbindBindingVAO(vao,true);assert(boundVAO==(vao?0u:42u));
        DeleteBindingVAO(vao);assert(vao==0 && deletedVAOs==unsigned(allocate && !fail));
        DeleteBindingVAO(vao);assert(deletedVAOs==unsigned(allocate && !fail));
        lifecycleCases++;
    }
    printf("PASS: %u production VAO lifecycle cases: GLES 3.1/3.2 without ARB extension, unsupported/missing APIs, allocation failure and repeated deletion.\n",lifecycleCases);
    gl_extensions.IsGLES=true;gl_extensions.version=31;
    GLRInputLayout base{{{0,3,GL_FLOAT,0,0,0},{1,2,GL_FLOAT,0,12,0},{2,4,GL_UNSIGNED_BYTE,1,20,0},{3,3,GL_FLOAT,0,24,0}},36};
    GLRInputLayout split=base;split.entries.push_back({4,4,GL_UNSIGNED_BYTE,1,0,1});split.stride1=8;
    GLRInputLayout packed{{{0,3,GL_SHORT,1,0,0},{2,4,GL_UNSIGNED_BYTE,1,6,0}},12};
    GLRInputLayout zero=packed;zero.stride=0;
    GLRInputLayout large=base;large.entries[1].offset=2048;
    std::array<GLRInputLayout*,5> layouts{&base,&split,&packed,&zero,&large};for(auto l:layouts)Mask(*l);
    std::array<GLRBuffer,4> buffers{{{10},{11},{12},{13}}};
    State before,after;Pass p,q;
    std::mt19937 rng(240);
    for(int i=0;i<50000;i++) {
        // Frequent sequential same-layout draws, mixed with fallbacks and pass resets.
        auto l=layouts[(i/97)%layouts.size()];GLRRenderData c{};
        c.draw.inputLayout=l;c.draw.vertexBuffer=&buffers[rng()%4];c.draw.vertexBuffer1=l->stride1?&buffers[3]:nullptr;
        c.draw.vertexOffset=(rng()%4000)*16;c.draw.vertexOffset1=(rng()%400)*8;
        c.draw.indexBuffer=i%4?&buffers[2]:nullptr;c.draw.indexOffset=(rng()%200)*2;
        c.draw.instances=i%7==0?3:1;c.draw.maxIndex=i%3==0?-1:23;c.draw.first=i%5;c.draw.count=i%40+1;
        if(i%211==0){p=Pass{};q=Pass{};before.enabled=after.enabled=0;before.array=after.array=0;before.element=after.element=0;}
        tizenSeparateVertexBinding=i%17!=0;
        tizenpsp_set_vertex_binding(i%31!=0?1:0);
        tizenBindingAPI=i%19==0?TizenVertexBindingAPI{Format,nullptr,BufferBinding}:TizenVertexBindingAPI{Format,AttributeBinding,BufferBinding};
        active=&before;BaselineDraw(c,p);active=&after;OptimizedDraw(c,q);
        assert(before.observed==after.observed);
    }
    // Report deterministic API reduction for a steady four-attribute layout.
    before=State{};after=State{};p=Pass{};q=Pass{};tizenSeparateVertexBinding=true;tizenBindingAPI={Format,AttributeBinding,BufferBinding};
    tizenpsp_set_vertex_binding(1);
    for(int i=0;i<10000;i++) {
        GLRRenderData c{};c.draw.inputLayout=&base;c.draw.vertexBuffer=&buffers[0];c.draw.vertexOffset=i*36;
        active=&before;BaselineDraw(c,p);active=&after;OptimizedDraw(c,q);assert(before.observed==after.observed);
    }
    auto legacy=before.pointerCalls+before.arrayCalls;
    auto modern=after.formatCalls+after.bindingCalls+after.arrayCalls;
    assert(legacy==40001 && modern==10008);
    // A capability downgrade must take the unmodified legacy path.
    for(int missing=0;missing<3;missing++) {
        auto api=TizenVertexBindingAPI{Format,AttributeBinding,BufferBinding};
        if(missing==0)api.format=nullptr;if(missing==1)api.attributeBinding=nullptr;if(missing==2)api.bufferBinding=nullptr;
        assert(!q.vertexBindings.Apply(api,true,&base,10,0,0,0));
    }
    printf("PASS: 128 production capability probe combinations: frontend VAO preserved, unsupported/error fallback.\n");
    printf("PASS: 50000 production DRAW comparisons: vertex fetches, formats, two streams, indexed/ranged/instanced draws, fallback and pass resets match.\n");
    printf("STEADY_DRAW_SETUP baseline_calls=%llu optimized_calls=%llu draws=10000\n",legacy,modern);
}
