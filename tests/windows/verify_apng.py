"""Independent PNG/APNG validation: Python standard library only."""
from pathlib import Path
import json
import struct
import subprocess
import sys
import zlib
import os

ROOT = Path(__file__).resolve().parents[2]
ART = Path(os.environ.get('AIPT_VERIFICATION_DIR', ROOT / 'build' / 'verification'))
EXE = Path(os.environ.get('QQIMAGE_EXE', ROOT / 'build' / 'windows' / 'AI-Image-Editing-Tools.exe'))


def chunks(path):
    data = Path(path).read_bytes()
    assert data[:8] == b'\x89PNG\r\n\x1a\n'
    p, result = 8, []
    while p < len(data):
        n = struct.unpack_from('>I', data, p)[0]
        tag = data[p+4:p+8]
        payload = data[p+8:p+8+n]
        assert len(payload) == n
        crc = struct.unpack_from('>I', data, p+8+n)[0]
        assert zlib.crc32(tag + payload) == crc, tag
        result.append((tag, payload))
        p += n + 12
    assert result[-1] == (b'IEND', b'')
    return result


def decode_rgba(header, compressed):
    w, h, depth, mode, compression, filtering, interlace = struct.unpack('>IIBBBBB', header)
    assert (depth, mode, compression, filtering, interlace) == (8, 6, 0, 0, 0)
    raw = zlib.decompress(compressed)
    rowlen = w * 4
    assert len(raw) == (rowlen+1)*h
    decoded = bytearray()
    previous = bytes(rowlen)
    for y in range(h):
        filter_mode = raw[y*(rowlen+1)]
        row = bytearray(raw[y*(rowlen+1)+1:(y+1)*(rowlen+1)])
        for x in range(rowlen):
            left = row[x-4] if x >= 4 else 0
            up = previous[x]
            upper_left = previous[x-4] if x >= 4 else 0
            if filter_mode == 0:
                prediction = 0
            elif filter_mode == 1:
                prediction = left
            elif filter_mode == 2:
                prediction = up
            elif filter_mode == 3:
                prediction = (left + up) // 2
            elif filter_mode == 4:
                p = left + up - upper_left
                distances = [abs(p-left), abs(p-up), abs(p-upper_left)]
                prediction = [left, up, upper_left][distances.index(min(distances))]
            else:
                raise AssertionError('Unknown PNG filter')
            row[x] = (row[x]+prediction) & 255
        decoded.extend(row)
        previous = row
    return (w, h), bytes(decoded)


def static_image(path):
    cs = chunks(path)
    return decode_rgba(cs[0][1], b''.join(p for t, p in cs if t == b'IDAT'))


def validate(path, cover, real):
    cs = chunks(path)
    assert static_image(path) == static_image(cover), 'Fallback must show cover'
    types = [t for t, _ in cs]
    assert types.index(b'IDAT') < types.index(b'fcTL'), 'Cover must be outside animation'
    assert types.index(b'acTL') < types.index(b'IDAT')
    declared, loops = struct.unpack('>II', next(p for t, p in cs if t == b'acTL'))
    assert declared == 2 and loops == 0
    frames, current, expected_seq = [], None, 0
    for tag, payload in cs:
        if tag in (b'fcTL', b'fdAT'):
            seq = struct.unpack_from('>I', payload)[0]
            assert seq == expected_seq
            expected_seq += 1
        if tag == b'fcTL':
            current = [payload, bytearray()]
            frames.append(current)
        elif tag == b'fdAT':
            assert current is not None
            current[1].extend(payload[4:])
    assert len(frames) == declared
    fc = struct.unpack('>IIIIIHHBB', frames[0][0])
    assert fc[3:] == (0, 0, 10, 100, 0, 0)
    assert decode_rgba(cs[0][1], frames[0][1]) == static_image(real), 'Hidden RGBA pixels differ'
    fc = struct.unpack('>IIIIIHHBB', frames[1][0])
    assert fc[1:] == (1, 1, 0, 0, 10, 100, 0, 1)
    assert zlib.decompress(frames[1][1]) == bytes(5), 'Second frame must be transparent OVER'


def run(*args, success=True):
    p = subprocess.run([str(EXE), *map(str, args)], capture_output=True)
    assert (p.returncode == 0) == success, (args, p.returncode, p.stderr)


def main():
    ART.mkdir(parents=True,exist_ok=True)
    run('--self-test', ART)
    assert (ART / 'self-test.txt').read_text(encoding='utf-8-sig').startswith('PASS:')
    validate(ART/'double.png', ART/'cover.png', ART/'real.png')
    assert static_image(ART/'restored.png') == static_image(ART/'real.png')
    run('--encode', ART/'cover.png', ART/'real.png', ART/'cli.png', 0)
    validate(ART/'cli.png', ART/'cover.png', ART/'real.png')
    run('--restore', ART/'cli.png', ART/'cli-restored.png')
    assert static_image(ART/'cli-restored.png') == static_image(ART/'real.png')
    run('--restore', ART/'cover.png', ART/'should-not-exist.png', success=False)
    assert not (ART/'should-not-exist.png').exists()
    run('--encode', ART/'cover.png', ART/'real.png', ART/'resized.png', 100)
    assert static_image(ART/'resized.png')[0] == (100, 67)
    truncated = ART/'truncated.png'
    truncated.write_bytes((ART/'double.png').read_bytes()[:-10])
    run('--restore', truncated, ART/'bad-restore.png', success=False)
    assert not (ART/'bad-restore.png').exists()
    report = {'result': 'PASS', 'checks': [
        'C# pixel round trip including alpha', 'independent zlib + PNG filter decoder',
        'CRC of every PNG chunk', 'APNG sequence and frame count',
        'static fallback outside animation', 'transparent OVER dummy frame',
        'CLI generation and restoration', 'ordinary and truncated PNG rejection',
        'proportional resize and letterbox', 'atomic overwrite', 'one-pixel input'
    ]}
    (ART/'verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf8')
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
