// Host generator using PPSSPP's real ARM instruction emitter. The output runs
// under Unicorn in check-arm-uv-morph.py; no ARM instructions execute on Windows.
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include "Common/ArmEmitter.h"
#include "Common/CPUDetect.h"

CPUInfo::CPUInfo() { bNEON=true;bVFPv3=true;bVFPv4=true;bIDIVa=true; }
CPUInfo cpu_info;
static bool logging=false;
bool *g_bLogEnabledSetting=&logging;
LogChannel g_log[(size_t)Log::NUMBER_OF_LOGS];
// The harness emits into its own array and never uses CodeBlock allocation.
bool ProtectMemoryPages(const void *,size_t,uint32_t) { std::abort(); }
void FreeExecutableMemory(void *,size_t) { std::abort(); }
void GenericLog(Log,LogLevel,const char *,int,const char *,...) {}
bool HandleAssert(bool,const char *function,const char *file,int line,const char *expr,const char *fmt,...) {
    std::fprintf(stderr,"Emitter assertion %s %s:%d: %s\n",function,file,line,expr);
    va_list args;va_start(args,fmt);std::vfprintf(stderr,fmt,args);va_end(args);
    std::abort();
}

using namespace ArmGen;
static const ARMReg tempReg1=R3,tempReg2=R4,scratchReg=R6;
static const ARMReg srcReg=R0,dstReg=R1,counterReg=R2;
static const ARMReg neonUVScaleReg=D0,neonUVOffsetReg=D1;
static const ARMReg neonScratchReg=D2,neonScratchReg2=D3,neonScratchRegQ=Q1;
static constexpr float by128=1.0f/128.0f,by32768=1.0f/32768.0f;
static constexpr int GE_VTYPE_TC_MASK=3,GE_VTYPE_TC_8BIT=1,GE_VTYPE_TC_16BIT=2;
struct MorphState { float morphWeights[8]; };
// A guest address, embedded by the real MOVP2R emitter without host dereferencing.
#define gstate_c (*reinterpret_cast<MorphState *>(uintptr_t(0x200000)))
struct Decoder {
    int morphcount,onesize_,tcoff;
    struct { int uvoff,stride; } decFmt;
    int VertexType() const {return GE_VTYPE_TC_16BIT;}
    int VertexSize() const {return morphcount*onesize_;}
};
class VertexDecoderJitCache : public ARMXEmitter {
public:
    const Decoder *dec_;
    VertexDecoderJitCache(u8 *code,const Decoder *dec):ARMXEmitter(code),dec_(dec) {}
    void Jit_TcU16PrescaleMorph();
    void Generate();
};
#include "../build/arm-uv-morph-method.h"

int main(int argc,char **argv) {
    if(argc!=7)return 2;
    Decoder dec{};
    dec.morphcount=std::atoi(argv[2]);dec.onesize_=std::atoi(argv[3]);
    dec.tcoff=std::atoi(argv[4]);dec.decFmt.uvoff=std::atoi(argv[5]);dec.decFmt.stride=std::atoi(argv[6]);
    alignas(16) u8 code[16384]{};
    VertexDecoderJitCache emitter(code,&dec);emitter.Generate();
    std::ofstream output(argv[1],std::ios::binary);
    output.write(reinterpret_cast<char *>(code),emitter.GetCodePointer()-code);
    return output.good()?0:3;
}
