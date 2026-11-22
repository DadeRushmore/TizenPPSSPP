"""Summarize one exported current session, excluding retained previous sessions."""
import argparse
import datetime as dt
import hashlib
import json
import re
from pathlib import Path
from thread_cpu import parse_tasks, summarize_threads
from execution_costs import summarize_execution

p = argparse.ArgumentParser()
p.add_argument('report', type=Path)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
raw = a.report.read_bytes()
text = raw.decode('utf-8-sig').replace('\r\n', '\n')
sections = dict(re.findall(r'^--- (.*?) \(bounded tail\) ---\n(.*?)(?=^--- |\Z)', text, re.M | re.S))
context = sections['session-context.txt'].strip()
sid = re.search(r'SESSION id=(\w+)', context)[1]
lines = set()
for name, section in sections.items():
    if not name.startswith('graphics') or '.previous' in name:
        continue
    ids = set(re.findall(r'SESSION id=(\w+)', section))
    assert not ids or ids == {sid}, (name, ids)
    lines.update(x for x in section.splitlines() if re.match(r'^\S+Z ', x))
lines = sorted(lines)

def stamp(line):
    return dt.datetime.fromisoformat(line.split()[0].replace('Z', '+00:00')).timestamp()

def iso(t):
    return dt.datetime.fromtimestamp(t, dt.timezone.utc).isoformat()

exports = [stamp(x) for x in lines if ' LOG_EXPORT ' in x]
cutoff = min(exports, default=float('inf'))
native = [(stamp(x), {k: int(v) for k, v in re.findall(r'\b(\w+)=(\d+)\b', x)})
          for x in lines if ' JIT_NATIVE ' in x and stamp(x) < cutoff]
progress = [(stamp(x), int(re.search(r'guest_ms=(\d+)', x)[1]),
             int(re.search(r'actual_flips=(\d+)', x)[1]))
            for x in lines if ' CORE_PROGRESS ' in x and stamp(x) < cutoff]
assert native and len(progress) > 1
end = progress[-1]
endpoint = min(native, key=lambda x: abs(x[0] - end[0]))
assert abs(endpoint[0] - end[0]) < 0.1
totals = endpoint[1]
groups = {}
for kind in ('all', 'clear', 'range', 'cacheop', 'io', 'module', 'replace', 'debug', 'other'):
    group = {k: totals[f'inv_{kind}_{k}'] for k in ('calls', 'blocks', 'same', 'changed', 'unknown')}
    assert group['same'] + group['changed'] + group['unknown'] == group['blocks']
    measured = group['same'] + group['changed']
    group['sample_coverage_pct'] = 100 * measured / group['blocks'] if group['blocks'] else None
    group['unchanged_pct_of_measured'] = 100 * group['same'] / measured if measured else None
    groups[kind] = group

windows = []
for duration in (None, 180, 60, 25):
    start = progress[0] if duration is None else min(progress[:-1], key=lambda x: abs(end[0] - duration - x[0]))
    base = min(native, key=lambda x: abs(x[0] - start[0]))
    assert abs(base[0] - start[0]) < 0.1
    delta = {k: (v - base[1].get(k, v)) & 0xffffffff for k, v in totals.items()}
    wall = end[0] - start[0]
    windows.append({'requested_seconds': duration, 'start_utc': iso(start[0]), 'end_utc': iso(end[0]),
                    'seconds': wall, 'guest_speed_pct': (end[1] - start[1]) / wall / 10,
                    'display_flips_per_second': (end[2] - start[2]) / wall,
                    'native_delta': delta, 'execution_costs': summarize_execution(delta, totals)})

# Match OS task counters to the same pre-export windows; never use later export CPU.
threads = {stamp(x): parse_tasks(x.split('tasks=', 1)[1]) for x in lines
           if ' HOST_THREAD_CPU ' in x and stamp(x) < cutoff}
forward = [(stamp(x), {k: int(v) for k, v in re.findall(r'\b(\w+)=(\d+)\b', x)})
           for x in lines if ' INPUT_FORWARD ' in x and stamp(x) < cutoff]
forward_endpoint = None
if forward:
    candidate = min(forward, key=lambda x: abs(x[0]-end[0]))
    if abs(candidate[0]-end[0]) < 0.1:
        forward_endpoint = candidate[1]
for window in windows:
    bounds = [dt.datetime.fromisoformat(window[k]).timestamp() for k in ('start_utc', 'end_utc')]
    if threads:
        first, last = [min(threads, key=lambda t: abs(t-target)) for target in bounds]
        if last > first and max(abs(first-bounds[0]), abs(last-bounds[1])) < 0.1:
            window['thread_cpu'] = summarize_threads(threads[first], threads[last], totals['process_clock_hz'], last-first)
    if forward:
        first, last = [min(forward, key=lambda x: abs(x[0]-target)) for target in bounds]
        if last[0] > first[0] and max(abs(first[0]-bounds[0]), abs(last[0]-bounds[1])) < 0.1:
            counts = {k: last[1][k]-v for k, v in first[1].items()}
            for kind in ('button', 'stick'):
                requests, writes = counts[kind+'_requests'], counts[kind+'_writes']
                assert 0 <= writes <= requests
                counts[kind+'_suppressed_pct'] = 100*(requests-writes)/requests if requests else None
            window['input_forward_delta'] = counts

result = {'report': a.report.name, 'sha256': hashlib.sha256(raw).hexdigest(), 'context': context,
          'input_forward_endpoint': forward_endpoint,
          'session': sid, 'endpoint_utc': iso(endpoint[0]), 'native_totals': totals, 'groups': groups,
          'range_blocks_minus_native_invalidations': sum(g['blocks'] for g in groups.values()) - totals['invalidated_blocks'],
          'all_request_share_of_range_candidates_pct': 100 * groups['all']['blocks'] / sum(g['blocks'] for g in groups.values()) if sum(g['blocks'] for g in groups.values()) else None,
          'active_runtime_lines': [x for x in lines if ' HOST_RUNTIME ' in x and stamp(x) <= endpoint[0]][-2:],
          'range_blocks_minus_invalidated_and_retained': sum(g['blocks'] for g in groups.values()) - totals['invalidated_blocks'] - totals.get('retention_retained', 0),
          'compile_pct_of_emu_cpu': 100 * totals['compile_cpu_us'] / totals['emu_cpu_us'],
          'probe_pct_of_emu_cpu': 100 * totals['invalidation_probe_cpu_us'] / totals['emu_cpu_us'],
          'probe_lines': [x for x in lines if ' INV_PROBE ' in x], 'windows': windows,
          'limits': ['Cumulative totals include startup; window deltas begin at recorded progress samples.',
                     'Byte comparisons are deterministic 1/16 samples, not a random or complete census.',
                     'Equality concerns sampled guest-memory ranges at two instants, not all compilation dependencies.',
                     'Progress windows can include game menus/loading; they end before USB export.',
                     'Caller detail is rate limited; grouped counters include calls without a detailed line.']}
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(result, indent=2) + '\n')
print(json.dumps({k: result[k] for k in ('session', 'endpoint_utc', 'range_blocks_minus_native_invalidations',
                                       'all_request_share_of_range_candidates_pct', 'compile_pct_of_emu_cpu',
                                       'probe_pct_of_emu_cpu')}, indent=2))
print(json.dumps(groups['all'], indent=2))
