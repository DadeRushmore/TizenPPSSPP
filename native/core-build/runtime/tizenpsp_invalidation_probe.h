// Bounded exact guest-byte samples. Diagnostic only; never authorizes code reuse.
#pragma once
#include <cstdint>
#include <cstring>
#include <unordered_map>
#include <vector>
class TizenInvalidationProbe {
public:
 enum Result { Unknown, Same, Changed };
 explicit TizenInvalidationProbe(size_t budget=4*1024*1024) : budget_(budget) {}
 bool Capture(int id,const uint8_t *bytes,size_t size) {
  if (samples_.size()>=4096 || (id & 15) || !bytes || !size || size>65536 || size>budget_-used_)return false;
  auto old=samples_.find(id);
  if(old!=samples_.end())return false;
  samples_.emplace(id,std::vector<uint8_t>(bytes,bytes+size));used_+=size;return true;
 }
 Result Observe(int id,const uint8_t *bytes,size_t size) {
  auto it=samples_.find(id);if(it==samples_.end())return Unknown;
  Result r=(!bytes || !size)?Unknown:it->second.size()!=size?Changed:
   std::memcmp(it->second.data(),bytes,size)==0?Same:Changed;
  used_-=it->second.size();samples_.erase(it);return r;
 }
 void Forget(int id) {Observe(id,nullptr,0);}
 void Clear(){samples_.clear();used_=0;}
 size_t Used() const{return used_;}
private:
 size_t budget_,used_=0;
 std::unordered_map<int,std::vector<uint8_t>> samples_;
};
