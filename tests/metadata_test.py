"""Anonymous synthetic format fixtures; never opens a game installation or a process."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest
import zlib

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools' / 'Metadata'))
import read_re_pak
import read_ue_pak
from merge_layers import merge
from name_candidates import wwise_id, match
from common import decode


def fstring(value):
    data = value.encode() + b'\0'
    return struct.pack('<i', len(data)) + data


def ue_fixture(data, compressed=False):
    packed = zlib.compress(data) if compressed else data
    method, blocks = (1, 1) if compressed else (0, 0)
    header_size = 73 if compressed else 53
    disk = struct.pack('<QQQI20s', 0, len(packed), len(data), method, b'\0' * 20)
    if compressed:
        disk += struct.pack('<IQQ', 1, header_size, header_size + len(packed))
    disk += struct.pack('<BI', 0, 0)
    bits = 0xc0000000 | ((1 << 29) if compressed else 0) | (method << 23) | (blocks << 6)
    encoded = struct.pack('<III', bits, 0, len(data)) + (struct.pack('<I', len(packed)) if compressed else b'')
    directory = struct.pack('<i', 1) + fstring('') + struct.pack('<i', 1) + fstring('example.bnk') + struct.pack('<i', 0)

    def index(directory_at):
        return (fstring('../../../Example/') + struct.pack('<iQI', 1, 0, 1) + b'\0' * 36 + struct.pack('<Iqq', 1, directory_at, len(directory))
                + hashlib.sha1(directory).digest() + struct.pack('<i', len(encoded)) + encoded + struct.pack('<i', 0))

    position = header_size + len(packed)
    body = index(position + len(index(0)))
    methods = b'Zlib\0'.ljust(32, b'\0') + b'\0' * 128
    footer = b'\0' * 17 + struct.pack('<IIqq', 0x5a6f12e1, 11, position, len(body)) + hashlib.sha1(body).digest() + methods
    return disk + packed + body + directory + footer


class MetadataTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def test_re_plain_and_encoded_index(self):
        name, data = 'natives/example/init.bnk', b'BKHD-anonymous-fixture'
        for flags in (0, 8):
            target = self.root / f're-{flags}.pak'
            offset = 16 + 48 + (128 if flags else 0)
            table = struct.pack('<IIQQQQQ', read_re_pak.path_hash(name.lower()), read_re_pak.path_hash(name.upper()), offset, len(data), len(data), 0, 0)
            reference = self.root / 'public-fixture.cs'
            reference.write_text('lpModulus = {' + ','.join(['0xff'] * 128) + '};')
            encoded = bytes(value ^ (i & 255) for i, value in enumerate(table)) if flags else table
            target.write_bytes(struct.pack('<IBBHII', 0x414b504b, 4, 0, flags, 1, 0) + encoded + (b'\0' * 128 if flags else b'') + data)
            output = self.root / f'out-{flags}'
            output.mkdir()
            report = read_re_pak.inspect(argparse.Namespace(container=target, resource=[name], output=output, layer='base', cipher_reference=reference))
            self.assertEqual(Path(report['resources'][0]['outputPath']).read_bytes(), data)
            self.assertEqual(report['missingRequests'], [])

    def test_ue_plain_and_single_zlib_block_without_block_size_hint(self):
        data = b'bounded-bank-metadata' * 87
        for compressed in (False, True):
            target = self.root / f'ue-{compressed}.pak'
            target.write_bytes(ue_fixture(data, compressed))
            output = self.root / f'ue-out-{compressed}'
            output.mkdir()
            report = read_ue_pak.inspect(argparse.Namespace(container=target, target=['example.bnk'], output=output, layer='base', ooz=None, decoder_sha256=None))
            self.assertEqual(Path(report['resources'][0]['outputPath']).read_bytes(), data)
            changed = bytearray(target.read_bytes())
            changed[-221 - 1] ^= 1  # Corrupt the directory index, whose SHA1 must be checked.
            target.write_bytes(changed)
            with self.assertRaisesRegex(ValueError, 'SHA1'):
                read_ue_pak.inspect(argparse.Namespace(container=target, target=['example.bnk'], output=output, layer='base', ooz=None, decoder_sha256=None))

    def test_bounds_and_explicit_patch_priority(self):
        compressor = zlib.compressobj(wbits=-15)
        packed = compressor.compress(b'x' * 1000) + compressor.flush()
        with self.assertRaises(ValueError):
            decode(packed, 'deflate', 10)
        reports = []
        for layer in ['base', 'patch']:
            path = self.root / (layer + '.json')
            path.write_text(json.dumps({'schemaVersion': 1, 'outcome': 'completed', 'source': layer,
                'resources': [{'resourcePath': 'Example/Bank.bnk', 'sha256': layer, 'sourceLayer': layer}]}))
            reports.append(path)
        result = merge(reports)
        self.assertEqual(result['resources'][0]['source'], 'patch')
        self.assertEqual(len(result['overrides']), 1)

    def test_name_candidates_preserve_uncertainty(self):
        definition, words = self.root / 'game.json', self.root / 'words.txt'
        definition.write_text(json.dumps({'buses': [{'id': wwise_id('ExampleCandidate')}]}))
        words.write_text('ExampleCandidate\nexamplecandidate\nUnrelated\n')
        result = match(definition, words)
        self.assertEqual(len(result['matches']), 1)
        self.assertTrue(result['matches'][0]['multipleCandidates'])
        self.assertFalse(result['matches'][0]['auditionConfirmed'])


if __name__ == '__main__':
    unittest.main()
