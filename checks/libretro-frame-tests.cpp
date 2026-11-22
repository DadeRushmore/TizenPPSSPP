// Test the extracted production EmuFrame, with modeled Thin3D/GPU contracts.
// This does not execute GL or claim to validate the device driver.
#undef NDEBUG
#include <algorithm>
#include <atomic>
#include <cassert>
#include <cstdio>
#include <deque>
#include <initializer_list>
#include <stdexcept>
#include <string>
#include <vector>
#include "../native/core-build/runtime/tizenpsp_frame_pacing.h"

struct DisplayLayoutConfig {};
struct Config {
    bool bSkipBufferEffects = false;
    DisplayLayoutConfig layout;
    const DisplayLayoutConfig &GetDisplayLayoutConfig(int) { return layout; }
} g_Config;
struct Display { int GetDeviceOrientation() { return 0; } } g_display;
enum CoreState { CORE_RUNNING_CPU, CORE_NEXTFRAME, CORE_POWERDOWN, CORE_ERROR };
CoreState coreState = CORE_RUNNING_CPU;
enum class RunMode { Normal, EarlyFlips, Stop, PowerDown, NoProgress, Idle };
static RunMode runMode;
static unsigned calls, vblanks, flips, reports, pathLogs;
static unsigned frontendFBO;
static int outputPixel, pspPixel, presentedPixel;
static std::vector<std::string> events;
static void Check(bool good, const char *message) {
    if (!good) throw std::runtime_error(message);
}

