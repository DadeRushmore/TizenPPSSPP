// TizenPSP ARM JIT experiment. GPL-2.0.
#include "tizenpsp_jit_diagnostics.h"
#include "tizenpsp_compute_policy.h"
#include "tizenpsp_experiments.h"
#include <atomic>
#include <cstdio>
#include <cstdlib>
#include <cerrno>
#include <fcntl.h>
#include <chrono>
#include <cstring>
#ifndef _WIN32
#include <time.h>
#include <sched.h>
#endif
#ifdef TIZENPSP_ARM_JIT
#include "Core/HW/AvcDecoder.h"
#endif
#ifdef _WIN32
#include <io.h>
#define write _write
#define open _open
#define fsync _commit
#else
#include <unistd.h>
#endif

static const char *invNames[]={"all","clear","range","cacheop","io","module","replace","debug","other"};
struct InvTotals { std::atomic<unsigned> calls{0}, blocks{0}, same{0}, changed{0}, unknown{0}; };
static InvTotals invTotals[9];
static unsigned InvKind(const char *s) {
 if(!s)return 8;
 if(!strcmp(s,"sceKernelIcacheInvalidateAll"))return 0;
 if(!strcmp(s,"sceKernelIcacheClearAll"))return 1;
 if(!strcmp(s,"sceKernelIcacheInvalidateRange"))return 2;
 if(strstr(s,"Int_Cache"))return 3;
 if(strstr(s,"Io") || strstr(s,"IO"))return 4;
 if(strstr(s,"Module") || strstr(s,"Reloc") || strstr(s,"Import") || strstr(s,"Stub"))return 5;
 if(strstr(s,"Replacement") || !strcmp(s,"InvalidateReplacedAddress"))return 6;
 if(strstr(s,"Breakpoint") || strstr(s,"BreakPoint"))return 7;
 return 8;
}
bool TizenInvalidationRecord(const char *source,unsigned blocks,unsigned same,unsigned changed,unsigned unknown) {
 auto &t=invTotals[InvKind(source)];
 unsigned n=t.calls.fetch_add(1,std::memory_order_relaxed)+1;
 t.blocks.fetch_add(blocks,std::memory_order_relaxed);
 t.same.fetch_add(same,std::memory_order_relaxed);
 t.changed.fetch_add(changed,std::memory_order_relaxed);
 t.unknown.fetch_add(unknown,std::memory_order_relaxed);
 return n && (n<=8 || (n&(n-1))==0);
}
static std::atomic<unsigned> retentionRequests{0}, retentionCandidates{0}, retentionRevalidated{0}, retentionRetained{0}, retentionBytes{0};
void TizenRetentionRecord(unsigned candidates, unsigned revalidated, unsigned retained, size_t storedBytes) {
 retentionRequests.fetch_add(1, std::memory_order_relaxed);
 retentionCandidates.fetch_add(candidates, std::memory_order_relaxed);
 retentionRevalidated.fetch_add(revalidated, std::memory_order_relaxed);
 retentionRetained.fetch_add(retained, std::memory_order_relaxed);
 retentionBytes.store((unsigned)storedBytes, std::memory_order_relaxed);
}
static int traceFd = -1;
static std::atomic<unsigned> skippedInvalidationRewrites{0};
void TizenJitInvalidationSkipped() { skippedInvalidationRewrites.fetch_add(1, std::memory_order_relaxed); }
static std::atomic<unsigned> framebufferTransfers[(int)TizenFramebufferEvent::Count]{};
void TizenFramebufferTransfer(TizenFramebufferEvent event) {
	unsigned index = (unsigned)event;
	if (index < (unsigned)TizenFramebufferEvent::Count)
		framebufferTransfers[index].fetch_add(1, std::memory_order_relaxed);
}
static std::atomic<unsigned> uniformUploaded{0}, uniformSkipped{0};
void TizenUniformUpdate(bool uploaded) {
	(uploaded ? uniformUploaded : uniformSkipped).fetch_add(1, std::memory_order_relaxed);
}
static std::atomic<unsigned> commandRequested{0}, commandQueued{0}, commandSkipped{0};
extern "C" unsigned tizenpsp_command_queue_status(){return commandRequested.load(std::memory_order_relaxed)|2;}
extern "C" unsigned tizenpsp_set_command_queue(unsigned value){if(value>1)return 0xFFFFFFFFU;commandRequested.store(value,std::memory_order_relaxed);return tizenpsp_command_queue_status();}
void TizenCommandRecord(bool queued){(queued?commandQueued:commandSkipped).fetch_add(1,std::memory_order_relaxed);}
static std::atomic<unsigned> vertexBindingTotals[5]{};
static std::atomic<unsigned> vertexBindingRequested{1}, vertexBindingSupported{0};
extern "C" unsigned tizenpsp_vertex_binding_status() {
 return vertexBindingRequested.load(std::memory_order_relaxed) | (vertexBindingSupported.load(std::memory_order_relaxed)<<1);
}
extern "C" unsigned tizenpsp_set_vertex_binding(unsigned enabled) {
 if(enabled>1)return 0xFFFFFFFFU;
 vertexBindingRequested.store(enabled,std::memory_order_relaxed);
 return tizenpsp_vertex_binding_status();
}
void TizenVertexBindingCapability(bool supported) {vertexBindingSupported.store(supported?1:0,std::memory_order_relaxed);}
void TizenVertexBindingRecord(unsigned fast,unsigned fallback,unsigned formats,unsigned bindings,unsigned legacyAttributes) {
 const unsigned values[]={fast,fallback,formats,bindings,legacyAttributes};
 for(int i=0;i<5;i++)vertexBindingTotals[i].fetch_add(values[i],std::memory_order_relaxed);
}
static std::atomic<unsigned> events{0}, blocks{0}, codeBytes{0}, protects{0}, failures{0};
static std::atomic<unsigned> enters{0}, returns{0}, lastPC{0}, logErrors{0};
static std::atomic<unsigned> writeEstimateOverruns{0}, largestOverrunBlock{0};
static std::atomic<unsigned> linkedExits{0}, dispatcherExits{0}, cacheClears{0}, invalidatedBlocks{0};
static std::atomic<unsigned> cacheUsed{0}, cacheCapacity{0}, fastMemory{0};
static std::atomic<unsigned> computeThreads{0}, vertexCompiled{0}, vertexCompileFailed{0}, vertexCodeBytes{0};
static std::atomic<unsigned> vertexBatches{0}, vertexCounts[3];
struct PerfTotals { std::atomic<unsigned> count{0}, wall{0}, cpu{0}; };
static PerfTotals perf[(int)TizenPerfPhase::Count];
static std::atomic<unsigned> perfClockErrors{0};
struct ProfileTotals {
	std::atomic<unsigned> calls{0}, samples{0}, wall{0}, cpu{0};
};
static ProfileTotals profile[(int)TizenProfilePhase::Count];
#if TIZENPSP_DETAILED_PROFILE == 2
static constexpr unsigned profileDivisors[] = {32, 256, 16, 256, 16, 16, 16, 1, 256, 256, 256, 16, 256};
#else
static constexpr unsigned profileDivisors[] = {1, 64, 16, 64, 1, 8, 8, 1, 64, 64, 64, 16, 256};
#endif
static const char *profileNames[] = {"ge", "draw_submit", "texture_build", "software_transform", "gl_init", "gl_pass", "gl_copy", "gl_shader", "gl_draw", "gl_uniform", "gl_state", "gl_upload", "hle"};
static_assert(sizeof(profileDivisors) / sizeof(profileDivisors[0]) == (int)TizenProfilePhase::Count);
static_assert(sizeof(profileNames) / sizeof(profileNames[0]) == (int)TizenProfilePhase::Count);
struct SchedulerTotals {
	std::atomic<unsigned> reads{0}, errors{0}, cpu{0}, wait{0}, slices{0}, stamp{0};
};
static SchedulerTotals scheduler[2];

