// Actual ARM and native bookkeeping methods; host recording emitter, not ARM execution.
#undef NDEBUG
#define TIZENPSP_ARM_JIT
#include <cassert>
#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <vector>
#include <map>
#include <unordered_map>
using u8 = uint8_t;
using u32 = uint32_t;
static constexpr int MIN_BLOCK_NORMAL_LEN=12, MIN_BLOCK_EXIT_LEN=4, SCRATCH1=0;
static constexpr int MEM_PROT_READ=1, MEM_PROT_WRITE=2, MEM_PROT_EXEC=4;
static constexpr int NATIVE_CODE_SPACE_SIZE=32*1024*1024-64*1024;
#define _dbg_assert_(c) assert(c)
struct Operation { const u8 *at, *target; u32 immediate; };
struct Protection { const u8 *at; int length, flags; };
static std::vector<Operation> ops;
static std::vector<Protection> protections;
static unsigned linked, dispatched, invalidated, skipped, clears;
static bool flushed;
static bool PlatformIsWXExclusive() { return true; }
static void ProtectMemoryPages(const u8 *p,int length,int flags) {
    assert(flags==3 || flags==5); protections.push_back({p,length,flags});
}
static void TizenJitExit(bool link) { if(link)++linked;else ++dispatched; }
static void TizenJitInvalidation() { ++invalidated; }
static void TizenJitInvalidationSkipped() { ++skipped; }
static void TizenJitCacheClear() { ++clears; }
static void TizenJitCacheUsage(size_t,size_t) {}
struct IRNativeBlockExit { int offset,len; u32 dest; };
#include "../build/arm-link-block.h"
struct ARMXEmitter {
    explicit ARMXEmitter(u8 *p):cursor(p) {}
    u8 *cursor;
    void B(const u8 *target) { ops.push_back({cursor,target,0});cursor+=4; }
    void MOVI2R(int,u32 v) { ops.push_back({cursor,nullptr,v});cursor+=8; }
    void ReserveCodeSpace(int n) { cursor+=n; }
    void FlushIcache() { flushed=true; }
    u8 *GetWritableCodePtr() { return cursor; }
};
struct ArmJitBackend:ARMXEmitter {
    u8 buffer[8192]{};
    u8 *region=buffer;
    int region_size=sizeof(buffer),jitStartOffset_=64;
    struct { bool enableBlocklink=true; } jo;
    std::vector<IRNativeBlock> nativeBlocks_;
    std::unordered_map<u32,int> blockByStart_;
    std::multimap<u32,int> linksTo_;
    int registered=0,compilingBlockNum_=0;
    const u8 *dispatcherPCInSCRATCH1_=buffer+16;
    ArmJitBackend():ARMXEmitter(buffer+4092) {}
    int FindBlockByStart(u32 pc) const;
    const IRNativeBlock *GetNativeBlock(int n) const;
    void SetBlockEntry(int n,u32 pc,int offset);
    void SetBlockCheckedOffset(int n,int offset);
    const u8 *GetCodePointer() { return cursor; }
    const u8 *GetBasePtr() { return buffer; }
    int GetOffset(const u8 *p) { return (int)(p-buffer); }
    u8 *GetWritablePtrFromCodePtr(const u8 *p) { return const_cast<u8 *>(p); }
    void AddLinkableExit(int,u32,int,int) { ++registered; }
    void EraseAllLinks(int);
    void ClearCodeSpace(int offset) { cursor=buffer+offset; }
    void FlushIcacheSection(u8 *,u8 *) { flushed=true; }
    void WriteConstExit(u32 pc);
    void InvalidateBlock(int n);
    void OverwriteExit(int offset,int length,int n);
    void ClearAllBlocks();
    void NewBlock(int n,u32 pc,int checked) { SetBlockCheckedOffset(n,checked);SetBlockEntry(n,pc,checked+16); }
};
#include "../build/arm-link-methods.h"
static void reset() { ops.clear();protections.clear();flushed=false; }
int main() {
    ArmJitBackend jit;const u32 pc=0x08800000;
    jit.NewBlock(0,pc,64);jit.NewBlock(1,pc+128,128);
    // Dispatcher-only invalidation removes lookup with no native rewrite.
    assert(!jit.GetNativeBlock(1)->tizenHasIncomingBranch);
    jit.InvalidateBlock(1);
    assert(skipped==1 && ops.empty() && protections.empty() && !flushed);
    assert(jit.FindBlockByStart(pc+128)==-1);
    // Direct exit marks its target and reaches checked entry.
    jit.WriteConstExit(pc);
    assert(jit.GetNativeBlock(0)->tizenHasIncomingBranch);
    assert(ops.size()==1 && ops.back().target==jit.buffer+64);
    assert(jit.registered==0 && protections.empty() && linked==1);
    reset();jit.NewBlock(2,pc+256,256);jit.WriteConstExit(pc+512);
    assert(ops.size()==2 && ops[0].immediate==pc+512 && ops[1].target==jit.dispatcherPCInSCRATCH1_);
    jit.jo.enableBlocklink=false;reset();jit.WriteConstExit(pc+256);
    assert(ops.size()==2 && !jit.GetNativeBlock(2)->tizenHasIncomingBranch && dispatched==2);
    jit.jo.enableBlocklink=true;
    // Removing a source cannot clear the target's conservative lifetime flag.
    jit.compilingBlockNum_=2; jit.WriteConstExit(pc); reset(); jit.InvalidateBlock(2);
    assert(skipped==2 && protections.empty() && jit.GetNativeBlock(0)->tizenHasIncomingBranch);
    reset();jit.InvalidateBlock(0);
    assert(flushed && jit.FindBlockByStart(pc)==-1 && skipped==2);
    assert(ops[0].at==jit.buffer+80 && ops[0].immediate==pc && ops[1].target==jit.dispatcherPCInSCRATCH1_);
    assert(protections.size()==2 && protections[0].flags==3 && protections[1].flags==5);
    assert(protections[0].length==12 && protections[1].length==12);
    reset();jit.WriteConstExit(pc);
    assert(ops.size()==2 && ops[1].target==jit.dispatcherPCInSCRATCH1_);
    // A new version at the same PC starts unlinked; old edges keep old trampoline.
    jit.NewBlock(3,pc,384);assert(!jit.GetNativeBlock(3)->tizenHasIncomingBranch);
    reset();jit.WriteConstExit(pc);
    assert(ops.size()==1 && ops[0].target==jit.buffer+384 && jit.GetNativeBlock(3)->tizenHasIncomingBranch);
    jit.InvalidateBlock(0);assert(jit.FindBlockByStart(pc)==3);
    // Self edges and deferred rewrite paths also require the trampoline.
    jit.NewBlock(4,pc+1024,512);reset();jit.WriteConstExit(pc+1024);
    assert(jit.GetNativeBlock(4)->tizenHasIncomingBranch);
    jit.InvalidateBlock(4);assert(protections.size()==2);
    jit.NewBlock(5,pc+2048,640);reset();jit.OverwriteExit(2048,12,5);
    assert(jit.GetNativeBlock(5)->tizenHasIncomingBranch);
    reset();jit.InvalidateBlock(5);assert(protections.size()==2 && flushed);
    // Cache reset clears lifetime flags, including reused block numbers.
    reset();jit.ClearAllBlocks();
    assert(clears==1 && jit.nativeBlocks_.empty() && jit.blockByStart_.empty());
    jit.NewBlock(0,pc,64);reset();jit.InvalidateBlock(0);
    assert(skipped==3 && protections.empty() && ops.empty());
    // Retained source -> changed target: keep the source entry, patch only target.
    ArmJitBackend mixed; mixed.NewBlock(0,pc,64); mixed.NewBlock(1,pc+128,128);
    mixed.compilingBlockNum_=0; reset(); mixed.WriteConstExit(pc+128);
    const u8 *oldTarget=ops[0].target; assert(oldTarget==mixed.buffer+128);
    reset(); mixed.InvalidateBlock(1);
    assert(mixed.FindBlockByStart(pc)==0 && mixed.FindBlockByStart(pc+128)==-1);
    assert(mixed.GetNativeBlock(0)->entryOffset==80);
    assert(ops[0].at==mixed.buffer+144 && ops[1].target==mixed.dispatcherPCInSCRATCH1_);
    assert(protections.size()==2 && protections[0].flags==3 && protections[1].flags==5);
    mixed.NewBlock(2,pc+128,256); reset(); mixed.WriteConstExit(pc+128);
    assert(ops[0].target==mixed.buffer+256 && oldTarget!=ops[0].target);
    assert(mixed.FindBlockByStart(pc)==0); // Recompiling target does not destroy retained source.
    puts("PASS: retained source keeps entry while changed target uses old-entry trampoline and a new version.");
    puts("PASS: actual ARM methods/bookkeeping: unlinked bypass, direct/self links, source invalidation, old/new PC versions, deferred path, checked entry, cache reset and W^X restoration (recording emitter).");
}
