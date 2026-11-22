// One frontend tick advances to a PSP vblank, not merely a display-buffer flip.
// Callbacks run on the emulation thread. Stop/error states must never be resumed.
#pragma once
template<class Vblank, class Run, class AtFrame, class Resume>
unsigned TizenRunToVblank(Vblank vblank, Run run, AtFrame atFrame, Resume resume) {
    const auto initial = vblank();
    unsigned folded = 0;
    for (unsigned attempt = 0; attempt < 8; ++attempt) {
        run();
        if (!atFrame() || vblank() != initial)
            break;
        if (attempt == 7)
            break;  // Bound malformed/no-progress display loops.
        resume();
        ++folded;
    }
    return folded;
}