static uint64_t WallMicros() {
	return (uint64_t)std::chrono::duration_cast<std::chrono::microseconds>(
		std::chrono::steady_clock::now().time_since_epoch()).count();
}
static uint64_t ThreadMicros() {
#ifdef _WIN32
	return 0; // Host counter tests do not claim per-thread measurements.
#else
	struct timespec stamp;
	if (clock_gettime(CLOCK_THREAD_CPUTIME_ID, &stamp) != 0) {
		perfClockErrors.fetch_add(1, std::memory_order_relaxed);
		return 0;
	}
	return (uint64_t)stamp.tv_sec * 1000000 + stamp.tv_nsec / 1000;
#endif
}
int TizenConfigureComputeThreads(int reported) {
	unsigned char mask[128]{};
#ifndef _WIN32
	// libc's cpu_set_t is 1024 bits on this ARM32 target. Use its declared type.
	cpu_set_t allowed{};
	static_assert(sizeof(allowed) == sizeof(mask));
	int error = sched_getaffinity(0, sizeof(allowed), &allowed) == 0 ? 0 : errno;
	if (!error) memcpy(mask, &allowed, sizeof(mask));
	else TizenJitEvent("compute_affinity_unavailable", 0, 0, reported, error);
#endif
	int count = TizenComputeThreadLimit(reported, mask, sizeof(mask));
	computeThreads.store(count, std::memory_order_relaxed);
	TizenJitEvent("compute_thread_limit", 0, count, reported, 0);
	return count;
}
#if TIZENPSP_DETAILED_PROFILE
TizenProfileScope::TizenProfileScope(TizenProfilePhase phase) : phase_(phase) {
	auto &p = profile[(int)phase];
	unsigned sequence = p.calls.fetch_add(1, std::memory_order_relaxed);
	unsigned divisor = profileDivisors[(int)phase];
	sampled_ = divisor == 1 || sequence * 2654435761U <= 0xFFFFFFFFU / divisor;
	if (sampled_) { wall_ = WallMicros(); cpu_ = ThreadMicros(); }
}
TizenProfileScope::~TizenProfileScope() {
	if (!sampled_) return;
	uint64_t cpu = ThreadMicros(), wall = WallMicros();
	auto &p = profile[(int)phase_];
	p.wall.fetch_add((unsigned)(wall-wall_), std::memory_order_relaxed);
	p.cpu.fetch_add(cpu >= cpu_ ? (unsigned)(cpu-cpu_) : 0, std::memory_order_relaxed);
	p.samples.fetch_add(1, std::memory_order_relaxed);
}
#endif
bool TizenParseScheduler(const char *text, uint64_t values[3]) {
	if (!text || !values || strchr(text, '-')) return false;
	unsigned long long cpu, wait, slices;
	char extra;
	if (sscanf(text, "%llu %llu %llu %c", &cpu, &wait, &slices, &extra) != 3) return false;
	values[0] = cpu; values[1] = wait; values[2] = slices;
	return true;
}
static void UpdateScheduler(TizenPerfPhase phase) {
#ifndef _WIN32
	if (phase != TizenPerfPhase::Emu && phase != TizenPerfPhase::Render) return;
	if ((perf[(int)phase].count.load(std::memory_order_relaxed) & 63) != 1) return;
	auto &s = scheduler[phase == TizenPerfPhase::Emu ? 0 : 1];
	FILE *file = fopen("/proc/thread-self/schedstat", "r");
	char text[192]{};
	bool read = file && fgets(text, sizeof(text), file);
	if (file) fclose(file);
	uint64_t values[3];
	if (!read || !TizenParseScheduler(text, values)) { s.errors.fetch_add(1, std::memory_order_relaxed); return; }
	s.cpu.store((unsigned)(values[0] / 1000), std::memory_order_relaxed);
	s.wait.store((unsigned)(values[1] / 1000), std::memory_order_relaxed);
	s.slices.store((unsigned)values[2], std::memory_order_relaxed);
	s.stamp.store((unsigned)WallMicros(), std::memory_order_relaxed);
	s.reads.fetch_add(1, std::memory_order_relaxed);
#else
	(void)phase;
#endif
}
void TizenVertexCompiled(size_t bytes, bool success) {
	(success ? vertexCompiled : vertexCompileFailed).fetch_add(1, std::memory_order_relaxed);
	if (success) vertexCodeBytes.fetch_add((unsigned)bytes, std::memory_order_relaxed);
}
TizenVertexScope::TizenVertexScope(int vertices, int kind) : sampled_(false) {
	if (vertices <= 0 || kind < 0 || kind > 2) return;
	unsigned sequence = vertexBatches.fetch_add(1, std::memory_order_relaxed);
	vertexCounts[kind].fetch_add((unsigned)vertices, std::memory_order_relaxed);
	// Multiplicative hashing avoids sampling the same draw number in every frame.
	sampled_ = ((sequence * 2654435761U) >> 26) == 0;
	if (sampled_) { wall_ = WallMicros(); cpu_ = ThreadMicros(); }
}
TizenVertexScope::~TizenVertexScope() {
	if (!sampled_) return;
	uint64_t cpu = ThreadMicros(), wall = WallMicros();
	TizenPerfRecord(TizenPerfPhase::VertexDecode, (uint32_t)(wall-wall_), cpu >= cpu_ ? (uint32_t)(cpu-cpu_) : 0);
}
static void ThreadAffinity(TizenPerfPhase phase) {
#ifndef _WIN32
	if (phase != TizenPerfPhase::Emu && phase != TizenPerfPhase::Render) return;
	if (perf[(int)phase].count.load(std::memory_order_relaxed) != 0) return;
	FILE *file = fopen("/proc/thread-self/status", "r");
	if (!file) {
		TizenJitEvent("thread_affinity_unavailable", 0, 0, (int)phase, errno);
		return;
	}
	char line[256];
	while (fgets(line, sizeof(line), file)) {
		if (strncmp(line, "Cpus_allowed_list:", 18) == 0) {
			char *value = line + 18;
			while (*value == ' ' || *value == '\t') ++value;
			value[strcspn(value, "\r\n")] = 0;
			char stage[128];
			snprintf(stage, sizeof(stage), "thread_%s_cpus_%.80s", phase == TizenPerfPhase::Emu ? "emu" : "render", value);
			TizenJitEvent(stage, 0, 0, 0, 0);
			break;
		}
	}
	fclose(file);
#else
	(void)phase;
#endif
}
TizenPerfScope::TizenPerfScope(TizenPerfPhase phase) : phase_(phase), wall_(WallMicros()), cpu_(ThreadMicros()) { ThreadAffinity(phase); }
TizenPerfScope::~TizenPerfScope() {
	uint64_t cpu = ThreadMicros(), wall = WallMicros();
	TizenPerfRecord(phase_, (uint32_t)(wall - wall_), cpu >= cpu_ ? (uint32_t)(cpu - cpu_) : 0);
	UpdateScheduler(phase_);
}
void TizenPerfRecord(TizenPerfPhase phase, uint32_t wallUs, uint32_t cpuUs) {
	auto &p = perf[(int)phase];
	p.wall.fetch_add(wallUs, std::memory_order_relaxed);
	p.cpu.fetch_add(cpuUs, std::memory_order_relaxed);
	p.count.fetch_add(1, std::memory_order_relaxed);
}
void TizenJitExit(bool linked) { (linked ? linkedExits : dispatcherExits).fetch_add(1, std::memory_order_relaxed); }
void TizenJitCacheClear() { cacheClears.fetch_add(1, std::memory_order_relaxed); }
void TizenJitInvalidation() { invalidatedBlocks.fetch_add(1, std::memory_order_relaxed); }
void TizenJitCacheUsage(size_t used, size_t capacity) {
	cacheUsed.store((unsigned)used, std::memory_order_relaxed);
	cacheCapacity.store((unsigned)capacity, std::memory_order_relaxed);
}
void TizenJitFastMemory(bool enabled) { fastMemory.store(enabled ? 1 : 0, std::memory_order_relaxed); }

