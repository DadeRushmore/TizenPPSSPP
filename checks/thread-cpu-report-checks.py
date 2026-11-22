import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'tools'))
from thread_cpu import parse_tasks, summarize_threads
a=parse_tasks('42:100:100:20:GL%20worker,43:101:20:5:old,bad')
b=parse_tasks('42:100:120:30:GL%20worker,43:102:100:50:new')
r=summarize_threads(a,b,100,2)
assert r['matched_cpu_seconds']==0.3
assert r['threads'][0]['one_core_percent']==15
assert r['threads'][0]['name']=='GL worker'
assert r['new_or_reused_threads']==r['exited_or_reused_threads']==1
assert summarize_threads(a,b,0,2) is None
assert not parse_tasks('1:1:-1:0:bad')
print('PASS: task CPU units, name decoding, TID reuse and unavailable clock rate.')
