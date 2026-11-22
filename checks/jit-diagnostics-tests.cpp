#undef NDEBUG
#include <cassert>
#include <cstdio>
#include <cstring>
#include "../native/core-build/runtime/tizenpsp_jit_diagnostics.h"
#include "../native/core-build/runtime/tizenpsp_compute_policy.h"
#include "../native/core-build/runtime/tizenpsp_experiments.h"
int main(int argc, char **argv) {
	assert(argc == 2);
	assert(tizenpsp_vertex_binding_status()==1);
	TizenVertexBindingCapability(true);assert(tizenpsp_vertex_binding_status()==3);
	assert(tizenpsp_set_vertex_binding(0)==2);
	assert(tizenpsp_set_vertex_binding(99)==0xFFFFFFFFU && tizenpsp_vertex_binding_status()==2);
	assert(tizenpsp_set_vertex_binding(1)==3);
	TizenVertexBindingCapability(false);assert(tizenpsp_vertex_binding_status()==1);
	assert(tizenpsp_jit_init(argv[1]) == 0);
	assert(tizenpsp_jit_init(argv[1]) != 0);
	TizenJitCompiled(0x08800000, 96);
	TizenJitDispatch(true, 0x08800000);
	TizenJitDispatch(false, 0x08800010);
	TizenJitWriteEstimate(980);TizenJitWriteEstimate(6760);TizenJitWriteEstimate(344);
	TizenJitExit(true);TizenJitExit(true);TizenJitExit(false);
	TizenJitCacheClear();TizenJitInvalidation();
	TizenPerfRecord(TizenPerfPhase::Emu, 1000, 750);
	TizenPerfRecord(TizenPerfPhase::Emu, 900, 600);
	TizenPerfRecord(TizenPerfPhase::Render, 2000, 100);
	TizenPerfRecord(TizenPerfPhase::Compile, 0xFFFFFFFFU, 10);
	TizenPerfRecord(TizenPerfPhase::Compile, 2, 20);
	TizenJitProtection((void *)0x1000, 4096, 3, 0);
	TizenJitProtection((void *)0x1000, 4096, 5, 13);
	TizenJitCacheUsage(123456, 32 * 1024 * 1024 - 64 * 1024);
	TizenJitFastMemory(true);
	TizenPerfRecord(TizenPerfPhase::SyncWait, 5000, 50);
	TizenPerfRecord(TizenPerfPhase::Readback, 3000, 100);
	unsigned char mask[128]{};
	mask[0] = 8; assert(TizenComputeThreadLimit(4, mask, sizeof(mask)) == 1);
	mask[0] = 15; assert(TizenComputeThreadLimit(4, mask, sizeof(mask)) == 4);
	assert(TizenComputeThreadLimit(2, mask, sizeof(mask)) == 2);
	mask[0] = 0; mask[15] = 128; assert(TizenComputeThreadLimit(4, mask, sizeof(mask)) == 1);
	memset(mask, 0, sizeof(mask)); assert(TizenComputeThreadLimit(4, mask, sizeof(mask)) == 4);
	assert(TizenComputeThreadLimit(0, mask, sizeof(mask)) == 1);
	assert(TizenComputeThreadLimit(256, mask, sizeof(mask)) == 32);
	TizenVertexCompiled(256, true);TizenVertexCompiled(0, false);
	for (int i = 0; i < 4096; ++i) { TizenVertexScope scope(3, i % 3); }
	{ TizenVertexScope empty(0, 0); }
	uint64_t scheduler[3]{};
	assert(TizenParseScheduler("12345678900 7890000 456\n", scheduler));
	assert(scheduler[0] == 12345678900ULL && scheduler[1] == 7890000 && scheduler[2] == 456);
	assert(!TizenParseScheduler("1 2", scheduler));
	assert(!TizenParseScheduler("-1 2 3", scheduler));
	assert(!TizenParseScheduler("1 2 3 garbage", scheduler));
	assert(!TizenParseScheduler(nullptr, scheduler));
	for (int i = 0; i < 4096; ++i) {
		TizenProfileScope ge(TizenProfilePhase::GE);
		TizenProfileScope draw(TizenProfilePhase::GLDraw);
		TizenProfileScope upload(TizenProfilePhase::GLUpload);
		TizenProfileScope hle(TizenProfilePhase::HLE);
	}
	assert(TizenInvalidationRecord("sceKernelIcacheInvalidateAll",3,1,1,1));
	assert(TizenInvalidationRecord("sceIoRead",2,1,0,1));
	assert(TizenInvalidationRecord(nullptr,1,0,1,0));
	assert(TizenInvalidationRecord("InvalidateReplacedAddress",1,1,0,0));
	for(unsigned n=2;n<=32;++n) {
		bool log=TizenInvalidationRecord("sceKernelIcacheInvalidateAll",0,0,0,0);
		assert(log==(n<=8 || (n&(n-1))==0));
	}
	TizenRetentionRecord(100,90,80,1024);TizenRetentionRecord(10,9,8,2048);
	TizenPerfRecord(TizenPerfPhase::IRRetention,123,45);
	TizenVertexBindingRecord(10,2,8,10,40);TizenVertexBindingRecord(3,1,4,3,12);
	TizenPerfRecord(TizenPerfPhase::HostRun,100,80);
	TizenPerfRecord(TizenPerfPhase::Present,30,20);
	TizenPerfRecord(TizenPerfPhase::AudioOutput,7,3);
	char text[16384];unsigned size = tizenpsp_jit_snapshot(text, sizeof(text));
	assert(strstr(text,"vertex_binding_fast_draws=13 vertex_binding_fallback_draws=3 vertex_binding_format_calls=12 vertex_binding_buffer_calls=13 vertex_binding_legacy_attributes=52"));
	assert(strstr(text,"host_run_samples=1 host_run_wall_us=100 host_run_cpu_us=80"));
	assert(strstr(text,"present_samples=1 present_wall_us=30 present_cpu_us=20"));
	assert(strstr(text,"audio_output_samples=1 audio_output_wall_us=7 audio_output_cpu_us=3"));
	assert(strstr(text, "fb_uploads=0 fb_cached_uploads=0 fb_blocking_readbacks=0 fb_nonblocking_readbacks=0 fb_depth_copies=0"));
	for (int kind = 0; kind < (int)TizenFramebufferEvent::Count; ++kind)
		for (int count = 0; count <= kind; ++count)
			TizenFramebufferTransfer((TizenFramebufferEvent)kind);
	TizenFramebufferTransfer(TizenFramebufferEvent::Count); // Reject invalid callers without an out-of-bounds write.
	TizenFramebufferTransfer((TizenFramebufferEvent)-1);
	size = tizenpsp_jit_snapshot(text, sizeof(text));
	assert(strstr(text, "fb_uploads=1 fb_cached_uploads=2 fb_blocking_readbacks=3 fb_nonblocking_readbacks=4 fb_depth_copies=5"));
	// Reading a snapshot must not reset cumulative counts.
	size = tizenpsp_jit_snapshot(text, sizeof(text));
	assert(strstr(text, "fb_uploads=1 fb_cached_uploads=2 fb_blocking_readbacks=3 fb_nonblocking_readbacks=4 fb_depth_copies=5"));
	assert(strstr(text,"inv_all_calls=32 inv_all_blocks=3 inv_all_same=1 inv_all_changed=1 inv_all_unknown=1"));
	assert(strstr(text,"inv_io_calls=1 inv_io_blocks=2 inv_io_same=1 inv_io_changed=0 inv_io_unknown=1"));
	assert(strstr(text,"inv_other_calls=1 inv_other_blocks=1 inv_other_same=0 inv_other_changed=1 inv_other_unknown=0"));
	assert(strstr(text,"inv_replace_calls=1 inv_replace_blocks=1 inv_replace_same=1 inv_replace_changed=0 inv_replace_unknown=0"));
	assert(strstr(text,"retention_requests=2 retention_candidates=110 retention_revalidated=99 retention_retained=88 retention_last_stored_bytes=2048"));
	assert(strstr(text,"ir_retention_samples=1 ir_retention_wall_us=123 ir_retention_cpu_us=45"));
	assert(size == strlen(text));
	assert(size < sizeof(text)-1);
#if TIZENPSP_DETAILED_PROFILE == 2
	assert(strstr(text,"profile_enabled=2"));
	unsigned geSamples=0,drawSamples=0,hleSamples=0;
	assert(sscanf(strstr(text,"prof_ge_samples="),"prof_ge_samples=%u",&geSamples)==1);
	assert(sscanf(strstr(text,"prof_gl_draw_samples="),"prof_gl_draw_samples=%u",&drawSamples)==1);
	assert(sscanf(strstr(text,"prof_hle_samples="),"prof_hle_samples=%u",&hleSamples)==1);
	assert(geSamples>=126 && geSamples<=130 && drawSamples>=14 && drawSamples<=18 && hleSamples>=14 && hleSamples<=18);
	assert(strstr(text,"prof_gl_draw_divisor=256") && strstr(text,"prof_hle_divisor=256"));
#elif TIZENPSP_DETAILED_PROFILE == 1
	assert(strstr(text, "profile_enabled=1"));
	assert(strstr(text, "prof_ge_calls=4096 prof_ge_samples=4096"));
	unsigned drawSamples=0, uploadSamples=0;
	assert(sscanf(strstr(text, "prof_gl_draw_samples="), "prof_gl_draw_samples=%u", &drawSamples)==1);
	assert(sscanf(strstr(text, "prof_gl_upload_samples="), "prof_gl_upload_samples=%u", &uploadSamples)==1);
	assert(drawSamples>=60 && drawSamples<=68 && uploadSamples>=250 && uploadSamples<=262);
	assert(strstr(text, "prof_gl_draw_divisor=64") && strstr(text, "prof_gl_upload_divisor=16"));
#else
	assert(strstr(text, "profile_enabled=0") && !strstr(text, "prof_ge_calls="));
#endif
	TizenJitInvalidationSkipped();
	tizenpsp_jit_snapshot(text, sizeof(text));
	assert(strstr(text, "invalidation_skipped_rewrites=1"));
	assert(strstr(text, "sched_emu_reads=0") && strstr(text, "process_cpu_valid=0")); // Windows fixture.
	assert(strstr(text, "ffmpeg=0")); // Host diagnostic fixture does not link a decoder.
	assert(strstr(text, "compiled_blocks=1") && strstr(text, "dispatch_return=1") && strstr(text, "protections=2"));
	assert(strstr(text, "write_estimate_overruns=3") && strstr(text, "largest_overrun_block_bytes=6760"));
	assert(strstr(text, "linked_exits=2 dispatcher_exits=1 cache_clears=1 invalidated_blocks=1"));
	assert(strstr(text, "emu_samples=2 emu_wall_us=1900 emu_cpu_us=1350"));
	assert(strstr(text, "render_samples=1 render_wall_us=2000 render_cpu_us=100"));
	assert(strstr(text, "compile_samples=2 compile_wall_us=1 compile_cpu_us=30 perf_clock_errors=0"));
	assert(strstr(text, "sync_wait_samples=1 sync_wait_wall_us=5000 sync_wait_cpu_us=50"));
	assert(strstr(text, "readback_samples=1 readback_wall_us=3000 readback_cpu_us=100"));
	assert(strstr(text, "cache_used_bytes=123456 cache_capacity_bytes=33488896 memory_fast=1"));
	assert(strstr(text, "vertex_jit=1"));
	assert(strstr(text, "vertex_compiled=1 vertex_compile_failed=1 vertex_code_bytes=256 vertex_batches=4096 vertex_scalar=4098 vertex_simd=4095 vertex_generated=4095"));
	const char *sampleText = strstr(text, "vertex_decode_samples=");assert(sampleText);
	unsigned samples = 0;assert(sscanf(sampleText, "vertex_decode_samples=%u", &samples) == 1);
	assert(samples >= 60 && samples <= 68); // Sparse timings; all vertices are counted.
	TizenJitFastMemory(false);tizenpsp_jit_snapshot(text, sizeof(text));assert(strstr(text, "memory_fast=0"));
	for (unsigned capacity = 1; capacity <= sizeof(text); ++capacity) {
		char bounded[16385];memset(bounded, 0x7F, sizeof(bounded));
		unsigned length = tizenpsp_jit_snapshot(bounded, capacity);
		assert(length < capacity && bounded[length] == 0 && (unsigned char)bounded[capacity] == 0x7F);
	}
	assert(tizenpsp_command_queue_status()==2);
	assert(tizenpsp_set_command_queue(1)==3);
	assert(tizenpsp_set_command_queue(2)==0xFFFFFFFFU);
	assert(tizenpsp_command_queue_status()==3);
	TizenCommandRecord(true);TizenCommandRecord(false);
	tizenpsp_jit_snapshot(text,sizeof(text));
	assert(strstr(text,"command_queue_queued=1 command_queue_skipped=1"));
	assert(tizenpsp_set_command_queue(0)==2);
	char tiny[4];assert(tizenpsp_jit_snapshot(tiny, sizeof(tiny)) == 3 && tiny[3] == 0);
	assert(tizenpsp_jit_snapshot(nullptr, 0) == 0);
	for (int i = 0; i < 5000; i++)TizenJitEvent("bounded", 0, 0, 0, 0);
	TizenJitEvent("failure_after_cap", 0, 0, 0, 13);
	FILE *file = fopen(argv[1], "rb");assert(file);fseek(file, 0, SEEK_END);long length = ftell(file);assert(length < 1024 * 1024);fseek(file, 0, SEEK_SET);
	char line[512];bool error = false;while(fgets(line, sizeof(line), file))if(strstr(line, "failure_after_cap"))error = true;
	fclose(file);assert(error);
	puts("PASS: native JIT counters, trace persistence, bounds, error retention and snapshot truncation.");
}
