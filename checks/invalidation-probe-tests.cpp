#undef NDEBUG
#include <cassert>
#include <cstdio>
#include <string>
#include <vector>
#include "../native/core-build/runtime/tizenpsp_invalidation_probe.h"
#include "../native/core-build/runtime/tizenpsp_ir_retention.h"
using u32=uint32_t;
struct PendingCacheOperation {u32 addr,size;const char *source;u32 pc;};
struct Request {std::string source;u32 pc,addr,size;};
struct FakeJit {
 const char *tizenInvalidationSource="direct_jit";u32 tizenInvalidationPC=0;
 std::vector<Request> requests;unsigned clears=0;
 void InvalidateCacheAt(u32 addr,u32 size){requests.push_back({tizenInvalidationSource,tizenInvalidationPC,addr,size});}
 void ClearCache(){++clears;}
};
struct MIPSState {
 FakeJit *jit=nullptr;u32 pc=0;bool media=false,invalidateAll_=false;
 std::vector<PendingCacheOperation> pendingInvalidates_;
 bool IsMediaEngine() const{return media;}
 #include "../build/invalidation-request-declarations.h"
 void ProcessPendingInvalidates();
};
static MIPSState *mediaTarget=nullptr;
static MIPSState *MediaEngineToNotify(MIPSState *s){return s->media?nullptr:mediaTarget;}
static void Core_ReenterDispatcher(){}
static unsigned replacementChecks=0;
static void RemoveModifiedReplacementsInRange(u32,u32){++replacementChecks;}
#include "../build/invalidation-request-methods.h"
static void GameIcacheCaller(MIPSState &m){m.InvalidateICacheRangeDeferred(0x8800000,64);}
static void FileReadCaller(MIPSState &m){m.InvalidateICacheRangeImmediate(0x8800100,32);}

