"""Check operation-site coverage and report arithmetic without a GPU/TV claim."""
import re
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / 'tools'))
from framebuffer_counts import FIELDS, summarize_framebuffer

first = dict.fromkeys(FIELDS, 10)
last = dict(zip(FIELDS, (30, 70, 16, 14, 12)))
result = summarize_framebuffer(first, last, 2)
assert result['counts']['fb_uploads'] == 20
assert result['per_second']['fb_uploads'] == 10
assert result['per_second']['fb_cached_uploads'] == 30
assert result['upload_cache_hit_percent'] == 75
assert result['counts']['fb_blocking_readbacks'] == 6
assert result['counts']['fb_nonblocking_readbacks'] == 4
assert result['counts']['fb_depth_copies'] == 2
assert summarize_framebuffer(first, first, 2)['upload_cache_hit_percent'] is None
wrapped_first = dict(first, fb_uploads=0xffffffff)
wrapped_last = dict(first, fb_uploads=1)
assert summarize_framebuffer(wrapped_first, wrapped_last, 1)['counts']['fb_uploads'] == 2
assert not summarize_framebuffer({}, {}, 2)['available']
assert summarize_framebuffer({}, last, 2)['missing_fields'] == list(FIELDS)
assert not summarize_framebuffer(first, {k: v for k, v in last.items() if k != 'fb_depth_copies'}, 2)['available']
try:
    summarize_framebuffer(first, last, 0)
    raise AssertionError('zero-length window accepted')
except ValueError:
    pass

source = (root / 'vendor/ppsspp/GPU/Common/FramebufferManagerCommon.cpp').read_text(encoding='utf-8')
sites = {'numUploads': ('Upload', 2), 'numCachedUploads': ('CachedUpload', 1),
         'numBlockingReadbacks': ('BlockingReadback', 2), 'numReadbacks': ('NonblockingReadback', 1),
         'numDepthCopies': ('DepthCopy', 2)}
for field, (event, count) in sites.items():
    increment = 'gpuStats.perFrame.' + field + '++;'
    assert source.count(increment) == count
    guarded = re.escape(increment) + r'\n#ifdef TIZENPSP_ARM_JIT\n\s*TizenFramebufferTransfer\(TizenFramebufferEvent::' + event + r'\);\n#endif'
    assert len(re.findall(guarded, source)) == count, (field, 'missing or unguarded operation counter')
assert re.search(r'#ifdef TIZENPSP_ARM_JIT\n#include "tizenpsp_jit_diagnostics.h"\n#endif', source)
print('PASS: framebuffer operation coverage/ARM-JIT guards, counter rates, cache-hit denominator, modulo wrap and missing-old-report evidence.')
