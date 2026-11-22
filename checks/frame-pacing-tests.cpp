#undef NDEBUG
#include <cassert>
#include <cstdio>
#include "../native/core-build/runtime/tizenpsp_frame_pacing.h"
int main() {
    unsigned vblank=0, calls=0, resumes=0;
    bool next=true;
    auto read=[&]{return vblank;};
    auto atFrame=[&]{return next;};
    auto resume=[&]{++resumes;};
    // Reproduce two early display yields per actual PSP refresh.
    for(unsigned i=0;i<600;i++) {
        unsigned slice=0;
        auto folded=TizenRunToVblank(read,[&]{++calls;if(++slice==3)++vblank;},atFrame,resume);
        assert(folded==2);
    }
    assert(vblank==600 && calls==1800 && resumes==1200);
    // Normal, static and skipped drawing still yield exactly one vblank.
    assert(TizenRunToVblank(read,[&]{++vblank;},atFrame,resume)==0);
    // Never resume a stop/error, including when it occurs before vblank.
    unsigned before=resumes;
    assert(TizenRunToVblank(read,[&]{next=false;},atFrame,resume)==0 && resumes==before);
    // Malformed/no-progress exits cannot busy-loop without a bound.
    next=true;calls=0;
    assert(TizenRunToVblank(read,[&]{++calls;},atFrame,resume)==7 && calls==8);
    // Counter reset/wrap counts as a boundary, never an infinite catch-up loop.
    vblank=0xffffffff;
    assert(TizenRunToVblank(read,[&]{++vblank;},atFrame,resume)==0);
    puts("PASS: early display yields folded into one vblank; normal/stop/no-progress/wrap behavior bounded");
}
