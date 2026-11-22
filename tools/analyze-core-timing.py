"""Compare per-thread cumulative counters in current-session reports."""
import argparse
import datetime as dt
import json
import re
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('directory', type=Path)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
sessions = {}
for path in sorted(a.directory.glob('report-*.txt')):
    text = path.read_text(encoding='utf-8-sig')
    match = re.search(r'^SESSION id=(\w+).*?game_file=(.*?) selected_core=', text, re.M)
    if not match:
        continue
    sid, game = match.groups()
    sections = dict(re.findall(r'^--- (.*?) \(bounded tail\) ---\n(.*?)(?=^--- |\Z)', text, re.M | re.S))
    samples = {}
    for name in ('graphics-startup.log', 'graphics-test.part2.log', 'graphics-test.part1.log', 'graphics-test.log'):
        section = sections.get(name, '')
        identity = re.search(r'SESSION id=(\w+)', section)
        if identity and identity[1] != sid:
            raise ValueError('Mismatched current section: ' + name)
        for stamp, values in re.findall(r'^(\S+Z) JIT_NATIVE (.*)$', section, re.M):
            fields = {k: int(v) for k, v in re.findall(r'\b(\w+)=(\d+)\b', values)}
            if 'emu_cpu_us' in fields:
                when = dt.datetime.fromisoformat(stamp.replace('Z', '+00:00')).timestamp()
                samples[when] = fields
    if len(samples) < 2:
        continue
    points = sorted(samples)
    end = points[-1]
    start = min(points[:-1], key=lambda t: abs(end - t - 60))
    windows = [(start, end)] + list(zip(points[-13:-1], points[-12:]))
    output = []
    for first, last in windows:
        seconds = last - first
        delta = {k: (v - samples[first].get(k, v)) & 0xFFFFFFFF for k, v in samples[last].items()}
        item = {'seconds': round(seconds, 3), 'end_utc': dt.datetime.fromtimestamp(last, dt.timezone.utc).isoformat(),
                'blocks': delta.get('compiled_blocks'), 'cache_clears': delta.get('cache_clears'),
                'invalidations': delta.get('invalidated_blocks'), 'clock_errors': delta.get('perf_clock_errors')}
        for phase in ('emu', 'render', 'compile', 'dispatch', 'frame_wait', 'queue_wait', 'sync_wait', 'gl_work', 'readback', 'vertex_decode'):
            if phase + '_samples' not in delta:
                continue
            count = delta[phase + '_samples']
            item[phase] = {'samples': count,
                'wall_ms_per_sample': round(delta[phase + '_wall_us'] / count / 1000, 3) if count else None,
                'cpu_ms_per_sample': round(delta[phase + '_cpu_us'] / count / 1000, 3) if count else None,
                'wall_seconds': round(delta[phase + '_wall_us'] / 1e6, 3),
                'cpu_seconds': round(delta[phase + '_cpu_us'] / 1e6, 3),
                'one_core_cpu_percent': round(delta[phase + '_cpu_us'] / seconds / 10000, 2)}
            if phase == 'vertex_decode':
                item[phase]['timing_sample_divisor'] = samples[last].get('vertex_timing_sample_divisor', 1)
        output.append(item)
    sessions[sid] = {'session': sid, 'game': game, 'report': path.name, 'windows': output}
a.output.write_text(json.dumps(list(sessions.values()), indent=2) + '\n', encoding='utf-8')
for s in sessions.values():
    print(s['game'])
    for w in s['windows']:
        print(json.dumps(w))
