"""Validate and compare paired A/B captures; never infer an all-games speedup."""
import argparse
import json
from pathlib import Path
import re
import statistics


def fields(line):
    return dict(re.findall(r'\b(\w+)=([^\s]+)', line))


def integers(line):
    return {k: int(v) for k, v in fields(line).items() if v.isdigit()}


def snapshot(payload):
    lines = payload.splitlines()
    def one(prefix):
        found = [x for x in lines if x.startswith(prefix)]
        if len(found) != 1:
            raise ValueError('Missing/duplicate ' + prefix)
        return found[0]
    return dict(header=fields(lines[0]), context=one('SESSION '), game=one('game_file='),
                native=integers(one('JIT_NATIVE ')), progress=integers(one('CORE_PROGRESS ')),
                state=integers(one('LIVE_STATE ')), frame=fields(one('AB_FRAME ')))


def analyze_payloads(payloads):
    groups = {}
    for payload in payloads:
        first = payload.split('\n')[0]
        if not first.startswith('AB_'):
            continue
        meta = fields(first); identity = meta.get('id')
        if identity:
            groups.setdefault(identity, []).append(payload)
    results = []
    for identity, events in groups.items():
        result = dict(id=identity, usable_comparison=False, phases=[], reasons=[])
        try:
            plans = [fields(e) for e in events if e.startswith('AB_PLAN ')]
            if len(plans) != 1 or plans[0].get('experiment') not in ('vertex_binding','command_queue'):
                raise ValueError('Missing/duplicate/unknown plan')
            plan = plans[0]; experiment = plan['experiment']; experiment_prefix = 'vertex_binding' if experiment == 'vertex_binding' else 'command_queue'; order = plan['order']; duration = int(plan['seconds'])
            if order not in ('ABBA', 'BAAB') or not 10 <= duration <= 120 or plan.get('warmup') != '3':
                raise ValueError('Invalid plan')
            if sum(e.startswith('AB_DONE ') for e in events) != 1 or any(e.startswith('AB_ABORT ') for e in events):
                raise ValueError('Capture incomplete or aborted')
            expected = ['AB_PLAN'] + [kind for _ in order for kind in ('AB_START', 'AB_END')] + ['AB_DONE']
            if [e.split(' ', 1)[0] for e in events] != expected:
                raise ValueError('Missing, duplicate or reordered capture events')
            reference = None; generation = None; previous_end = None; native_mode = None
            for index, variant in enumerate(order):
                before = snapshot(events[1 + index * 2]); after = snapshot(events[2 + index * 2])
                for item in (before, after):
                    h = item['header']
                    if h['variant'] != variant or int(h['phase']) != index:
                        raise ValueError('Phase/variant mismatch')
                    context = (item['context'], item['game'])
                    if reference is None:
                        reference = context; generation = h['generation']; native_mode = item['native']['profile_enabled']
                    if context != reference or h['generation'] != generation or item['state']['connection'] != int(generation):
                        raise ValueError('Game, settings, session or connection changed')
                    if item['state']['ready'] != 1 or item['native'][experiment_prefix + '_supported'] != 1:
                        raise ValueError('Game/experiment unavailable')
                    if item['native']['profile_enabled'] != native_mode or item['native'][experiment_prefix + '_requested'] != (variant == 'B'):
                        raise ValueError('Profiling/variant changed')
                h0, h1 = before['header'], after['header']
                elapsed = float(h1['mono']) - float(h0['mono'])
                if not duration - .00001 <= elapsed <= duration + 5:
                    raise ValueError('Invalid or badly stalled measurement interval')
                if previous_end is not None and float(h0['mono']) - previous_end < 3 - .00001:
                    raise ValueError('Warmup interval missing')
                previous_end = float(h1['mono'])
                if int(h1['dropped']) != int(h0['dropped']) or after['state']['dropped'] != before['state']['dropped']:
                    raise ValueError('Telemetry dropped during measurement')
                a, b = before['native'], after['native']
                delta = lambda key: (b[key] - a[key]) & 0xffffffff
                if a['process_cpu_valid'] != 1 or b['process_cpu_valid'] != 1 or delta('perf_clock_errors'):
                    raise ValueError('CPU clock unavailable/error')
                fast, fallback = delta('vertex_binding_fast_draws'), delta('vertex_binding_fallback_draws')
                if experiment == 'vertex_binding' and ((variant == 'A' and (fast or not fallback)) or (variant == 'B' and not fast)):
                    raise ValueError('Requested rendering path did not execute')
                if experiment == 'command_queue':
                    if a['vertex_binding_requested'] != b['vertex_binding_requested'] or a['vertex_binding_requested'] != 1:
                        raise ValueError('Vertex path changed during queue experiment')
                    queued, skipped = delta('command_queue_queued'), delta('command_queue_skipped')
                    if not queued or (variant == 'A' and skipped):
                        raise ValueError('Queue experiment counters invalid')
                guest = after['progress']['guest_ms'] - before['progress']['guest_ms']
                flips = after['progress']['actual_flips'] - before['progress']['actual_flips']
                if guest <= 0 or flips <= 0 or int(after['frame']['runs']) <= 0 or int(after['frame']['overflow']):
                    raise ValueError('No progress or frame sample overflow')
                cpu = delta('process_cpu_us')
                sampled = {}
                for name in ('hle', 'ge', 'draw_submit', 'texture_build', 'software_transform', 'gl_init', 'gl_pass', 'gl_draw', 'gl_upload'):
                    prefix = 'prof_' + name
                    keys = [prefix + suffix for suffix in ('_calls', '_samples', '_cpu_us')]
                    if all(key in a and key in b for key in keys):
                        calls, samples, micros = [delta(key) for key in keys]
                        sampled[name] = dict(calls=calls, samples=samples, sparse_sample=samples < 30,
                            estimated_inclusive_cpu_seconds=micros / 1e6 * calls / samples if samples else None)
                result['phases'].append(dict(phase=index, variant=variant, seconds=elapsed,
                    guest_speed_pct=guest / elapsed / 10, flips_per_second=flips / elapsed,
                    process_one_core_pct=cpu / elapsed / 10000,
                    process_cpu_ms_per_guest_second=cpu / guest,
                    dispatch_cpu_seconds=delta('dispatch_cpu_us') / 1e6,
                    render_cpu_seconds=delta('render_cpu_us') / 1e6,
                    run_ms_p95=float(after['frame']['run_ms_p95']),
                    spacing_ms_p95=float(after['frame']['spacing_ms_p95']),
                    fast_draws=fast, fallback_draws=fallback,
                    fast_draw_share_pct=100 * fast / (fast + fallback) if fast+fallback else 0,
                    command_queue_queued=delta('command_queue_queued') if experiment=='command_queue' else None,
                    command_queue_skipped=delta('command_queue_skipped') if experiment=='command_queue' else None,
                    compile_cpu_seconds=delta('compile_cpu_us') / 1e6 if 'compile_cpu_us' in a and 'compile_cpu_us' in b else None,
                    compiled_blocks=delta('compiled_blocks') if 'compiled_blocks' in a and 'compiled_blocks' in b else None,
                    invalidated_blocks=delta('invalidated_blocks') if 'invalidated_blocks' in a and 'invalidated_blocks' in b else None,
                    sampled_inclusive_costs=sampled))
            result.update(experiment=experiment, usable_comparison=True, context=reference[0], game=reference[1], order=order)
            result['variants'] = {}
            for variant in 'AB':
                rows = [r for r in result['phases'] if r['variant'] == variant]
                result['variants'][variant] = {metric: statistics.mean(r[metric] for r in rows)
                    for metric in ('guest_speed_pct', 'flips_per_second', 'process_cpu_ms_per_guest_second', 'run_ms_p95', 'spacing_ms_p95')}
            a, b = result['variants']['A'], result['variants']['B']
            result['observed_guest_speed_change_pct'] = 100 * (b['guest_speed_pct'] / a['guest_speed_pct'] - 1)
            result['observed_cpu_cost_reduction_pct'] = 100 * (1 - b['process_cpu_ms_per_guest_second'] / a['process_cpu_ms_per_guest_second'])
        except (ValueError, KeyError, ZeroDivisionError) as error:
            result['reasons'].append(str(error))
        result['limits'] = ['ABBA/BAAB balances order but does not replay identical game scenes.',
                            'Transport and profiling remain enabled in both variants; their absolute overhead is not removed.',
                            'Run/spacing p95 measures frontend callbacks, not GPU completion or physical scanout.',
                            'Sampled HLE/GE/graphics costs are inclusive, overlap and may miss rare stalls; do not add them as separate CPU totals.',
                            'Repeat representative scenes and multiple games; this is not proof of an all-games speedup.']
        results.append(result)
    return results


def main():
    p = argparse.ArgumentParser(description=__doc__); p.add_argument('capture', type=Path)
    p.add_argument('--output', type=Path); a = p.parse_args()
    files = sorted(a.capture.glob('live-*.jsonl')) if a.capture.is_dir() else [a.capture]
    payloads = []
    for path in files:
        for line in path.read_text(encoding='utf-8').splitlines():
            payloads.append(json.loads(line)['payload'])
    result = analyze_payloads(payloads)
    text = json.dumps(dict(captures=result), indent=2) + '\n'
    if a.output:
        a.output.parent.mkdir(parents=True, exist_ok=True); a.output.write_text(text, encoding='utf-8')
    print(text)


if __name__ == '__main__':
    main()
