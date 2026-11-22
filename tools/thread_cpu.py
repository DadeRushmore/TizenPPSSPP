"""Per-task CPU deltas; identities include starttime to avoid TID reuse."""
from urllib.parse import unquote

def parse_tasks(text):
    result = {}
    for item in text.split(','):
        fields = item.split(':', 4)
        if len(fields) != 5:
            continue
        try:
            tid, start, user, system = map(int, fields[:4])
        except ValueError:
            continue
        if min(tid, start, user, system) < 0:
            continue
        result[(tid, start)] = (user, system, unquote(fields[4]))
    return result

def summarize_threads(first, last, hz, seconds):
    if hz <= 0 or seconds <= 0:
        return None
    rows = []
    for identity in first.keys() & last.keys():
        before, after = first[identity], last[identity]
        user, system = after[0]-before[0], after[1]-before[1]
        if min(user, system) < 0:
            continue
        rows.append({'tid': identity[0], 'start_ticks': identity[1], 'name': after[2],
                     'user_seconds': user/hz, 'system_seconds': system/hz,
                     'cpu_seconds': (user+system)/hz,
                     'one_core_percent': round((user+system)/hz/seconds*100, 2)})
    return {'clock_hz': hz, 'seconds': seconds,
            'new_or_reused_threads': len(last.keys()-first.keys()),
            'exited_or_reused_threads': len(first.keys()-last.keys()),
            'matched_cpu_seconds': sum(row['cpu_seconds'] for row in rows),
            'threads': sorted(rows, key=lambda row: row['cpu_seconds'], reverse=True)}