namespace Draw {
enum class DebugFlags { NONE };
enum class RPAction { CLEAR, KEEP };
enum class PresentMode { FIFO };
struct Framebuffer { unsigned handle; };
struct DrawContext {
    Framebuffer primary{0}, psp{101};
    bool frame = false, pass = false, primaryAsNull = false;
    unsigned bound = 0, primaryBinds = 0, clears = 0, presents = 0;
    Framebuffer *GetPrimarySwapchain() { return primaryAsNull ? nullptr : &primary; }
    void BeginFrame(DebugFlags) {
        Check(!frame, "nested frame");
        frame = true;
        pass = false;
        events.push_back("begin");
    }
    void BindFramebufferAsRenderTarget(Framebuffer *fb, std::initializer_list<RPAction> actions, const char *) {
        Check(frame, "pass outside frame");
        Check(actions.size() == 3, "missing attachment actions");
        pass = true;
        // Thin3D PrimaryToNull followed by GLQueueRunner's g_defaultFBO mapping.
        const bool backbuffer = !fb || fb == &primary;
        bound = backbuffer ? frontendFBO : fb->handle;
        if (backbuffer) {
            Check(bound != 0, "must use callback-owned nonzero framebuffer");
            ++primaryBinds;
            events.push_back("backbuffer");
            if (*actions.begin() == RPAction::CLEAR) {
                outputPixel = 0;
                ++clears;
            }
        } else {
            events.push_back("psp-fbo");
        }
    }
    void GuestDraw() {
        Check(pass, "draw without render pass");
        if (g_Config.bSkipBufferEffects) {
            Check(bound == frontendFBO, "direct draw into wrong framebuffer");
            outputPixel = 42;
        } else {
            Check(bound == psp.handle, "buffered draw into wrong framebuffer");
            pspPixel = 42;
        }
        events.push_back("draw");
    }
    void EndFrame() {
        Check(frame, "ending inactive frame");
        frame = false;
        pass = false;
        events.push_back("end");
    }
    void Present(PresentMode) {
        Check(!frame, "present before end");
        presentedPixel = outputPixel;
        ++presents;
        events.push_back("present");
    }
};
}
static std::deque<Draw::DrawContext> drawContexts;
static Draw::DrawContext *drawContext;
struct Context {
    Draw::DrawContext *draw = nullptr;
    void SetRenderTarget() { events.push_back("target"); }
    Draw::DrawContext *GetDrawContext() { return draw; }
} context;
static Context *ctx = &context;
struct GPU {
    void BeginHostFrame(const DisplayLayoutConfig &) { events.push_back("gpu-begin"); }
    void EndHostFrame() { events.push_back("gpu-end"); }
    void PrepareCopyDisplayToOutput(const DisplayLayoutConfig &) { events.push_back("prepare"); }
    void CopyDisplayToOutput(const DisplayLayoutConfig &) {
        Check(context.draw->pass, "copy without pass");
        Check(context.draw->bound == frontendFBO, "presentation into wrong framebuffer");
        if (!g_Config.bSkipBufferEffects) outputPixel = pspPixel;
        events.push_back("copy");
    }
} gpuObject;
static GPU *gpu = &gpuObject;
static void PSP_RunLoopWhileState() {
    Check(coreState == CORE_RUNNING_CPU, "resumed stop/error state");
    ++calls;
    if (runMode == RunMode::Stop || runMode == RunMode::PowerDown) {
        coreState = runMode == RunMode::Stop ? CORE_ERROR : CORE_POWERDOWN;
        return;
    }
    if (runMode == RunMode::Idle) {
        ++vblanks;
        coreState = CORE_NEXTFRAME;
        return;
    }
    if (gpu && context.draw) {
        if (!g_Config.bSkipBufferEffects) {
            context.draw->BindFramebufferAsRenderTarget(&context.draw->psp,
                {Draw::RPAction::KEEP, Draw::RPAction::KEEP, Draw::RPAction::KEEP}, "PSP");
        }
        context.draw->GuestDraw();
    }
    ++flips;
    if (runMode != RunMode::NoProgress && (runMode != RunMode::EarlyFlips || calls % 3 == 0)) ++vblanks;
    coreState = CORE_NEXTFRAME;
}
#ifdef TIZENPSP_ARM_JIT
enum class TizenPerfPhase { Emu };
struct TizenPerfScope { explicit TizenPerfScope(TizenPerfPhase) {} };
static std::atomic<unsigned> tizenFoldedFlips{0}, tizenGuestMillis{0}, tizenActualFlips{0};
static unsigned __DisplayGetNumVblanks() { return vblanks; }
static unsigned __DisplayGetFlipCount() { return flips; }
namespace CoreTiming { static unsigned GetGlobalTimeUs() { return vblanks * 16683; } }
static void ReportGuestError() { ++reports; }
namespace Log { enum { G3D }; }
static void LogPath(int, const char *, unsigned, const char *) { ++pathLogs; }
#define INFO_LOG(...) LogPath(__VA_ARGS__)
#endif

#include "../build/libretro-emuframe-methods.h"

