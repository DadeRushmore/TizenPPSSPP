#undef NDEBUG
#include <cassert>
#include <cstdio>
#include "../native/core-build/runtime/tizenpsp_guest_error.h"

int main() {
	constexpr const char *error = "ERROR: Error, can only expand when we reached the head";
	TizenGuestErrorCapture capture;
	TizenGuestErrorEvent event;
	assert(!capture.Take(event));
	capture.Observe(false, error, 1, 2, 3);
	capture.Observe(true, "Error, can only expand when we reached the head", 1, 2, 3);
	capture.Observe(true, "ERROR: Error, can only expand when we reached the head!", 1, 2, 3);
	assert(!capture.Take(event));
	capture.Observe(true, "ERROR: Error, can only expand when we reached the head\r\n", 0x08812340, 0x08812400, 37);
	capture.Observe(true, error, 1, 2, 3);
	assert(capture.Take(event));
	assert(event.pc == 0x08812340 && event.ra == 0x08812400 && event.thread == 37);
	assert(!capture.Take(event));
	capture.Observe(true, error, 4, 5, 6);
	assert(!capture.Take(event));
	puts("PASS: guest error exact stderr routing, newline handling, retained first PC/RA/thread, one-shot deferred consumption.");
}