void TizenJitEvent(const char *stage, uintptr_t address, size_t bytes, int flags, int error) {
	unsigned event = events.fetch_add(1) + 1;
	if (traceFd < 0 || (event > 4096 && !error))
		return;
	char line[320];
	int n = snprintf(line, sizeof(line), "JIT_EVENT seq=%u stage=%s address=0x%zx bytes=%zu flags=%d errno=%d blocks=%u enters=%u returns=%u\n",
		event, stage, (size_t)address, bytes, flags, error, blocks.load(), enters.load(), returns.load());
	if (n <= 0)
		return;
	if (n >= (int)sizeof(line))
		n = sizeof(line) - 1;
	if (write(traceFd, line, n) != n)
		logErrors.fetch_add(1);
	if (error || event <= 4) {
		if (fsync(traceFd) != 0)
			logErrors.fetch_add(1);
	}
}

void TizenJitProtection(const void *address, size_t bytes, int flags, int error) {
	unsigned count = protects.fetch_add(1) + 1;
	if (error || count <= 16 || count % 4096 == 0)
		TizenJitEvent("mprotect", (uintptr_t)address, bytes, flags, error);
}

[[noreturn]] void TizenJitFatal(const char *stage, const void *address, size_t bytes, int flags, int error) {
	failures.fetch_add(1);
	TizenJitEvent(stage, (uintptr_t)address, bytes, flags, error ? error : EINVAL);
	// Stop before writing/executing an invalid mapping; next app launch defaults to IR.
	std::abort();
}

