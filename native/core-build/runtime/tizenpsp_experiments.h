#pragma once
// Status: bit 0 requested, bit 1 supported by the current core GLES context.
// Only the render thread reads this at a pass boundary; no live VAO mutation.
extern "C" unsigned tizenpsp_set_vertex_binding(unsigned enabled);
extern "C" unsigned tizenpsp_vertex_binding_status();
void TizenVertexBindingCapability(bool supported);

extern "C" unsigned tizenpsp_set_command_queue(unsigned enabled);
extern "C" unsigned tizenpsp_command_queue_status();
void TizenCommandRecord(bool queued);
