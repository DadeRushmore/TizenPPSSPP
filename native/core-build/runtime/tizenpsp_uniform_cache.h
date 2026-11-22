// TizenPSP GLES uniform suppression. GPL-2.0.
#pragma once
#include <cstdint>
#include <cstring>

// Owned by one GL program and accessed only by the GL consumer thread.
// All supported writes update one uniform array element (including mat4).
// Collisions evict an entry and cause an extra upload, never a missed update.
class TizenUniformCache {
public:
	void Clear() { for (auto &entry : entries_) entry.location = -1; }
	bool Update(int location, unsigned type, unsigned words, const void *data) {
		if (location < 0 || !data || words == 0 || words > 16) return true;
		auto &entry = entries_[(unsigned)location % 64];
		if (entry.location == location && entry.type == type && entry.words == words &&
			memcmp(entry.data, data, words * sizeof(uint32_t)) == 0) return false;
		entry.location = location;
		entry.type = type;
		entry.words = words;
		memcpy(entry.data, data, words * sizeof(uint32_t));
		return true;
	}
private:
	struct Entry {
		int location = -1;
		unsigned type = 0, words = 0;
		uint32_t data[16]{};
	};
	Entry entries_[64];
};
