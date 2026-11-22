"""Summarize cumulative logical framebuffer operations; these are not GPU timings."""
FIELDS = ('fb_uploads', 'fb_cached_uploads', 'fb_blocking_readbacks',
          'fb_nonblocking_readbacks', 'fb_depth_copies')


def summarize_framebuffer(first, last, seconds):
    if seconds <= 0:
        raise ValueError('Framebuffer window must have positive elapsed time')
    missing = [name for name in FIELDS if name not in first or name not in last]
    if missing:
        return {'available': False, 'missing_fields': missing}
    counts = {name: (last[name] - first[name]) & 0xffffffff for name in FIELDS}
    upload_requests = counts['fb_uploads'] + counts['fb_cached_uploads']
    return {'available': True, 'counts': counts,
            'per_second': {name: round(value / seconds, 3) for name, value in counts.items()},
            'upload_cache_hit_percent': round(100 * counts['fb_cached_uploads'] / upload_requests, 2) if upload_requests else None,
            'scope': 'Logical framebuffer operations; no GPU stall time measured.'}
