// Exact, bounded evidence for retaining native translations. No hash-only reuse.
#pragma once
#include <array>
#include <cstdint>
#include <cstring>
#include <unordered_map>
#include <vector>

class TizenIRRetention {
public:
 using Context = std::array<uint32_t, 4>;
 explicit TizenIRRetention(size_t budget = 16 * 1024 * 1024) : budget_(budget) {}
 static bool BroadRequest(const char *source, uint32_t address, int length) {
  return source && address == 0 && length == 0x3FFFFFFF &&
   (!std::strcmp(source, "sceKernelIcacheInvalidateAll") || !std::strcmp(source, "sceKernelIcacheClearAll"));
 }
 bool Capture(int id, uint32_t address, const Context &context, const void *code, size_t codeSize,
              const void *ir, size_t irSize) {
  if (!code || !ir || !codeSize || !irSize || codeSize > 65536 || irSize > 65536 ||
      entries_.size() >= 32768 || codeSize + irSize > budget_ - used_ || entries_.count(id)) return false;
  Entry e{address, context, Copy(code, codeSize), Copy(ir, irSize)};
  entries_.emplace(id, std::move(e)); used_ += codeSize + irSize; return true;
 }
 bool CodeMatches(int id, uint32_t address, const Context &context, const void *code, size_t size) const {
  auto it = entries_.find(id);
  return it != entries_.end() && address == it->second.address && context == it->second.context &&
   Equal(it->second.code, code, size);
 }
 bool IRMatches(int id, const void *ir, size_t size) const {
  auto it = entries_.find(id); return it != entries_.end() && Equal(it->second.ir, ir, size);
 }
 void Forget(int id) {
  auto it = entries_.find(id); if (it == entries_.end()) return;
  used_ -= it->second.code.size() + it->second.ir.size(); entries_.erase(it);
 }
 void Clear() { entries_.clear(); used_ = 0; }
 size_t Used() const { return used_; }
private:
 struct Entry { uint32_t address; Context context; std::vector<uint8_t> code, ir; };
 static std::vector<uint8_t> Copy(const void *p, size_t n) {
  auto bytes = static_cast<const uint8_t *>(p); return {bytes, bytes + n};
 }
 static bool Equal(const std::vector<uint8_t> &v, const void *p, size_t n) {
  return p && n && v.size() == n && std::memcmp(v.data(), p, n) == 0;
 }
 size_t budget_, used_ = 0;
 std::unordered_map<int, Entry> entries_;
};