static void Reset(bool skip, RunMode mode = RunMode::Normal) {
    drawContexts.emplace_back();
    drawContext = &drawContexts.back();
    context.draw = drawContext;
    gpu = &gpuObject;
    g_Config.bSkipBufferEffects = skip;
    coreState = CORE_RUNNING_CPU;
    runMode = mode;
    calls = vblanks = flips = reports = 0;
    frontendFBO = 37;
    outputPixel = pspPixel = presentedPixel = -1;
    events.clear();
#ifdef TIZENPSP_ARM_JIT
    tizenFoldedFlips = tizenGuestMillis = tizenActualFlips = 0;
#endif
}
static size_t Event(const char *name) {
    auto found = std::find(events.begin(), events.end(), name);
    Check(found != events.end(), name);
    return found - events.begin();
}
int main() {
    // Default buffered mode retains render-to-PSP, prepare, bind, copy ordering.
    assert(!g_Config.bSkipBufferEffects);
    for (bool skip : {false, true}) {
        for (bool primaryAsNull : {false, true}) {
            Reset(skip);
            drawContext->primaryAsNull = primaryAsNull;
            EmuFrame();
            assert(calls == 1 && vblanks == 1 && coreState == CORE_RUNNING_CPU);
            assert(presentedPixel == 42 && drawContext->primaryBinds == 1 && drawContext->clears == 1);
            assert(drawContext->presents == 1 && !drawContext->frame);
            assert(Event("begin") < Event("gpu-begin") && Event("gpu-end") < Event("prepare"));
            if (skip) {
                assert(Event("backbuffer") < Event("gpu-begin") && Event("backbuffer") < Event("draw"));
            } else {
                assert(Event("draw") < Event("prepare") && Event("prepare") < Event("backbuffer"));
            }
            assert(Event("copy") < Event("end") && Event("end") < Event("present"));
            // The frontend can supply a different owned framebuffer each tick.
            frontendFBO = 91;
            const unsigned previousLogs = pathLogs;
            EmuFrame();
            assert(presentedPixel == 42 && drawContext->bound == 91 && pathLogs == previousLogs);
            // Fixed-rate frontend ticks also occur without drawing (static
            // screens or frame skipping). Keep the last visible picture.
            runMode = RunMode::Idle;
            const unsigned previousFlips = flips;
            EmuFrame();
            EmuFrame();
            assert(presentedPixel == 42 && flips == previousFlips && vblanks == 4);
        }
        for (RunMode mode : {RunMode::EarlyFlips, RunMode::Stop, RunMode::PowerDown, RunMode::NoProgress}) {
            Reset(skip, mode);
            EmuFrame();
            assert(coreState == (mode == RunMode::Stop ? CORE_ERROR : CORE_RUNNING_CPU));
            assert(drawContext->presents == 1);
#ifdef TIZENPSP_ARM_JIT
            const unsigned expectedCalls = mode == RunMode::EarlyFlips ? 3 : mode == RunMode::NoProgress ? 8 : 1;
            assert(calls == expectedCalls && tizenFoldedFlips == expectedCalls - 1);
            assert(tizenActualFlips == flips && reports == 1);
#else
            assert(calls == 1);
#endif
        }
        Reset(skip);
        context.draw = nullptr;
        EmuFrame();
        assert(calls == 1 && drawContext->presents == 0);
        Reset(skip);
        gpu = nullptr;
        EmuFrame();
        assert(calls == 1 && drawContext->presents == 1);
    }
    // An empty first frame starts black rather than exposing stale/undefined
    // texture color. Later empty ticks preserve the most recent game picture.
    Reset(true, RunMode::Idle);
    EmuFrame();
    assert(presentedPixel == 0 && drawContext->clears == 1);
    runMode = RunMode::Normal;
    EmuFrame();
    assert(presentedPixel == 42 && drawContext->clears == 1);
    runMode = RunMode::Idle;
    EmuFrame();
    assert(presentedPixel == 42 && drawContext->clears == 1);
    // Buffered -> direct resets initialization, including when switching back
    // to the very same context; a new context also gets its first clear.
    g_Config.bSkipBufferEffects = false;
    runMode = RunMode::Normal;
    EmuFrame();
    assert(presentedPixel == 42 && drawContext->clears == 2);
    g_Config.bSkipBufferEffects = true;
    runMode = RunMode::Idle;
    EmuFrame();
    assert(presentedPixel == 0 && drawContext->clears == 3);
    Reset(true, RunMode::Idle);
    EmuFrame();
    assert(presentedPixel == 0 && drawContext->clears == 1);
    // Sensitivity checks: these are mutated copies of the same extracted method.
    Reset(true);
    bool caughtMissingPass = false;
    try { EmuFrameMissingPass(); }
    catch (const std::runtime_error &error) { caughtMissingPass = std::string(error.what()) == "draw without render pass"; }
    assert(caughtMissingPass);
    Reset(true);
    EmuFrameLateClear();
    assert(presentedPixel == 0 && drawContext->clears == 2);
    puts("PASS: actual EmuFrame buffered/direct ordering, nonzero frontend FBO, preserved image and idle frames, mode logging, frame pacing and stop paths; both negative controls detected");
}
