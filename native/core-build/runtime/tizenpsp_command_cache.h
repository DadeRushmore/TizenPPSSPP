#pragma once
#include <cstdint>
#include <cstring>
// Producer-thread only. Exact state repeats can be omitted within one pass.
// Never caches resource bindings, draws, uniforms, clears or uploads.
class TizenCommandCache {
 struct Entry { bool valid=false; unsigned size=0; unsigned char bytes[64]{}; } entries[6];
public:
 void Clear(){for(auto &e:entries)e.valid=false;}
 bool Update(unsigned slot,const void *data,unsigned size){
  if(slot>=6 || !data || size>64 || !size){Clear();return true;}
  auto &e=entries[slot];
  if(e.valid && e.size==size && !memcmp(e.bytes,data,size))return false;
  e.valid=true;e.size=size;memcpy(e.bytes,data,size);return true;
 }
};