using u8=uint8_t;
namespace Memory { static u8 bytes[96]{};static bool IsValidRange(u32 a,u32 n){return a<=96 && n<=96-a;} }
struct ProbeBlock {u32 addr,size;void GetRange(u32 *a,u32 *s) const{*a=addr;*s=size;}};
struct ProbeSpace {const u8 *GetPointerUnchecked(u32 a)const{return Memory::bytes+a;}};
struct ProbeTable {
 ProbeBlock records[32]{};std::vector<int> ids;ProbeSpace space;
 std::vector<int> FindInvalidatedBlockNumbers(u32,int){return ids;}
 const ProbeBlock *GetBlock(int i)const{return &records[i];}
 const ProbeSpace &Space()const{return space;}
};
enum class TizenPerfPhase{InvalidationProbe,IRRetention};
static void TizenRetentionRecord(unsigned,unsigned,unsigned,size_t) {}
struct TizenPerfScope{explicit TizenPerfScope(TizenPerfPhase){}};
static unsigned seenBlocks,seenSame,seenChanged,seenUnknown;
static bool TizenInvalidationRecord(const char *,unsigned b,unsigned s,unsigned c,unsigned u){seenBlocks=b;seenSame=s;seenChanged=c;seenUnknown=u;return true;}
#define INFO_LOG(...) ((void)0)
#define DEBUG_LOG(...) ((void)0)
struct IRJit {
 ProbeTable blocks_;TizenInvalidationProbe tizenProbe_;
 TizenIRRetention tizenRetention_;
 bool TizenRetentionEnabled() const { return false; }
 bool TizenTryRetainBlock(int,unsigned &) { return false; }
 const char *tizenInvalidationSource="test";u32 tizenInvalidationPC=123;
 unsigned invalidations_=0,invalidatedBlocks_=0;std::vector<int> invalidated;
 void InvalidateCacheAt(u32,int);
 void InvalidateBlock(int n){invalidated.push_back(n);tizenProbe_.Forget(n);}
};
#include "../build/invalidation-observe-method.h"
int main(){
 uint8_t code[32]{};TizenInvalidationProbe p(64);
 assert(p.Capture(0,code,32));assert(!p.Capture(1,code,32));
 assert(p.Observe(0,code,32)==TizenInvalidationProbe::Same);assert(p.Used()==0);
 assert(p.Observe(0,code,32)==TizenInvalidationProbe::Unknown);
 assert(p.Capture(16,code,32));code[31]=1;
 assert(p.Observe(16,code,32)==TizenInvalidationProbe::Changed); // Last-byte change matters.
 assert(p.Capture(32,code,32));assert(p.Observe(32,code,31)==TizenInvalidationProbe::Changed);
 assert(p.Capture(48,code,32));assert(p.Observe(48,nullptr,32)==TizenInvalidationProbe::Unknown);
 assert(p.Capture(64,code,32));assert(!p.Capture(64,code,32));assert(p.Capture(80,code,32));
 assert(!p.Capture(96,code,32));p.Forget(64);assert(p.Capture(96,code,32));
 p.Clear();assert(p.Used()==0);assert(p.Observe(80,code,32)==TizenInvalidationProbe::Unknown);
 assert(!p.Capture(0,nullptr,32));assert(!p.Capture(0,code,0));assert(!p.Capture(0,code,65537));
 TizenInvalidationProbe bounded;for(int i=0;i<4096;++i)assert(bounded.Capture(i*16,code,1));
 assert(!bounded.Capture(4096*16,code,1));bounded.Clear();assert(bounded.Used()==0);
 FakeJit j,meJit;MIPSState m,me;m.jit=&j;m.pc=123;me.jit=&meJit;me.media=true;me.pc=456;mediaTarget=&me;
 GameIcacheCaller(m);m.pc=999;m.ProcessPendingInvalidates();
 assert(j.requests.size()==1 && j.requests[0].source=="GameIcacheCaller" && j.requests[0].pc==123);
 assert(j.requests[0].addr==0x8800000 && j.requests[0].size==64);
 me.ProcessPendingInvalidates();assert(meJit.requests[0].source=="GameIcacheCaller");
 FileReadCaller(m);assert(j.requests[1].source=="FileReadCaller" && j.requests[1].pc==999);
 GameIcacheCaller(m);m.invalidateAll_=true;size_t before=j.requests.size();m.ProcessPendingInvalidates();
 assert(j.clears==1 && j.requests.size()==before && m.pendingInvalidates_.empty());
 m.InvalidateICacheRangeImmediate(0,0);assert(j.requests.size()==before);
 m.jit=nullptr;GameIcacheCaller(m);m.ProcessPendingInvalidates();assert(m.pendingInvalidates_.empty());
 assert(replacementChecks>0);

 IRJit ir;ir.blocks_.records[0]={0,32};ir.blocks_.records[16]={32,32};ir.blocks_.records[17]={64,32};ir.blocks_.ids={0,16,17};
 assert(ir.tizenProbe_.Capture(0,Memory::bytes,32));assert(ir.tizenProbe_.Capture(16,Memory::bytes+32,32));
 Memory::bytes[63]=4;ir.InvalidateCacheAt(0,96);
 assert(seenBlocks==3 && seenSame==1 && seenChanged==1 && seenUnknown==1);
 assert(ir.invalidated==ir.blocks_.ids && ir.invalidatedBlocks_==3 && ir.tizenProbe_.Used()==0);
 assert(std::string(ir.tizenInvalidationSource)=="direct_jit" && ir.tizenInvalidationPC==0);
 ir.blocks_.ids.clear();ir.InvalidateCacheAt(0,96);assert(seenBlocks==0 && ir.invalidated.size()==3);
 puts("PASS: exact unchanged/changed bytes, tail/size changes, missing samples, byte/entry caps, clear/reuse; actual immediate/deferred methods preserve caller and queued guest PC, mirror source, full-clear and no-JIT semantics.");
}
