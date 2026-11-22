// A minimal C++ module with required dynamic initialization, no C++ library.
// Reproduces the startup feature required by ordinary PPSSPP global objects.
static volatile int initialized;
static volatile int calls;
struct Startup {
    Startup() { ++calls; initialized = 0x505350; }
};
static Startup startup;
extern "C" __attribute__((visibility("default")))
int tizenpsp_cpp_probe() { return calls==1 ? initialized : -calls; }
