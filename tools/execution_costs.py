"""Interpret cumulative native timing deltas without adding nested scopes."""

EXACT_SCOPES = {
    'emu': 'Emulation thread (inclusive)',
    'dispatch': 'Generated code plus services called from it (inclusive)',
    'compile': 'Native compilation',
    'render': 'Native render thread scope (inclusive)',
    'host_run': 'retro_run on the frontend thread (inclusive)',
    'present': 'Core video handoff, not physical display presentation',
    'audio_output': 'Core audio handoff',
    'queue_wait': 'Render queue wait; wall time overlaps other threads',
}


def summarize_execution(delta, endpoint):
    exact = {}
    for key, label in EXACT_SCOPES.items():
        if key + '_samples' in delta:
            exact[key] = dict(label=label, calls=delta[key + '_samples'],
                              wall_seconds=delta[key + '_wall_us'] / 1e6,
                              cpu_seconds=delta[key + '_cpu_us'] / 1e6)
    sampled = {}
    if endpoint.get('profile_enabled', 0):
        for key in ('ge', 'draw_submit', 'texture_build', 'software_transform',
                    'gl_init', 'gl_pass', 'gl_copy', 'gl_shader', 'gl_draw', 'gl_upload', 'hle'):
            prefix = 'prof_' + key
            if prefix + '_samples' not in delta:
                continue
            samples, calls = delta[prefix + '_samples'], delta[prefix + '_calls']
            # This is a deterministic sparse sample, not an exclusive measurement
            # or a statistical confidence interval. Never subtract it from dispatch.
            scale = calls / samples if samples else None
            sampled[key] = dict(calls=calls, samples=samples,
                sampled_cpu_seconds=delta[prefix + '_cpu_us'] / 1e6,
                estimated_inclusive_cpu_seconds=delta[prefix + '_cpu_us'] / 1e6 * scale if scale else None,
                sparse_sample=samples < 30,
                divisor=endpoint.get(prefix + '_divisor'))
    binding = None
    if 'vertex_binding_fast_draws' in delta:
        original = delta['vertex_binding_legacy_attributes']
        actual = delta['vertex_binding_format_calls'] + delta['vertex_binding_buffer_calls']
        binding = dict(fast_draws=delta['vertex_binding_fast_draws'],
            fallback_draws=delta['vertex_binding_fallback_draws'],
            original_attribute_calls_for_fast_draws=original,
            replacement_setup_calls=actual,
            setup_calls_saved=original - actual,
            setup_call_reduction_pct=100 * (original - actual) / original if original else None)
    return dict(exact_inclusive_scopes=exact, sampled_inclusive_estimates=sampled,
        vertex_binding=binding,
        limits=['Scopes overlap and must not be added together.',
                'Dispatch includes guest instructions, HLE, GPU work and replacements.',
                'Sparse deterministic samples can miss rare stalls and can be biased by periodic work.',
                'Present measures the core callback; Evas composition, GPU completion and scanout are outside it.',
                'Driver call reduction is not an FPS or GPU utilization measurement.'])
