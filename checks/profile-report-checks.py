"""Check unit conversion, unsigned rollover, missing samples and inclusive estimates."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]/'tools'))
from profile_costs import summarize

a = {'prof_draw_calls': 100, 'prof_draw_samples': 10, 'prof_draw_wall_us': 9000,
     'prof_draw_cpu_us': 1000, 'prof_draw_divisor': 64,
     'process_cpu_valid': 1, 'process_cpu_us': 0xfffffff0, 'emu_cpu_us': 5, 'render_cpu_us': 0,
     'sched_emu_reads': 1, 'sched_emu_errors': 0, 'sched_emu_stamp_us': 0xfffffff0,
     'sched_emu_cpu_us': 20, 'sched_emu_wait_us': 10, 'sched_emu_slices': 5}
b = dict(a, prof_draw_calls=228, prof_draw_samples=12, prof_draw_wall_us=10000, prof_draw_cpu_us=1500,
         process_cpu_us=984, emu_cpu_us=305, render_cpu_us=200,
         sched_emu_reads=2, sched_emu_stamp_us=984, sched_emu_cpu_us=420, sched_emu_wait_us=210,
         sched_emu_slices=8)
r = summarize(a,b,0.002)
assert r['profiles']['draw']['estimated_cpu_seconds']==0.032
assert r['profiles']['draw']['sampled_cpu_seconds']==0.0005
assert r['profiles']['draw']['inclusive'] and r['profiles']['draw']['estimate']
assert r['process']['cpu_seconds']==0.001 and r['process']['one_core_cpu_percent']==50
assert r['process']['cpu_outside_emu_render_scopes_seconds']==0.0005
assert r['scheduler']['emu']['sample_span_seconds']==0.001
assert r['scheduler']['emu']['ready_wait_seconds']==0.0002 and r['scheduler']['emu']['timeslices']==3
b['prof_draw_samples']=a['prof_draw_samples']
assert summarize(a,b,1)['profiles']['draw']['estimated_cpu_seconds'] is None
b['prof_draw_divisor']=1
assert summarize(a,b,1)['profiles']['draw']['estimated_cpu_seconds']==0.0005
b['process_cpu_valid']=0
assert 'process' not in summarize(a,b,1)
assert summarize({}, {}, 1)=={'profiles':{},'scheduler':{}}
u = summarize({'uniform_uploaded': 10, 'uniform_skipped': 0xfffffff0},
              {'uniform_uploaded': 20, 'uniform_skipped': 14}, 1)['uniforms']
assert u == {'uploaded': 10, 'skipped': 30, 'skipped_percent': 75.0}
print('PASS: profile sampling estimates, unsigned CPU/scheduler rollover, units and unavailable data.')
