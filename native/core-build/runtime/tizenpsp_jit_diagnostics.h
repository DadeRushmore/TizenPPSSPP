// TizenPSP ARM JIT experiment. GPL-2.0.
#pragma once
#include <stddef.h>
#include <stdint.h>
void TizenJitEvent(const char *stage, uintptr_t address, size_t bytes, int flags, int error);
void TizenJitProtection(const void *address, size_t bytes, int flags, int error);
void TizenJitCompiled(uint32_t pc, size_t bytes);
void TizenJitDispatch(bool entering, uint32_t pc);
void TizenJitWriteEstimate(size_t actualBytes);
void TizenJitExit(bool linked);
void TizenJitCacheClear();
void TizenJitInvalidation();
void TizenJitInvalidationSkipped();
void TizenRetentionRecord(unsigned candidates, unsigned revalidated, unsigned retained, size_t storedBytes);
void TizenJitCacheUsage(size_t used, size_t capacity);
void TizenJitFastMemory(bool enabled);
int TizenConfigureComputeThreads(int reported);
void TizenVertexCompiled(size_t bytes, bool success);
enum class TizenPerfPhase { Emu, Render, Compile, Dispatch, FrameWait, QueueWait, SyncWait, GLWork, Readback, VertexDecode, InvalidationProbe, IRRetention, HostRun, Present, AudioOutput, Count };
#ifndef TIZENPSP_DETAILED_PROFILE
#define TIZENPSP_DETAILED_PROFILE 0
#endif
enum class TizenProfilePhase { GE, DrawSubmit, TextureBuild, SoftwareTransform, GLInit, GLPass, GLCopy, GLShader, GLDraw, GLUniform, GLState, GLUpload, HLE, Count };
class TizenProfileScope {
public:
#if TIZENPSP_DETAILED_PROFILE
	explicit TizenProfileScope(TizenProfilePhase phase);
	~TizenProfileScope();
private:
	TizenProfilePhase phase_;
	bool sampled_;
	uint64_t wall_ = 0, cpu_ = 0;
#else
	explicit TizenProfileScope(TizenProfilePhase) {}
#endif
};
bool TizenParseScheduler(const char *text, uint64_t values[3]);
// Count every batch; time a sparse deterministic sample to limit hot-path overhead.
class TizenVertexScope {
public:
	TizenVertexScope(int vertices, int kind); // 0 = scalar, 1 = handwritten SIMD, 2 = generated.
	~TizenVertexScope();
private:
	bool sampled_;
	uint64_t wall_ = 0, cpu_ = 0;
};
// Totals are unsigned microseconds, modulo 2^32; compare snapshots with unsigned deltas.
void TizenPerfRecord(TizenPerfPhase phase, uint32_t wallUs, uint32_t cpuUs);
class TizenPerfScope {
public:
	explicit TizenPerfScope(TizenPerfPhase phase);
	~TizenPerfScope();
private:
	TizenPerfPhase phase_;
	uint64_t wall_, cpu_;
};
[[noreturn]] void TizenJitFatal(const char *stage, const void *address, size_t bytes, int flags, int error);
extern "C" int tizenpsp_jit_init(const char *path);
extern "C" unsigned tizenpsp_jit_snapshot(char *buffer, unsigned size);

void TizenUniformUpdate(bool uploaded);
void TizenVertexBindingRecord(unsigned fast,unsigned fallback,unsigned formats,unsigned bindings,unsigned legacyAttributes);
// Cumulative logical framebuffer operations; unsigned modulo 2^32, no timings.
// Mirrors GPUStatsPerFrame semantics without reading its non-atomic fields.
enum class TizenFramebufferEvent { Upload, CachedUpload, BlockingReadback, NonblockingReadback, DepthCopy, Count };
void TizenFramebufferTransfer(TizenFramebufferEvent event);


// Cumulative range invalidation attribution; returns true for a bounded log sample.
bool TizenInvalidationRecord(const char *source,unsigned blocks,unsigned same,unsigned changed,unsigned unknown);
