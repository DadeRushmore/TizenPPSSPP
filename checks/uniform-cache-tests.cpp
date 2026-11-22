#include "../native/core-build/runtime/tizenpsp_uniform_cache.h"
#include <cstdlib>
#include <cstdio>
#define assert(condition) do { if (!(condition)) { fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #condition); abort(); } } while (0)
int main() {
	TizenUniformCache a, b;
	uint32_t values[16]{};
	assert(a.Update(0, 1, 4, values));
	assert(!a.Update(0, 1, 4, values));
	assert(b.Update(0, 1, 4, values)); // A different program must upload.
	values[3] = 5;
	assert(a.Update(0, 1, 4, values));
	assert(!a.Update(0, 1, 4, values));
	assert(a.Update(0, 3, 4, values)); // Same bits, integer instead of float.
	assert(a.Update(0, 3, 1, values));
	assert(a.Update(64, 3, 1, values)); // Collision safely evicts.
	assert(a.Update(0, 3, 1, values));
	a.Clear();
	assert(a.Update(0, 3, 1, values)); // Relink invalidates all values.
	assert(a.Update(1234567, 4, 16, values)); // Sparse driver locations.
	assert(!a.Update(1234567, 4, 16, values));
	values[15]=1;
	assert(a.Update(1234567, 4, 16, values));
	values[0]=0x80000000;
	assert(a.Update(1, 1, 1, values));
	values[0]=0;
	assert(a.Update(1, 1, 1, values)); // Preserve signed zero bit patterns.
	assert(a.Update(-1, 1, 1, values));
	assert(a.Update(1, 1, 17, values));
	puts("PASS: uniform program isolation, type/width, matrices, eviction, relink, sparse locations and bitwise changes.");
}
