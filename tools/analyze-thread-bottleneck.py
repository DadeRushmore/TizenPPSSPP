"""Read explicitly identified current AND retained previous sessions, deduplicated by ID."""
import argparse
import datetime as dt
import hashlib
import json
import re
from pathlib import Path
from profile_costs import summarize
from framebuffer_counts import summarize_framebuffer
from thread_cpu import parse_tasks, summarize_threads

p = argparse.ArgumentParser()
p.add_argument('directory', type=Path)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
sessions = {}

def timestamp(value):
    return dt.datetime.fromisoformat(value.replace('Z', '+00:00')).timestamp()

def iso(value):
    return dt.datetime.fromtimestamp(value, dt.timezone.utc).isoformat()

for path in sorted(a.directory.glob('report-*.txt')):
    text = path.read_text(encoding='utf-8-sig')
    sections = dict(re.findall(r'^--- (.*?) \(bounded tail\) ---\n(.*?)(?=^--- |\Z)', text, re.M | re.S))
    for previous in (False, True):
        suffix = '.previous' if previous else ''
        context = sections.get('session-context' + suffix + '.txt', '')
        identity = re.search(r'SESSION id=(\w+).*?game_file=(.*?) selected_core=(\S+)(.*)', context)
        if not identity:
            continue
        sid, game, core, settings = identity.groups()
        session = sessions.setdefault(sid, {'session': sid, 'game': game, 'core': core,
            'app_version': re.search(r'app_version=(\S+)', context)[1],
            'settings': dict(re.findall(r'(\w+)=([^\s]+)', settings)), 'sources': [],
            'affinity': set(), 'native': {}, 'progress': {}, 'paused': set(), 'thread_cpu': {}})
        session['sources'].append({'report': path.name, 'section': 'previous' if previous else 'current',
                                  'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
        for name in ('graphics-startup' + suffix + '.log', 'graphics-test' + suffix + '.part2.log',
                     'graphics-test' + suffix + '.part1.log', 'graphics-test' + suffix + '.log'):
            section = sections.get(name, '')
            ids = set(re.findall(r'SESSION id=(\w+)', section))
            assert not ids or ids == {sid}, (path, name, ids, sid)
            for stamp, values in re.findall(r'^(\S+Z) JIT_NATIVE (.*)$', section, re.M):
                fields = {k: int(v) for k, v in re.findall(r'\b(\w+)=(\d+)\b', values)}
                if 'emu_cpu_us' in fields:
                    session['native'][timestamp(stamp)] = fields
            for stamp, guest, flips in re.findall(r'^(\S+Z) CORE_PROGRESS guest_ms=(\d+) actual_flips=(\d+)', section, re.M):
                session['progress'][timestamp(stamp)] = (int(guest), int(flips))
            for stamp, errors, tasks in re.findall(r'^(\S+Z) HOST_THREAD_CPU .*?errors=(\d+) tasks=(.*)$', section, re.M):
                session['thread_cpu'][timestamp(stamp)] = (int(errors), parse_tasks(tasks))
            session['paused'].update(timestamp(t) for t in re.findall(r'^(\S+Z) AUDIO .*Audio paused', section, re.M))
        trace = sections.get('jit-native' + suffix + '.log', '')
        session['affinity'].update(re.findall(r'stage=(thread_(?:emu|render)_cpus_\S+)', trace))

results = []
for session in sessions.values():
    native, progress, paused = session.pop('native'), session.pop('progress'), session.pop('paused')
    threads = session.pop('thread_cpu')
    points = sorted(native)
    omitted = []
    while points and any(abs(points[-1]-t) < 1 for t in paused):
        omitted.append(iso(points.pop()))
    session['trailing_paused_snapshots_omitted'] = omitted
    session['affinity'] = sorted(session['affinity'])
    session['windows'] = []
    for requested in (60, 25):
        if len(points) < 2:
            continue
        end = points[-1]
        start = min(points[:-1], key=lambda t: abs(end - t - requested))
        delta = {k: (v-native[start].get(k, v)) & 0xffffffff for k, v in native[end].items()}
        window = {'requested_seconds': requested, 'start_utc': iso(start), 'end_utc': iso(end),
                  'seconds': round(end-start, 3), 'compiled_blocks': delta['compiled_blocks'],
                  'cache_clears': delta['cache_clears'], 'phases': {}}
        window['jit_activity'] = {k: delta[k] for k in ('protections', 'invalidated_blocks',
            'invalidation_skipped_rewrites', 'linked_exits', 'dispatcher_exits') if k in delta}
        for phase in ('emu', 'render', 'compile', 'dispatch', 'frame_wait', 'queue_wait', 'sync_wait', 'gl_work', 'readback', 'vertex_decode'):
            if phase+'_samples' not in delta:
                continue
            count = delta.get(phase+'_samples', 0)
            wall, cpu = delta.get(phase+'_wall_us', 0), delta.get(phase+'_cpu_us', 0)
            window['phases'][phase] = {'samples': count, 'wall_seconds': round(wall/1e6, 3),
                'cpu_seconds': round(cpu/1e6, 3),
                'wall_ms_per_sample': round(wall/count/1000, 3) if count else None,
                'cpu_ms_per_sample': round(cpu/count/1000, 3) if count else None}
            if phase == 'vertex_decode':
                window['phases'][phase]['timing_sample_divisor'] = native[end].get('vertex_timing_sample_divisor', 1)
        window['vertex_counts'] = {k: delta[k] for k in ('vertex_compiled', 'vertex_compile_failed', 'vertex_batches', 'vertex_scalar', 'vertex_simd', 'vertex_generated') if k in delta}
        window['cost_profile'] = summarize(native[start], native[end], end-start)
        window['framebuffer_activity'] = summarize_framebuffer(native[start], native[end], end-start)
        if threads:
            first_t, last_t = (min(threads, key=lambda t: abs(t-target)) for target in (start, end))
            if last_t > first_t and abs(first_t-start) < 1 and abs(last_t-end) < 1:
                window['thread_cpu'] = summarize_threads(threads[first_t][1], threads[last_t][1], native[end].get('process_clock_hz', 0), last_t-first_t)
                window['thread_cpu_read_errors'] = [threads[first_t][0], threads[last_t][0]]
        if progress:
            first, last = (min(progress, key=lambda t: abs(t-target)) for target in (start, end))
            if last > first and abs(first-start) < 1 and abs(last-end) < 1:
                guest, flips = (progress[last][i]-progress[first][i] for i in (0, 1))
                assert guest >= 0 and flips >= 0
                window['progress'] = {'seconds': round(last-first, 3),
                    'guest_speed_percent': round(guest/(last-first)/10, 2),
                    'flips_per_second': round(flips/(last-first), 2),
                    'display_progress': flips > 0}
        session['windows'].append(window)
    results.append(session)
    print(session['game'], session['affinity'])
    for w in session['windows']:
        print(w['seconds'], w.get('progress'), 'emu', w['phases']['emu'], 'render', w['phases']['render'])
a.output.write_text(json.dumps(results, indent=2)+'\n', encoding='utf-8')
