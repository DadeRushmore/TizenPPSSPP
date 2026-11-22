"""Summarize current sessions only; historical log tails are not extra runs."""
import argparse
import datetime as dt
import hashlib
import json
import re
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('directory', type=Path)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
sessions = {}
for path in sorted(args.directory.glob('report-*.txt')):
    content = path.read_text(encoding='utf-8-sig')
    header = content.split('Historical files below', 1)[0]
    session = re.search(r'^SESSION id=(\w+).*?game_file=(.*?) selected_core=(\S+)', header, re.M)
    if not session:
        continue
    sid, game, core = session.groups()
    fields = dict(re.findall(r'\b([a-z][a-z0-9_]*)=([^\s]+)', header))
    result = sessions.setdefault(sid, {'session': sid, 'game': game, 'core': core, 'files': []})
    result['files'].append({'name': path.name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    result['header_metrics'] = fields
    # Limit samples to this session's graphics log, before retained previous logs.
    sections = dict(re.findall(r'^--- (.*?) \(bounded tail\) ---\n(.*?)(?=^--- |\Z)', content, re.M | re.S))
    startup = sections.get('graphics-startup.log', '')
    if not re.search(r'SESSION id=' + re.escape(sid) + r'\b', startup):
        continue
    section = startup + '\n' + '\n'.join(sections.get(name, '') for name in
        ('graphics-test.part2.log', 'graphics-test.part1.log', 'graphics-test.log'))
    samples = []
    for match in re.finditer(r'^(\S+Z) CORE_PROGRESS guest_ms=(\d+) actual_flips=(\d+)', section, re.M):
        stamp, guest, flips = match.groups()
        samples.append((dt.datetime.fromisoformat(stamp.replace('Z', '+00:00')).timestamp(), int(guest), int(flips)))
    samples = sorted(set(samples))
    if len(samples) >= 2:
        end = samples[-1]
        start = min(samples[:-1], key=lambda s: abs(end[0] - s[0] - 60))
        elapsed = end[0] - start[0]
        if elapsed > 0 and end[1] >= start[1] and end[2] >= start[2]:
            result['last_window'] = {'wall_seconds': round(elapsed, 3),
                'guest_speed_percent': round((end[1] - start[1]) / elapsed / 10, 2),
                'game_flips_per_second': round((end[2] - start[2]) / elapsed, 2),
                'note': 'Approximately last 60 seconds of current log; can include menu time.'}
args.output.write_text(json.dumps({'sessions': list(sessions.values())}, indent=2) + '\n', encoding='utf-8')
for result in sessions.values():
    print(result['game'], result['session'][:8], result.get('last_window'),
          'recent cadence:', result['header_metrics'].get('run_cadence_pct'))
