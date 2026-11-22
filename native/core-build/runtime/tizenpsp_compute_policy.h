#pragma once
#include <stddef.h>

// Clamp to the inherited affinity, preserving a usable pool when it cannot be read.
inline int TizenComputeThreadLimit(int reported, const unsigned char *mask, size_t bytes) {
	int allowed = 0;
	for (size_t i = 0; i < bytes; ++i)
		for (unsigned bit = 0; bit < 8; ++bit)
			allowed += (mask[i] >> bit) & 1;
	if (reported < 1) reported = 1;
	if (reported > 32) reported = 32;
	return allowed > 0 && allowed < reported ? allowed : reported;
}