void TizenJitCompiled(uint32_t pc, size_t bytes) {
	lastPC.store(pc);
	codeBytes.fetch_add((unsigned)bytes);
	unsigned count = blocks.fetch_add(1) + 1;
	if (count <= 8 || count % 1024 == 0)
		TizenJitEvent("block_compiled", pc, bytes, 0, 0);
}

void TizenJitWriteEstimate(size_t actualBytes) {
	unsigned count = writeEstimateOverruns.fetch_add(1) + 1;
	unsigned maximum = largestOverrunBlock.load();
	while (maximum < actualBytes && !largestOverrunBlock.compare_exchange_weak(maximum, (unsigned)actualBytes)) {}
	if (count <= 4 || count % 1024 == 0)
		TizenJitEvent("write_estimate_exceeded", 0, actualBytes, 0, 0);
}

void TizenJitDispatch(bool entering, uint32_t pc) {
	lastPC.store(pc);
	unsigned count = (entering ? enters : returns).fetch_add(1) + 1;
	if (count <= 4 || count % 600 == 0)
		TizenJitEvent(entering ? "dispatch_enter" : "dispatch_return", pc, 0, 0, 0);
}

extern "C" int tizenpsp_jit_init(const char *path) {
	if (traceFd >= 0 || !path)
		return EINVAL;
	traceFd = open(path, O_WRONLY | O_CREAT | O_TRUNC | O_APPEND, 0600);
	if (traceFd < 0)
		return errno;
	TizenJitEvent("diagnostics_ready_wx_cpu_only", 0, 0, 0, 0);
	return logErrors.load() ? EIO : 0;
}

