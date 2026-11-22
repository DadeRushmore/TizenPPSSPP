// Bounded guest failure capture. GPL-2.0.
#pragma once
#include <cstdint>
#include <string_view>

struct TizenGuestErrorEvent {
	uint32_t pc = 0, ra = 0;
	int thread = 0;
};

// Both the HLE output hook and the frame-boundary consumer run on the emulation
// thread. This stores only the first exact error per loaded core instance.
class TizenGuestErrorCapture {
public:
	void Observe(bool stderrOutput, std::string_view text, uint32_t pc, uint32_t ra, int thread) {
		if (seen_ || !stderrOutput) return;
		while (!text.empty() && (text.back() == '\n' || text.back() == '\r')) text.remove_suffix(1);
		if (text != "ERROR: Error, can only expand when we reached the head") return;
		event_ = {pc, ra, thread};
		seen_ = pending_ = true;
	}
	bool Take(TizenGuestErrorEvent &event) {
		if (!pending_) return false;
		event = event_;
		pending_ = false;
		return true;
	}
private:
	bool seen_ = false, pending_ = false;
	TizenGuestErrorEvent event_;
};

inline TizenGuestErrorCapture tizenGuestError;
