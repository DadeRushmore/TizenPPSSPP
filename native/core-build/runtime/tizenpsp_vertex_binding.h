// GLES 3.1 separate vertex formats/bindings. GPL-2.0-or-later.
#pragma once
#include <cstdint>

struct TizenVertexBindingAPI {
    void (*format)(unsigned, int, unsigned, unsigned char, unsigned) = nullptr;
    void (*attributeBinding)(unsigned, unsigned) = nullptr;
    void (*bufferBinding)(unsigned, unsigned, intptr_t, int) = nullptr;
    bool Available() const { return format && attributeBinding && bufferBinding; }
};

// One instance per render pass: never reuse knowledge across another VAO,
// frontend frame, resource deletion, or context recreation. Layouts are immutable
// during a queued pass. No new draws, indexing changes, or vertex data changes.
class TizenVertexBindingState {
    const void *layout_ = nullptr;
    struct Binding { bool known=false; unsigned buffer=0; intptr_t offset=0; int stride=0; } bindings_[2];
public:
    unsigned fastDraws=0, fallbackDraws=0, formatCalls=0, bindingCalls=0, legacyAttributeCalls=0;
    void Forget() { layout_=nullptr;bindings_[0].known=bindings_[1].known=false; }
    template<class Layout>
    bool Apply(const TizenVertexBindingAPI &api, bool enabled, const Layout *layout,
               unsigned buffer, intptr_t offset, unsigned buffer1, intptr_t offset1) {
        bool valid=enabled && api.Available() && buffer && offset>=0 && offset1>=0 &&
            layout->stride>0 && layout->stride<=2048 && layout->stride1>=0 && layout->stride1<=2048;
        if(layout_!=layout && valid) {
            unsigned seen=0;
            for(const auto &e:layout->entries) {
                if(e.location<0 || e.location>=16 || e.count<1 || e.count>4 || e.offset<0 || e.offset>2047 ||
                   e.stream<0 || e.stream>1 || (e.stream==1 && (!layout->stride1 || !buffer1)) || (seen&(1u<<e.location))) {valid=false;break;}
                seen|=1u<<e.location;
            }
        }
        if(layout->stride1 && !buffer1)valid=false;
        if(!valid) {fallbackDraws++;Forget();return false;}
        if(layout_!=layout) {
            for(const auto &e:layout->entries) {
                api.format(e.location,e.count,e.type,e.normalized,(unsigned)e.offset);
                api.attributeBinding(e.location,e.stream);formatCalls+=2;
            }
            layout_=layout;
        }
        const unsigned buffers[]={buffer,buffer1};const intptr_t offsets[]={offset,offset1};
        const int strides[]={layout->stride,layout->stride1};
        for(int i=0;i<2;i++) {
            if(i==1 && !strides[i])continue;
            auto &b=bindings_[i];
            if(!b.known || b.buffer!=buffers[i] || b.offset!=offsets[i] || b.stride!=strides[i]) {
                api.bufferBinding(i,buffers[i],offsets[i],strides[i]);bindingCalls++;
                b={true,buffers[i],offsets[i],strides[i]};
            }
        }
        fastDraws++;legacyAttributeCalls+=(unsigned)layout->entries.size();return true;
    }
};
