import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from execution_costs import summarize_execution

assert summarize_execution({}, {})['sampled_inclusive_estimates'] == {}
d = dict(dispatch_samples=60, dispatch_wall_us=2000000, dispatch_cpu_us=1000000,
         prof_hle_calls=512, prof_hle_samples=2, prof_hle_cpu_us=100,
         vertex_binding_fast_draws=10000, vertex_binding_fallback_draws=2,
         vertex_binding_legacy_attributes=40000, vertex_binding_format_calls=8,
         vertex_binding_buffer_calls=10000)
r = summarize_execution(d, dict(profile_enabled=2, prof_hle_divisor=256))
assert r['exact_inclusive_scopes']['dispatch']['cpu_seconds'] == 1
assert r['sampled_inclusive_estimates']['hle']['estimated_inclusive_cpu_seconds'] == .0256
assert r['sampled_inclusive_estimates']['hle']['sparse_sample']
assert r['vertex_binding']['setup_calls_saved'] == 29992
d.update(prof_hle_samples=0, prof_hle_cpu_us=0, vertex_binding_legacy_attributes=0,
         vertex_binding_format_calls=0, vertex_binding_buffer_calls=0)
r = summarize_execution(d, dict(profile_enabled=2))
assert r['sampled_inclusive_estimates']['hle']['estimated_inclusive_cpu_seconds'] is None
assert r['vertex_binding']['setup_call_reduction_pct'] is None
assert summarize_execution(d, dict(profile_enabled=0))['sampled_inclusive_estimates'] == {}
print('PASS: execution attribution preserves inclusive scopes, unavailable samples and disabled/fallback paths.')
