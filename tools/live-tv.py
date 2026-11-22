"""Paired LAN receiver and command client for TizenPPSSPP. Python standard library."""
import argparse
import base64
import datetime as dt
import hashlib
import hmac
import importlib.util
import ipaddress
import json
import os
from pathlib import Path
import secrets
import select
import socket
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
MAX_PAYLOAD, MAX_FRAME = 32768, 44000


def private_ipv4(host):
    ip = ipaddress.IPv4Address(host)
    return any(ip in ipaddress.IPv4Network(n) for n in
               ('10.0.0.0/8', '172.16.0.0/12', '192.168.0.0/16', '127.0.0.0/8', '169.254.0.0/16'))


def load_pairing(path):
    if path.stat().st_size > 4096:
        raise ValueError('Pairing file too large')
    lines = path.read_text(encoding='utf-8').strip().splitlines()
    if len(lines) != 4 or lines[0] != 'TIZENPSP_LIVE_V1' or not all(
            line.startswith(prefix) for line, prefix in zip(lines[1:], ('host=', 'port=', 'key='))):
        raise ValueError('Invalid pairing file')
    host, port, key = lines[1][5:], int(lines[2][5:]), bytes.fromhex(lines[3][4:])
    if not private_ipv4(host) or not 1024 <= port <= 65535 or len(key) != 32:
        raise ValueError('Invalid LAN pairing settings')
    return host, port, key


def mac(key, text):
    return hmac.new(key, text.encode('utf-8'), hashlib.sha256).digest()


def pack(key, direction, sequence, payload):
    data = payload.encode('utf-8')
    if len(data) > MAX_PAYLOAD:
        raise ValueError('Payload too large')
    body = str(sequence) + ' ' + base64.b64encode(data).decode('ascii')
    return (body + ' ' + mac(key, direction + '|' + body).hex() + '\n').encode('ascii')


def unpack(key, direction, sequence, frame):
    fields = frame.split(' ')
    if len(fields) != 3 or fields[0] != str(sequence) or not hmac.compare_digest(
            fields[2], mac(key, direction + '|' + ' '.join(fields[:2])).hex()):
        raise ValueError('Signature/sequence rejected')
    payload = base64.b64decode(fields[1], validate=True)
    if len(payload) > MAX_PAYLOAD:
        raise ValueError('Payload too large')
    return payload.decode('utf-8')


class Reader:
    def __init__(self, connection):
        self.connection, self.buffer = connection, bytearray()

    def line(self, limit=MAX_FRAME):
        while True:
            at = self.buffer.find(b'\n')
            if at >= 0:
                if at > limit:
                    raise ValueError('Frame too large')
                line = bytes(self.buffer[:at])
                del self.buffer[:at + 1]
                if any(c < 32 or c > 126 for c in line):
                    raise ValueError('Invalid frame')
                return line.decode('ascii')
            if len(self.buffer) > limit:
                raise ValueError('Frame too large')
            data = self.connection.recv(4096)
            if not data:
                raise EOFError('Connection closed')
            self.buffer.extend(data)


def authenticate(connection, reader, key):
    pc = secrets.token_hex(32)
    connection.sendall(('CHALLENGE ' + pc + '\n').encode('ascii'))
    fields = reader.line(200).split(' ')
    if len(fields) != 3 or fields[0] != 'HELLO' or len(fields[1]) != 64 or any(
            c not in '0123456789abcdef' for c in fields[1]):
        raise ValueError('Invalid handshake')
    context = pc + '|' + fields[1]
    if not hmac.compare_digest(fields[2], mac(key, 'TV|' + context).hex()):
        raise ValueError('Pairing rejected')
    connection.sendall(('WELCOME ' + mac(key, 'PC|' + context).hex() + '\n').encode('ascii'))
    return mac(key, 'SESSION|' + context)


def atomic_json(path, value):
    temp = path.with_suffix('.tmp')
    temp.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')
    os.replace(temp, path)


