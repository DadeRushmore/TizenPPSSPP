"""Production C# client + Python receiver on loopback; synthetic performance data."""
import copy
import importlib.util
import json
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time

root = Path(__file__).resolve().parents[1]


def module(name, file):
    spec = importlib.util.spec_from_file_location(name, root / file)
    value = importlib.util.module_from_spec(spec); spec.loader.exec_module(value)
    return value


wire = module('live_tv', 'tools/live-tv.py')
analysis = module('analyze_live', 'tools/analyze-live.py')
key = bytes([0x0b] * 20)
assert wire.mac(key, 'Hi There').hex() == 'b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7'
assert wire.unpack(key, 'T', 1, wire.pack(key, 'T', 1, '日本語\ncheck').decode().strip()) == '日本語\ncheck'
for direction, sequence in [('P', 1), ('T', 2)]:
    try:
        wire.unpack(key, direction, sequence, wire.pack(key, 'T', 1, 'x').decode().strip())
        raise AssertionError('Replay/direction accepted')
    except ValueError:
        pass

subprocess.run(['dotnet', 'build', str(root / 'checks/Check.csproj'), '-c', 'Release', '--nologo', '-v', 'quiet'], check=True, cwd=root)
with tempfile.TemporaryDirectory(prefix='live-integration-', dir=root / 'build') as temporary:
    state = Path(temporary).resolve(); assert state.is_relative_to((root / 'build').resolve())
    with socket.socket() as reserve:
        reserve.bind(('127.0.0.1', 0)); port = reserve.getsockname()[1]
    subprocess.run([sys.executable, str(root / 'tools/live-tv.py'), '--state', str(state), 'pair', '--host', '127.0.0.1', '--port', str(port)], check=True, stdout=subprocess.DEVNULL)
    log = (state / 'receiver.log').open('w', encoding='utf-8')
    receiver = subprocess.Popen([sys.executable, str(root / 'tools/live-tv.py'), '--state', str(state), 'serve'], cwd=root, stdout=log, stderr=subprocess.STDOUT)
    client = None
    try:
        deadline = time.monotonic() + 8
        while not (state / 'status.json').exists():
            assert receiver.poll() is None and time.monotonic() < deadline, 'Receiver startup'
            time.sleep(.05)
        # An unpaired LAN client must not establish a session or issue commands.
        with socket.create_connection(('127.0.0.1', port), timeout=3) as rogue:
            reader = wire.Reader(rogue); assert reader.line().startswith('CHALLENGE ')
            rogue.sendall(('HELLO ' + 'a' * 64 + ' ' + '0' * 64 + '\n').encode())
            assert rogue.recv(1) == b'', 'Unauthenticated client accepted'
        client = subprocess.Popen(['dotnet', str(root / 'checks/bin/Release/netcoreapp3.1/Check.dll'), '--live-fixture', str(state / 'tizenpsp-live.txt')], cwd=root, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        deadline = time.monotonic() + 10
        while not json.loads((state / 'status.json').read_text())['connected']:
            assert time.monotonic() < deadline and client.poll() is None, 'Fixture connection'
            time.sleep(.05)
        wire.queue_command(state, 'AB 10 ABBA')
        output, _ = client.communicate(timeout=25)
        assert client.returncode == 0, output
        print(output.strip())
        time.sleep(.2)
        payloads = [json.loads(line)['payload'] for file in sorted((state / 'captures').glob('*/live-*.jsonl')) for line in file.read_text(encoding='utf-8').splitlines()]
        results = analysis.analyze_payloads(payloads)
        assert len(results) == 1 and results[0]['usable_comparison'], results
        result = results[0]
        saved = list((state / 'captures').glob('*/comparison-*.json'))
        assert len(saved) == 1 and json.loads(saved[0].read_text())['captures'] == results, 'Automatic comparison publication'
        assert abs(result['observed_guest_speed_change_pct'] - 20) < .01, result
        assert abs(result['observed_cpu_cost_reduction_pct'] - 100 / 3) < .01, result
        assert all(p['fast_draws'] == 0 for p in result['phases'] if p['variant'] == 'A')
        for phase in result['phases']:
            hle = phase['sampled_inclusive_costs']['hle']
            assert abs(hle['estimated_inclusive_cpu_seconds'] - hle['calls'] * .0001) < 1e-8
        cases = []
        cases.append([p for p in payloads if not p.startswith('AB_DONE ')])
        cases.append(payloads + ['AB_ABORT id=' + result['id'] + ' reason=test'])
        original = next(p for p in payloads if p.startswith('AB_END '))
        for damaged in [original.replace('dropped=0', 'dropped=1'),
                        original.replace('command_queue_requested=0', 'command_queue_requested=1'),
                        original.replace('command_queue_supported=1', 'command_queue_supported=0'),
                        original.replace('resolution=480x272', 'resolution=960x544'),
                        original.replace('connection=1', 'connection=2'),
                        original.replace('overflow=0', 'overflow=1'),
                        original.replace('ready=1', 'ready=0')]:
            cases.append([damaged if p == original else p for p in payloads])
        cases.append(payloads + [original])
        for case in cases:
            rejected = analysis.analyze_payloads(case)
            assert len(rejected) == 1 and not rejected[0]['usable_comparison'], rejected
        artifact = dict(scope='Host-only synthetic workload; not TV/game performance',
                        checks=['Production C# client to Python receiver authentication and streaming; unpaired client rejected',
                                'Real receiver command queue drives production ABBA state machine',
                                'All four signed endpoint pairs received and analyzed',
                                'Receiver automatically publishes the complete comparison JSON',
                                'Known synthetic guest-speed and CPU-cost ratios recovered',
                                'Incomplete, aborted, duplicate, dropped, wrong-variant, unsupported, changed-settings, changed-connection, overflow and paused captures rejected'],
                        valid_phases=len(result['phases']), invalid_cases_rejected=len(cases))
        (root / 'docs/live-validation-v0243.json').write_text(json.dumps(artifact, indent=2) + '\n')
        print('PASS: complete cross-language capture and invalid-capture rejection.')
    finally:
        if client is not None and client.poll() is None:
            client.terminate(); client.wait(timeout=5)
        receiver.terminate(); receiver.wait(timeout=5); log.close()
