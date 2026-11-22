// Exercise the actual PPSSPP CodeBlock template with real Windows page protections.
// Only platform allocation/protection, logging and the instruction emitter are mocked.
#undef NDEBUG
#include <windows.h>
#include <cstdio>
#include <cstring>
#include <stdexcept>
#include "../vendor/ppsspp/Common/CodeBlock.h"

static constexpr size_t Page = 4096, Capacity = 65536;
static void Need(bool ok, const char *message) { if (!ok) throw std::runtime_error(message); }
static size_t writableCalls=0, writablePages=0;
bool PlatformIsWXExclusive() { return true; }
void *AllocateExecutableMemory(size_t bytes) {
    void *p = VirtualAlloc(nullptr, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    Need(p != nullptr, "VirtualAlloc failed"); return p;
}
void FreeExecutableMemory(void *p, size_t) { Need(VirtualFree(p, 0, MEM_RELEASE) != 0, "VirtualFree failed"); }
bool ProtectMemoryPages(const void *p, size_t bytes, int flags) {
    Need(flags == 3 || flags == 5, "Unexpected/RWX permission request");
    if (!bytes) return true;
    if(flags==3) { ++writableCalls; writablePages+=(((uintptr_t)p % Page)+bytes+Page-1)/Page; }
    DWORD previous;
    Need(VirtualProtect(const_cast<void *>(p), bytes, flags == 3 ? PAGE_READWRITE : PAGE_EXECUTE_READ, &previous) != 0, "VirtualProtect failed");
    return true;
}
static DWORD Protection(const void *p) {
    MEMORY_BASIC_INFORMATION info{};
    Need(VirtualQuery(p, &info, sizeof(info)) != 0, "VirtualQuery failed"); return info.Protect;
}
class Emitter {
protected:
    u8 *position = nullptr;
public:
    void SetCodePointer(u8 *read, u8 *write) { Need(read == write, "Unexpected dual mapping"); position = write; }
    const u8 *GetCodePointer() const { return position; }
    u8 *GetWritableCodePtr() const { return position; }
    void Emit(size_t bytes) {
        // Check before touching a protected page: a failed test must not crash the host.
        for (size_t i = 0; i < bytes; ++i)
            Need(Protection(position + i) == PAGE_READWRITE, "Emitter would write into an RX page");
        memset(position, 0xcc, bytes); position += bytes;
    }
};
class Block : public CodeBlock<Emitter> {
    void PoisonMemory(int offset) override { memset(GetBasePtr() + offset, 0xcc, GetRegionSize() - offset); }
public:
    void CheckFrontier() {
        const size_t end = GetOffset(GetCodePtr());
        for (size_t page = 0; page < Capacity; page += Page)
            Need(Protection(GetBasePtr() + page) == (page < end ? PAGE_EXECUTE_READ : PAGE_READWRITE), "Written prefix/future page permissions incorrect");
    }
};
int main(int argc, char **argv) {
    const bool oldPolicy = argc == 2 && !strcmp(argv[1], "--expect-old-failure");
    try {
        Block block; block.AllocCodeSpace(Capacity);
        block.BeginWrite(3000); block.Emit(3000); block.EndWrite();
        block.BeginWrite(2048); block.Emit(300); block.EndWrite();
        // An earlier overestimate formerly left page 2 RX. This observed 640/980
        // underestimate reopens only page 1, then crosses into that future RX page.
        block.BeginWrite(640); block.Emit(980); block.EndWrite();
        Need(!oldPolicy, "Legacy negative control did not detect the unsafe write");
        block.CheckFrontier();
        block.BeginWrite(1); block.EndWrite(); block.CheckFrontier();
        block.BeginWrite(1); block.ContinueWrite(16000); block.Emit(16); block.EndWrite(); block.CheckFrontier();
        // Vertex compiler failure: rewind over a multi-page RX tail, then retry
        // with a shorter decoder. The complete remaining range must be writable.
        size_t retry = block.GetOffset(block.GetCodePtr());
        block.BeginWrite(block.GetSpaceLeft()); block.Emit(9000); block.EndWrite();
        block.ResetCodePtr(retry);
        block.BeginWrite(block.GetSpaceLeft()); block.Emit(300); block.EndWrite(); block.CheckFrontier();
        block.BeginWrite(32); block.Emit(9000); block.EndWrite(); block.CheckFrontier();
        block.ClearCodeSpace(Page); block.CheckFrontier();
        block.BeginWrite(32); block.EndWrite(); block.CheckFrontier();
        unsigned random = 7;
        for (int i = 0; i < 600; ++i) {
            random = random * 1664525u + 1013904223u;
            size_t actual = 4 + (random % 2048), estimate = 1 + ((random >> 12) % 16000);
            if (block.GetSpaceLeft() < actual) block.ClearCodeSpace(Page);
            block.BeginWrite(estimate); block.Emit(actual); block.EndWrite(); block.CheckFrontier();
        }
        block.BeginWrite(block.GetSpaceLeft()); block.Emit(block.GetSpaceLeft()); block.EndWrite(); block.CheckFrontier();
        block.BeginWrite(1); block.EndWrite(); block.CheckFrontier();
#ifdef TIZENPSP_ARM_JIT
        size_t calls[2]{},pages[2]{};
        for(int append=0;append<2;++append) {
            Block cpu;cpu.AllocCodeSpace(Capacity);
            const size_t startCalls=writableCalls,startPages=writablePages;
            for(int i=0;i<600;++i) {
                // Mostly small blocks, plus underestimated multi-page writes.
                size_t actual=i%41==0?5000:64;
                if(cpu.GetSpaceLeft()<actual)cpu.ClearCodeSpace(Page);
                size_t estimate=i%41==0?1:24000;
                if(append)cpu.BeginWriteAppend(estimate);else cpu.BeginWrite(estimate);
                cpu.Emit(actual);cpu.EndWrite();cpu.CheckFrontier();
                if(i%31==0) {
                    if(append)cpu.BeginWriteAppend(0);else cpu.BeginWrite(1);
                    cpu.EndWrite();cpu.CheckFrontier();
                }
            }
            // Exhaustion, aligned empty write and clearing a partial page.
            if(append)cpu.BeginWriteAppend(1);else cpu.BeginWrite(1);
            cpu.Emit(cpu.GetSpaceLeft());cpu.EndWrite();cpu.CheckFrontier();
            if(append)cpu.BeginWriteAppend(1);else cpu.BeginWrite(1);
            cpu.EndWrite();cpu.CheckFrontier();
            cpu.ClearCodeSpace(Page+16);
            if(append)cpu.BeginWriteAppend(24000);else cpu.BeginWrite(24000);
            cpu.Emit(5000);cpu.EndWrite();cpu.CheckFrontier();
            calls[append]=writableCalls-startCalls;pages[append]=writablePages-startPages;
        }
        Need(pages[1]<pages[0] && calls[1]<calls[0],"append policy did not reduce protection work");
        printf("PASS append workload: RW calls %zu -> %zu; RW page requests %zu -> %zu; identical emitted sizes and RX/RW frontier\n",calls[0],calls[1],pages[0],pages[1]);
#endif
        puts("PASS: actual CodeBlock with OS RW/RX pages, overestimate then underestimate, empty writes, continuation, cache reset, exhaustion and 600 varied blocks.");
        return 0;
    } catch (const std::exception &e) {
        if (oldPolicy && !strcmp(e.what(), "Emitter would write into an RX page")) {
            puts("PASS negative control: original CodeBlock policy would write into an RX page."); return 0;
        }
        fprintf(stderr, "FAIL: %s\n", e.what()); return 1;
    }
}