def queue_command(state, command):
    status = json.loads((state / 'status.json').read_text(encoding='utf-8'))
    if not status.get('connected') or time.time() - status['updated'] > 6:
        raise ValueError('No current TV connection. Enable Live connection on the TV first.')
    folder = state / 'commands'
    folder.mkdir(exist_ok=True)
    if len(list(folder.glob('*.json'))) >= 32:
        raise ValueError('Command queue full')
    path = folder / (uuid.uuid4().hex + '.json')
    atomic_json(path, dict(connection=status['connection'], command=command))
    return path.name


def command_valid(command):
    fields = command.split(' ')
    return command in ('STATUS', 'CANCEL') or (len(fields) == 3 and fields[0] == 'AB'
        and fields[1].isdigit() and 10 <= int(fields[1]) <= 120 and fields[2] in ('ABBA', 'BAAB'))


def serve(state):
    spec = importlib.util.spec_from_file_location('live_analysis', Path(__file__).with_name('analyze-live.py'))
    analysis = importlib.util.module_from_spec(spec); spec.loader.exec_module(analysis)
    host, port, secret = load_pairing(state / 'tizenpsp-live.txt')
    commands = state / 'commands'; commands.mkdir(exist_ok=True)
    captures = state / 'captures'; captures.mkdir(exist_ok=True)
    status_path = state / 'status.json'
    status = dict(connected=False, updated=time.time(), host=host, port=port)
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
        listener.bind((host, port)); listener.listen(2); listener.settimeout(1)
        atomic_json(status_path, status)
        print(f'Listening on {host}:{port}. Enable Live connection on the TV. Ctrl+C stops.', flush=True)
        try:
            while True:
                try:
                    connection, peer = listener.accept()
                except socket.timeout:
                    continue
                with connection:
                    try:
                        if not private_ipv4(peer[0]):
                            raise ValueError('LAN peers only')
                        connection.settimeout(3); connection.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                        reader = Reader(connection); key = authenticate(connection, reader, secret)
                        identity = uuid.uuid4().hex
                        folder = captures / (dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dT%H%M%SZ-') + identity[:8])
                        folder.mkdir()
                        status = dict(connected=True, connection=identity, peer=peer[0], updated=time.time(), capture=str(folder), last_response='Connected')
                        atomic_json(status_path, status)
                        print('TV connected. Saving to ' + str(folder), flush=True)
                        tx = rx = part = size = 0
                        capture_events = []; comparisons = 0
                        def save_comparison():
                            nonlocal comparisons
                            comparisons += 1
                            path = folder / f'comparison-{comparisons:03d}.json'
                            atomic_json(path, dict(captures=analysis.analyze_payloads(capture_events)))
                            status['comparison'] = str(path)
                            print('Comparison saved: ' + str(path), flush=True)
                        heartbeat = saved = last_rx = time.monotonic()
                        output = (folder / f'live-{part:03d}.jsonl').open('w', encoding='utf-8')
                        try:
                            while True:
                                now = time.monotonic()
                                for path in sorted(commands.glob('*.json'))[:8]:
                                    try:
                                        if path.stat().st_size > 1024:
                                            continue
                                        request = json.loads(path.read_text(encoding='utf-8'))
                                        command = request.get('command', '')
                                        if request.get('connection') == identity and isinstance(command, str) and command_valid(command):
                                            tx += 1; connection.sendall(pack(key, 'P', tx, command))
                                    except (ValueError, UnicodeError):
                                        status['last_response'] = 'Rejected malformed local command'
                                    finally:
                                        path.unlink(missing_ok=True)
                                if now - heartbeat >= 2:
                                    tx += 1; connection.sendall(pack(key, 'P', tx, 'PING')); heartbeat = now
                                if reader.buffer or select.select([connection], [], [], .05)[0]:
                                    rx += 1; payload = unpack(key, 'T', rx, reader.line()); last_rx = time.monotonic()
                                    if payload != 'PONG':
                                        row = json.dumps(dict(received_utc=dt.datetime.now(dt.timezone.utc).isoformat(), connection=identity, payload=payload), ensure_ascii=False) + '\n'
                                        output.write(row); size += len(row.encode('utf-8'))
                                        if not payload.startswith('LOG\n'):
                                            status['last_response'] = payload.split('\n')[0]
                                            print(status['last_response'], flush=True); output.flush()
                                        if payload.startswith('AB_PLAN '):
                                            capture_events.clear()
                                        if payload.startswith('AB_'):
                                            capture_events.append(payload)
                                            if len(capture_events) > 12:
                                                raise ValueError('Capture event limit')
                                            if payload.startswith(('AB_DONE ', 'AB_ABORT ')):
                                                save_comparison(); capture_events.clear()
                                        if size >= 64 * 1024 * 1024:
                                            output.close(); part += 1; size = 0
                                            output = (folder / f'live-{part:03d}.jsonl').open('w', encoding='utf-8')
                                if now - last_rx > 12:
                                    raise TimeoutError('TV heartbeat expired')
                                if now - saved >= 1:
                                    status['updated'] = time.time(); atomic_json(status_path, status); output.flush(); saved = now
                        finally:
                            output.close()
                            if capture_events:
                                save_comparison()
                    except (OSError, ValueError, EOFError) as error:
                        # Do not echo peer-supplied text, signatures or keys.
                        print('Disconnected or pairing rejected: ' + type(error).__name__, flush=True)
                    finally:
                        status['connected'] = False; status['updated'] = time.time(); atomic_json(status_path, status)
        finally:
            status['connected'] = False; status['updated'] = time.time(); atomic_json(status_path, status)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--state', type=Path, default=ROOT / 'build/live')
    sub = parser.add_subparsers(dest='action', required=True)
    pairing = sub.add_parser('pair', help='Create a one-time USB /PSP pairing file')
    pairing.add_argument('--host', required=True, help='This PC private IPv4 address')
    pairing.add_argument('--port', type=int, default=27941)
    sub.add_parser('serve', help='Receive logs and forward bounded experiment commands')
    command = sub.add_parser('command')
    command.add_argument('command', choices=['status', 'cancel', 'ab'])
    command.add_argument('--seconds', type=int, default=30)
    command.add_argument('--order', choices=['ABBA', 'BAAB'], default='ABBA')
    sub.add_parser('status', help='Show receiver state and last TV reply')
    args = parser.parse_args(); state = args.state.resolve(); state.mkdir(parents=True, exist_ok=True)
    if args.action == 'pair':
        if not private_ipv4(args.host) or not 1024 <= args.port <= 65535:
            parser.error('Use a local IPv4 address and port 1024-65535')
        path = state / 'tizenpsp-live.txt'
        key = load_pairing(path)[2] if path.exists() else secrets.token_bytes(32)
        path.write_text(f'TIZENPSP_LIVE_V1\nhost={args.host}\nport={args.port}\nkey={key.hex()}\n', encoding='ascii')
        print(f'Pairing file: {path}\nCopy it to USB /PSP once. Do not share the file or include it in reports.')
    elif args.action == 'serve':
        serve(state)
    elif args.action == 'status':
        print((state / 'status.json').read_text(encoding='utf-8'))
    else:
        command = f'AB {args.seconds} {args.order}' if args.command == 'ab' else args.command.upper()
        if not command_valid(command):
            parser.error('A/B duration must be 10-120 seconds')
        print('Queued ' + queue_command(state, command) + '; use status to read the TV reply.')


if __name__ == '__main__':
    try:
        main()
    except KeyboardInterrupt:
        print('Receiver stopped.')
    except (OSError, ValueError) as error:
        raise SystemExit(str(error))
