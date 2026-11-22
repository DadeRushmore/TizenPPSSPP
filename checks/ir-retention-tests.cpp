#undef NDEBUG
#include <cassert>
#include <cstdio>
#include <algorithm>
#include <string>
#include <vector>
#include "../native/core-build/runtime/tizenpsp_ir_retention.h"
#include "../native/core-build/runtime/tizenpsp_invalidation_probe.h"
using u32 = uint32_t; using u8 = uint8_t;
// Actual IR enum/layout; guest memory and frontend below are controlled fixtures.
#include "../build/retention-ir-types.h"
namespace Memory {
static u8 bytes[1024]{};
static u32 g_MemorySize = sizeof(bytes);
static bool depth = false, unreadable = false;
static bool DepthMirrorsActive() { return depth; }
static bool MayRunInKernelMode(u32 address) { return (address & 0x80000000U) != 0; }
static bool IsValidRange(u32 address, u32 size) { return !unreadable && address < sizeof(bytes) && size <= sizeof(bytes)-address; }
}
struct { bool bFastMemory = true; u32 uJitDisableFlags = 0; } g_Config;
struct { bool tracing_enabled = false; } mipsTracer;
struct Debug {
 bool bp = false, temp = false, mem = false;
 bool HasBreakPoints() const { return bp; }
 bool HasTempBreakPoint() const { return temp; }
 bool HasMemChecks() const { return mem; }
} g_breakpoints;
struct MIPSState { bool media = false; bool IsMediaEngine() const { return media; } } cpu;
struct IRFrontend {
 unsigned calls = 0, context = 0; bool roundingChange = false; u32 size = 32; IROp op = IROp::SetConst;
 void DoJit(u32 address, std::vector<IRInst> &out, u32 &bytes) {
  ++calls; bytes = size;
  // Models an out-of-block lookahead dependency and a compiler-state dependency.
  out = {{op, {1}, 0, 0, context + Memory::bytes[address+32]}};
 }
 bool CheckRounding(u32) { return roundingChange; }
};
struct GuestSpace { const u8 *GetPointerUnchecked(u32 address) const { return Memory::bytes+address; } };
struct JitBlock {
 u32 address = 0, size = 32; bool valid = true;
 bool IsValid() const { return valid; }
 void GetRange(u32 *a, u32 *s) const { *a = address; *s = size; }
 void Destroy(const GuestSpace &) { valid = false; }
};
struct Table {
 JitBlock records[16]{}; std::vector<int> ids, removed; GuestSpace space;
 JitBlock *GetBlock(int id) { return &records[id]; }
 const GuestSpace &Space() const { return space; }
 std::vector<int> FindInvalidatedBlockNumbers(u32, int) const { return ids; }
 void RemoveBlockFromPageLookup(int id) { removed.push_back(id); }
 void Clear() { ids.clear(); for (auto &r : records) r.valid = false; }
};
enum class TizenPerfPhase { InvalidationProbe, IRRetention };
struct TizenPerfScope { explicit TizenPerfScope(TizenPerfPhase) {} };
static bool TizenInvalidationRecord(const char *, unsigned, unsigned, unsigned, unsigned) { return false; }
static unsigned seenCandidates, seenRevalidated, seenRetained;
static void TizenRetentionRecord(unsigned candidates, unsigned revalidated, unsigned retained, size_t) {
 seenCandidates=candidates; seenRevalidated=revalidated; seenRetained=retained;
}
#define INFO_LOG(...) ((void)0)
#define DEBUG_LOG(...) ((void)0)
struct IRJit {
 struct { bool optimizeForInterpreter = false; } jo;
 MIPSState *mips_ = &cpu; bool profiling_ = false;
 Table blocks_; IRFrontend frontend_; TizenIRRetention tizenRetention_; TizenInvalidationProbe tizenProbe_;
 const char *tizenInvalidationSource = "sceKernelIcacheInvalidateAll"; u32 tizenInvalidationPC = 123;
 unsigned invalidations_ = 0, invalidatedBlocks_ = 0;
 std::vector<int> nativeInvalidations;
 bool TizenRetentionEnabled() const;
 TizenIRRetention::Context TizenRetentionContext() const;
 bool TizenTryRetainBlock(int, unsigned &);
 void InvalidateCacheAt(u32, int);
 void InvalidateBlock(int);
 void ClearCache();
 void WriteProfileReport() {}
 void InvalidateTargetBlock(int id) { assert(blocks_.records[id].valid); nativeInvalidations.push_back(id); }
 void Capture(int id, u32 address) {
  blocks_.records[id] = {address, 32, true};
  IRFrontend copy=frontend_; std::vector<IRInst> ir; u32 size;
  copy.DoJit(address,ir,size);
  assert(TizenRetainableForTest(ir));
  assert(tizenRetention_.Capture(id,address,TizenRetentionContext(),Memory::bytes+address,size,ir.data(),ir.size()*sizeof(IRInst)));
 }
 static bool TizenRetainableForTest(const std::vector<IRInst> &);
 void Broad() { tizenInvalidationSource="sceKernelIcacheInvalidateAll"; InvalidateCacheAt(0,0x3FFFFFFF); }
};
#include "../build/retention-methods.h"
bool IRJit::TizenRetainableForTest(const std::vector<IRInst> &ir) { return TizenRetainableIR(ir); }
static void reset() {
 std::memset(Memory::bytes,0,sizeof(Memory::bytes)); Memory::depth=false; Memory::unreadable=false;
 Memory::g_MemorySize=sizeof(Memory::bytes); g_Config={true,0}; mipsTracer.tracing_enabled=false;
 g_breakpoints={}; cpu.media=false; seenCandidates=seenRevalidated=seenRetained=0;
}
int main() {
 reset();
 const char *all="sceKernelIcacheInvalidateAll";
 assert(TizenIRRetention::BroadRequest(all,0,0x3FFFFFFF));
 assert(TizenIRRetention::BroadRequest("sceKernelIcacheClearAll",0,0x3FFFFFFF));
 for (const char *source : {"sceKernelIcacheInvalidateRange","PopResult","InvalidateReplacedAddress","direct_jit"})
  assert(!TizenIRRetention::BroadRequest(source,0,0x3FFFFFFF));
 assert(!TizenIRRetention::BroadRequest(nullptr,0,0x3FFFFFFF));
 assert(!TizenIRRetention::BroadRequest(all,4,0x3FFFFFFF));
 assert(!TizenIRRetention::BroadRequest(all,0,32));
 // Production loop retains an unchanged block, invalidates a tail change and an untracked block.
 IRJit j; j.Capture(0,64); j.Capture(1,128); j.blocks_.records[2]={192,32,true}; j.blocks_.ids={0,1,2};
 Memory::bytes[159]=1; j.Broad();
 assert((j.nativeInvalidations==std::vector<int>{1,2}));
 assert(j.blocks_.records[0].valid && j.invalidatedBlocks_==2);
 assert(seenCandidates==3 && seenRevalidated==1 && seenRetained==1);
 assert(j.frontend_.calls==0); // Revalidation never changes the running frontend.
 j.blocks_.ids={0}; j.Broad(); assert(seenRetained==1 && j.nativeInvalidations.size()==2);
 // Same block bytes, but changed lookahead/IR, must invalidate.
 Memory::bytes[96]=4; j.Broad(); assert(!j.blocks_.records[0].valid && seenRetained==0);
 for (int scenario=0;scenario<12;++scenario) {
  reset(); IRJit x; x.Capture(0,64); x.blocks_.ids={0};
  switch(scenario) {
  case 0: x.frontend_.context=1; break;
  case 1: x.frontend_.roundingChange=true; break;
  case 2: x.frontend_.size=36; break;
  case 3: x.frontend_.op=IROp::CallReplacement; break;
  case 4: Memory::depth=true; break;
  case 5: g_Config.bFastMemory=false; break;
  case 6: Memory::g_MemorySize=512; break;
  case 7: g_Config.uJitDisableFlags=1; break;
  case 8: Memory::unreadable=true; break;
  case 9: x.blocks_.records[0].address=128; break;
  case 10: x.blocks_.records[0].address=0x80000040; break;
  case 11: x.tizenRetention_.Forget(0); break;
  }
  x.Broad(); assert(x.nativeInvalidations.size()==1 && seenRetained==0 && x.tizenRetention_.Used()==0);
 }
 for (int scenario=0;scenario<7;++scenario) {
  reset(); IRJit x; x.Capture(0,64); x.blocks_.ids={0};
  switch(scenario) {
  case 0:g_breakpoints.bp=true;break; case 1:g_breakpoints.temp=true;break;
  case 2:g_breakpoints.mem=true;break; case 3:cpu.media=true;break;
  case 4:mipsTracer.tracing_enabled=true;break; case 5:x.profiling_=true;break;
  case 6:x.jo.optimizeForInterpreter=true;break;
  }
  x.Broad(); assert(x.nativeInvalidations.size()==1 && seenRevalidated==0);
 }
 for (const char *source : {"PopResult","InvalidateReplacedAddress","sceKernelIcacheInvalidateRange","direct_jit"}) {
  reset(); IRJit x; x.Capture(0,64); x.blocks_.ids={0}; x.tizenInvalidationSource=source;
  x.InvalidateCacheAt(0,0x3FFFFFFF); assert(x.nativeInvalidations.size()==1);
 }
 reset(); IRJit cleared; cleared.Capture(0,64); cleared.ClearCache();
 assert(cleared.tizenRetention_.Used()==0); cleared.Capture(0,128);
 cleared.InvalidateBlock(0); assert(cleared.tizenRetention_.Used()==0);
 for (auto op : {IROp::Syscall,IROp::SyscallUnresolved,IROp::CallReplacement,IROp::Interpret,
                 IROp::Break,IROp::Breakpoint,IROp::MemoryCheck,IROp::LogIRBlock,IROp::Bad})
  assert(!TizenRetainableIR({{op,{0},0,0,0}}));
 assert(!TizenRetainableIR({}));
 // Storage failure never authorizes reuse; exact IR and guest address are required.
 TizenIRRetention tiny(8); TizenIRRetention::Context context{}; u32 code=7, ir=8;
 assert(tiny.Capture(0,64,context,&code,4,&ir,4)); assert(!tiny.Capture(1,64,context,&code,4,&ir,4));
 assert(!tiny.Capture(0,64,context,&code,4,&ir,4));
 assert(tiny.CodeMatches(0,64,context,&code,4)); assert(!tiny.CodeMatches(0,128,context,&code,4));
 assert(!tiny.CodeMatches(0,64,context,nullptr,4)); assert(!tiny.IRMatches(0,&code,4));
 assert(!tiny.IRMatches(0,&ir,3)); tiny.Forget(0); assert(tiny.Used()==0);
 assert(!tiny.CodeMatches(0,64,context,&code,4));
 assert(!tiny.Capture(0,64,context,&code,0,&ir,4)); assert(!tiny.Capture(0,64,context,&code,65537,&ir,4));
 TizenIRRetention capped;
 for (int i=0;i<32768;++i) assert(capped.Capture(i,64,context,&code,4,&ir,4));
 assert(!capped.Capture(32768,64,context,&code,4,&ir,4)); capped.Clear(); assert(capped.Used()==0);
 puts("PASS: production retention decision/invalidation/clear methods: unchanged kept; changed code, lookahead/IR, context, rounding, hooks, debug, unknown and direct requests invalidate; bounded evidence and frontend-copy isolation.");
}
