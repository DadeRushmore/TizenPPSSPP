"""Inclusive CPU cost estimates; sampled phases are never treated as exact totals."""
def delta(first, last, key):
    return (last[key] - first[key]) & 0xffffffff

def summarize(first, last, seconds):
    result = {'profiles': {}, 'scheduler': {}}
    for key in last:
        if not key.startswith('prof_') or not key.endswith('_calls') or key not in first:
            continue
        prefix = key[:-6]
        calls = delta(first, last, key)
        samples = delta(first, last, prefix+'_samples')
        wall = delta(first, last, prefix+'_wall_us') / 1e6
        cpu = delta(first, last, prefix+'_cpu_us') / 1e6
        divisor = last[prefix+'_divisor']
        # Finishing scopes may straddle a snapshot boundary. All scopes are
        # measured for divisor 1; sampled scopes use observed calls/samples.
        factor = 1 if divisor == 1 else calls/samples if samples else None
        result['profiles'][prefix[5:]] = {
            'calls': calls, 'timed_samples': samples, 'sample_divisor': divisor,
            'sampled_cpu_seconds': round(cpu, 6), 'sampled_wall_seconds': round(wall, 6),
            'estimated_cpu_seconds': round(cpu*factor, 6) if factor is not None else None,
            'estimated_wall_seconds': round(wall*factor, 6) if factor is not None else None,
            'inclusive': True, 'estimate': divisor != 1,
        }
    if first.get('process_cpu_valid') == last.get('process_cpu_valid') == 1 and seconds > 0:
        cpu = delta(first, last, 'process_cpu_us') / 1e6
        main = sum(delta(first, last, phase+'_cpu_us') for phase in ('emu','render')) / 1e6
        result['process'] = {'cpu_seconds': round(cpu, 6), 'one_core_cpu_percent': round(cpu/seconds*100, 2),
                             'cpu_outside_emu_render_scopes_seconds': round(cpu-main, 6)}
    if all(k in first and k in last for k in ('uniform_uploaded','uniform_skipped')):
        uploaded = delta(first,last,'uniform_uploaded')
        skipped = delta(first,last,'uniform_skipped')
        result['uniforms'] = {'uploaded': uploaded, 'skipped': skipped,
                              'skipped_percent': round(skipped/(uploaded+skipped)*100,2) if uploaded+skipped else None}
    for phase in ('emu', 'render'):
        prefix = 'sched_'+phase
        if not first.get(prefix+'_reads') or prefix+'_reads' not in last:
            continue
        reads = delta(first, last, prefix+'_reads')
        if not reads:
            continue
        span = delta(first, last, prefix+'_stamp_us') / 1e6
        if span <= 0:
            continue
        result['scheduler'][phase] = {'sample_span_seconds': round(span, 6), 'new_reads': reads,
            'new_errors': delta(first, last, prefix+'_errors'),
            'cpu_seconds': round(delta(first, last, prefix+'_cpu_us')/1e6, 6),
            'ready_wait_seconds': round(delta(first, last, prefix+'_wait_us')/1e6, 6),
            'timeslices': delta(first, last, prefix+'_slices')}
    return result
