#pragma once
#include <cstddef>
#define MEM_PROT_READ 1
#define MEM_PROT_WRITE 2
#define MEM_PROT_EXEC 4
bool PlatformIsWXExclusive();
inline size_t GetMemoryProtectPageSize() { return 4096; }
void *AllocateExecutableMemory(size_t bytes);
void FreeExecutableMemory(void *pointer, size_t bytes);
bool ProtectMemoryPages(const void *pointer, size_t bytes, int flags);