extern "C" unsigned tizenpsp_jit_snapshot(char *buffer, unsigned size) {
	if (!buffer || !size)
		return 0;
#ifdef TIZENPSP_ARM_JIT
	// USE_FFMPEG is private to Core's target. Query its availability function
	// rather than reporting the libretro wrapper's unrelated compile definitions.
	const char *codecs = AvcDecoder::IsAvailable() ? "ffmpeg=1" : "ffmpeg=0";
#else
	const char *codecs = "ffmpeg=0";
#endif
	int n = snprintf(buffer, size, "JIT_NATIVE backend=ARM_IR wx=1 vertex_jit=1 block_link=1 link_mode=existing %s compiled_blocks=%u generated_bytes=%u dispatch_enter=%u dispatch_return=%u last_psp_pc=0x%08x protections=%u failures=%u log_errors=%u trace_events=%u write_estimate_overruns=%u largest_overrun_block_bytes=%u linked_exits=%u dispatcher_exits=%u cache_clears=%u invalidated_blocks=%u emu_samples=%u emu_wall_us=%u emu_cpu_us=%u render_samples=%u render_wall_us=%u render_cpu_us=%u compile_samples=%u compile_wall_us=%u compile_cpu_us=%u perf_clock_errors=%u", codecs,
		blocks.load(), codeBytes.load(), enters.load(), returns.load(), lastPC.load(), protects.load(), failures.load(), logErrors.load(), events.load(), writeEstimateOverruns.load(), largestOverrunBlock.load(),
		linkedExits.load(), dispatcherExits.load(), cacheClears.load(), invalidatedBlocks.load(),
		perf[0].count.load(), perf[0].wall.load(), perf[0].cpu.load(),
		perf[1].count.load(), perf[1].wall.load(), perf[1].cpu.load(),
		perf[2].count.load(), perf[2].wall.load(), perf[2].cpu.load(), perfClockErrors.load());
	if (n < 0) return 0;
	unsigned used = (unsigned)n < size ? (unsigned)n : size - 1;
	const char *names[] = { "emu", "render", "compile", "dispatch", "frame_wait", "queue_wait", "sync_wait", "gl_work", "readback", "vertex_decode", "invalidation_probe", "ir_retention", "host_run", "present", "audio_output" };
	static_assert(sizeof(names) / sizeof(names[0]) == (int)TizenPerfPhase::Count);
	for (int phase = 3; phase < (int)TizenPerfPhase::Count && used < size - 1; ++phase) {
		auto &p = perf[phase];
		n = snprintf(buffer + used, size - used, " %s_samples=%u %s_wall_us=%u %s_cpu_us=%u", names[phase],
			p.count.load(), names[phase], p.wall.load(), names[phase], p.cpu.load());
		if (n < 0) return used;
		used += (unsigned)n < size - used ? (unsigned)n : size - used - 1;
	}
	if (used < size - 1) {
		n = snprintf(buffer + used, size - used, " cache_used_bytes=%u cache_capacity_bytes=%u memory_fast=%u",
			cacheUsed.load(), cacheCapacity.load(), fastMemory.load());
		if (n > 0) used += (unsigned)n < size - used ? (unsigned)n : size - used - 1;
	}
	if (used < size - 1) {
		n = snprintf(buffer + used, size - used, " compute_threads=%u vertex_compiled=%u vertex_compile_failed=%u vertex_code_bytes=%u vertex_batches=%u vertex_scalar=%u vertex_simd=%u vertex_generated=%u vertex_timing_sample_divisor=64",
			computeThreads.load(), vertexCompiled.load(), vertexCompileFailed.load(), vertexCodeBytes.load(), vertexBatches.load(),
			vertexCounts[0].load(), vertexCounts[1].load(), vertexCounts[2].load());
		if (n > 0) used += (unsigned)n < size - used ? (unsigned)n : size - used - 1;
	}
	for (int i = 0; TIZENPSP_DETAILED_PROFILE && i < (int)TizenProfilePhase::Count && used < size - 1; ++i) {
		auto &p = profile[i]; const char *name = profileNames[i];
		n = snprintf(buffer + used, size - used, " prof_%s_calls=%u prof_%s_samples=%u prof_%s_wall_us=%u prof_%s_cpu_us=%u prof_%s_divisor=%u",
			name, p.calls.load(), name, p.samples.load(), name, p.wall.load(), name, p.cpu.load(), name, profileDivisors[i]);
		if (n < 0) return used;
		used += (unsigned)n < size - used ? (unsigned)n : size - used - 1;
	}
	for (int i = 0; i < 2 && used < size - 1; ++i) {
		auto &s = scheduler[i]; const char *name = i == 0 ? "emu" : "render";
		n = snprintf(buffer + used, size - used, " sched_%s_reads=%u sched_%s_errors=%u sched_%s_cpu_us=%u sched_%s_wait_us=%u sched_%s_slices=%u sched_%s_stamp_us=%u",
			name, s.reads.load(), name, s.errors.load(), name, s.cpu.load(), name, s.wait.load(), name, s.slices.load(), name, s.stamp.load());
		if (n < 0) return used;
		used += (unsigned)n < size - used ? (unsigned)n : size - used - 1;
	}
	unsigned processCpu = 0, processClockOk = 0;
	long clockTicks = 0;
#ifndef _WIN32
	clockTicks = sysconf(_SC_CLK_TCK);
	struct timespec processStamp;
	if (clock_gettime(CLOCK_PROCESS_CPUTIME_ID, &processStamp) == 0) {
		processCpu = (unsigned)((uint64_t)processStamp.tv_sec * 1000000 + processStamp.tv_nsec / 1000);
		processClockOk = 1;
	}
#endif
	if (used < size - 1) {
		n = snprintf(buffer + used, size - used, " process_cpu_us=%u process_cpu_valid=%u profile_enabled=%u uniform_uploaded=%u uniform_skipped=%u process_clock_hz=%ld invalidation_skipped_rewrites=%u", processCpu, processClockOk, TIZENPSP_DETAILED_PROFILE, uniformUploaded.load(), uniformSkipped.load(), clockTicks, skippedInvalidationRewrites.load());
		if (n > 0) used += (unsigned)n < size - used ? (unsigned)n : size - used - 1;
	}
	const char *transferNames[] = { "uploads", "cached_uploads", "blocking_readbacks", "nonblocking_readbacks", "depth_copies" };
	static_assert(sizeof(transferNames) / sizeof(transferNames[0]) == (int)TizenFramebufferEvent::Count);
	for (int i = 0; i < (int)TizenFramebufferEvent::Count && used < size - 1; ++i) {
		n = snprintf(buffer + used, size - used, " fb_%s=%u", transferNames[i], framebufferTransfers[i].load(std::memory_order_relaxed));
		if (n < 0) return used;
		used += (unsigned)n < size - used ? (unsigned)n : size - used - 1;
	}
	for (unsigned i=0;i<9 && used<size-1;++i) {
		auto &t=invTotals[i];
		n=snprintf(buffer+used,size-used," inv_%s_calls=%u inv_%s_blocks=%u inv_%s_same=%u inv_%s_changed=%u inv_%s_unknown=%u",invNames[i],t.calls.load(),invNames[i],t.blocks.load(),invNames[i],t.same.load(),invNames[i],t.changed.load(),invNames[i],t.unknown.load());
		if(n<0)return used;
		used+=(unsigned)n<size-used?(unsigned)n:size-used-1;
	}
	if (used < size - 1) {
		n = snprintf(buffer + used, size - used, " retention_requests=%u retention_candidates=%u retention_revalidated=%u retention_retained=%u retention_last_stored_bytes=%u", retentionRequests.load(), retentionCandidates.load(), retentionRevalidated.load(), retentionRetained.load(), retentionBytes.load());
		if (n > 0) used += (unsigned)n < size - used ? (unsigned)n : size - used - 1;
	}
	if (used < size - 1) {
		n = snprintf(buffer + used, size - used, " vertex_binding_fast_draws=%u vertex_binding_fallback_draws=%u vertex_binding_format_calls=%u vertex_binding_buffer_calls=%u vertex_binding_legacy_attributes=%u", vertexBindingTotals[0].load(),vertexBindingTotals[1].load(),vertexBindingTotals[2].load(),vertexBindingTotals[3].load(),vertexBindingTotals[4].load());
		if (n > 0) used += (unsigned)n < size - used ? (unsigned)n : size - used - 1;
	}
	if (used < size - 1) {
		n=snprintf(buffer+used,size-used," vertex_binding_requested=%u vertex_binding_supported=%u command_queue_requested=%u command_queue_supported=1 command_queue_queued=%u command_queue_skipped=%u",vertexBindingRequested.load(),vertexBindingSupported.load(),commandRequested.load(),commandQueued.load(),commandSkipped.load());
		if(n>0)used+=(unsigned)n<size-used?(unsigned)n:size-used-1;
	}
	return used;
}
