// Production table methods; modeled block lifecycle, no ARM/TV execution.
#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <stdexcept>
#include <unordered_map>
#include <vector>
#include <random>
using u32=uint32_t;
#define WARN_LOG(...) ((void)0)
struct JitBlock {
 u32 origAddr_,origSize_;bool valid=true;
 bool IsValid() const {return valid;}
 u32 GetOriginalStart() const {return origAddr_;}
 void GetRange(u32 *a,u32 *s) const {*a=origAddr_;*s=origSize_;}
 bool OverlapsRange(u32,u32) const;
 void Finalize(int) {valid=true;}
 void Destroy() {valid=false;origAddr_=0;}
};
struct JitBlockTable {
 std::vector<JitBlock> blocks_;std::unordered_map<u32,std::vector<int>> byPage_;
 int dummy=0;int *space_=&dummy;
 u32 AddressToPage(u32) const;
 void FinalizeBlock(int);void RemoveBlockFromPageLookup(int);
 std::vector<int> FindInvalidatedBlockNumbers(u32,u32);
 void Add(u32 a,u32 size) {blocks_.push_back({a,size,true});FinalizeBlock((int)blocks_.size()-1);}
};
#include "../build/ir-invalidation-methods.h"
void Need(bool b,const char *m) {if(!b)throw std::runtime_error(m);}
size_t queries=0;
std::vector<int> Check(JitBlockTable &t,u32 addr,u32 size) {
 auto found=t.FindInvalidatedBlockNumbers(addr,size);std::sort(found.begin(),found.end());
 Need(std::adjacent_find(found.begin(),found.end())==found.end(),"duplicate invalidation");
 std::vector<int> expected;
 uint64_t lo=addr&0x3fffffff,hi=lo+size;
 for(size_t i=0;i<t.blocks_.size();++i) {
  auto &b=t.blocks_[i];uint64_t bl=b.origAddr_&0x3fffffff;
  if(b.valid && lo<bl+b.origSize_ && hi>bl)expected.push_back((int)i);
 }
 Need(found==expected,"affected block set differs from overlap oracle");++queries;return found;
}
int main(int argc, char **) {try {
 if(argc>1) {JitBlockTable x;for(int i=0;i<4096;++i)x.Add(0x08000000+i*8192,4096);printf("%zu\n",x.FindInvalidatedBlockNumbers(0,0x3fffffff).size());return 0;}
 JitBlockTable t;
 // Sparse full-map traversal: one block occurs in five indexed pages.
 t.Add(0x08000000,4096);Need(Check(t,0,0x3fffffff).size()==1,"full range");
 // Dense map forces the per-page traversal, including a query starting inside.
 for(int i=0;i<2000;++i)t.Add(0x08100000+i*1024,16);
 Check(t,0x08000400,2048);Check(t,0x88000400,2048);
 Check(t,0x08001000,4);Check(t,0x08000000-4,4);
 Check(t,0x08000000+4095,1);
 // Touching a boundary, overlapping/mirrored blocks and unaligned queries.
 t.Add(0x080003fc,12);t.Add(0x88000000,4096);
 std::mt19937 rng(12345);
 for(int i=0;i<2000;++i) {
  u32 addr=0x08000000+rng()%0x300000;
  if(i%2)addr|=0x80000000;
  Check(t,addr,1+rng()%8192);
 }
 for(int i=0;i<500;++i) {
  auto found=Check(t,0x08000000+rng()%0x300000,1+rng()%0x10000);
  for(int n:found){t.RemoveBlockFromPageLookup(n);t.blocks_[n].Destroy();}
 }
 auto all=Check(t,0,0x3fffffff);
 for(int n:all){t.RemoveBlockFromPageLookup(n);t.blocks_[n].Destroy();}
 Need(t.byPage_.empty(),"empty page entries retained");
 Check(t,0,0x3fffffff);
 // Repeated removal may not create phantom pages after Destroy.
 t.RemoveBlockFromPageLookup(0);Need(t.byPage_.empty(),"phantom page inserted");
 t.Add(0x08000000,4096);Need(Check(t,0,0x3fffffff).size()==1,"recompile at same address");
 // Reproducible multi-page stress: old lookup returned five entries per block.
 JitBlockTable stress;for(int i=0;i<4096;++i)stress.Add(0x08000000+i*8192,4096);
 Need(Check(stress,0,0x3fffffff).size()==4096,"stress set");
 printf("PASS: %zu oracle comparisons, both lookup paths, aliases, page boundaries, removal/recompile; 4096 multi-page blocks invalidated once each (old path: 20480 calls)\n",queries);
 return 0;
 }catch(const std::exception &e){fprintf(stderr,"%s\n",e.what());return 1;}}
