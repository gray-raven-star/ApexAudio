"""Limited RE Pak v4 (48-byte entries) recipe; reference: Ekey/REE.PAK.Tool.
Only explicitly named metadata resources are written. No game path lists are bundled.
"""
import argparse
import hashlib
from pathlib import Path
import re
import struct
from common import MAX_ENTRIES, MAX_RESOURCE, decode, read_at, run, write_resource


def rotate(value, count):
    return ((value << count) | (value >> (32 - count))) & 0xffffffff


def path_hash(name):
    data = name.encode("utf-16le")
    value = 0xffffffff
    for at in range(0, len(data) // 4 * 4, 4):
        part = int.from_bytes(data[at:at + 4], "little") * 0xcc9e2d51 & 0xffffffff
        value ^= rotate(part, 15) * 0x1b873593 & 0xffffffff
        value = (rotate(value, 13) * 5 + 0xe6546b64) & 0xffffffff
    tail = data[len(data) // 4 * 4:]
    if tail:
        value ^= rotate(int.from_bytes(tail, "little") * 0xcc9e2d51 & 0xffffffff, 15) * 0x1b873593 & 0xffffffff
    value ^= len(data)
    value ^= value >> 16
    value = value * 0x85ebca6b & 0xffffffff
    value ^= value >> 13
    value = value * 0xc2b2ae35 & 0xffffffff
    return value ^ (value >> 16)


def inspect(args):
    names = args.resource
    if not names or len(names) > 256:
        raise ValueError("Specify 1..256 exact resource paths using --resource")
    candidates = {}
    for name in names:
        key = (path_hash(name.lower()), path_hash(name.upper()))
        if key in candidates and candidates[key] != name:
            raise ValueError("Ambiguous supplied path hashes")
        candidates[key] = name
    with open(args.container, "rb") as source:
        magic, major, minor, flags, count, fingerprint = struct.unpack("<IBBHII", read_at(source, 0, 16))
        if magic != 0x414b504b or (major, minor) != (4, 0) or flags not in (0, 8):
            raise ValueError("Only RE Pak v4.0 with flags 0 or 8 is supported")
        if not 0 < count <= MAX_ENTRIES:
            raise ValueError("Index count exceeds limit")
        table = read_at(source, 16, count * 48)
        dependency = None
        if flags == 8:
            if not args.cipher_reference:
                raise ValueError("Encoded index requires --cipher-reference to the reviewed public PakCipher.cs; see container-metadata.md")
            reference = Path(args.cipher_reference)
            if reference.stat().st_size > 1024 * 1024:
                raise ValueError("Cipher reference exceeds limit")
            text = reference.read_text(encoding="utf-8-sig")
            match = re.search(r"lpModulus\s*=\s*\{([^}]+)\}", text)
            if not match:
                raise ValueError("Unsupported public cipher reference layout")
            modulus = bytes(int(v, 16) for v in re.findall(r"0x([0-9a-fA-F]{2})", match[1]))
            if len(modulus) != 128:
                raise ValueError("Expected the reviewed 128-byte public modulus")
            wrapped = read_at(source, 16 + count * 48, 128)
            key = pow(int.from_bytes(wrapped, "little"), 65537, int.from_bytes(modulus, "little")).to_bytes(129, "little")
            table = bytes(v ^ ((i + key[i % 32] * key[i % 29]) & 255) for i, v in enumerate(table))
            dependency = {"purpose": "public-format-reference", "sha256": hashlib.sha256(reference.read_bytes()).hexdigest()}
        resources = []
        for at in range(count):
            lower, upper, offset, packed_size, size, attributes, checksum = struct.unpack_from("<IIQQQQQ", table, at * 48)
            name = candidates.get((lower, upper))
            if name is None:
                continue
            if max(size, packed_size) > MAX_RESOURCE:
                raise ValueError("Requested resource exceeds 16 MiB")
            if (attributes >> 16) & 255:
                raise ValueError("Resource-level encryption is unsupported")
            method = {0: "none", 1: "deflate", 2: "zstd"}.get(attributes & 15)
            if method is None:
                raise ValueError("Unsupported RE compression method")
            data = decode(read_at(source, offset, packed_size, MAX_RESOURCE), method, size)
            resources.append({"resourcePath": name, "offset": offset, "compressedSize": packed_size, "size": size, "compression": method,
                "containerEntry": at, "sourceLayer": args.layer, "checksum": f"{checksum:016X}", **write_resource(args.output, name, data)})
        return {"format": "re-pak", "version": "4.0", "flags": flags, "fingerprint": fingerprint, "entryCount": count,
            "indexSha256": hashlib.sha256(table).hexdigest(), "dependency": dependency, "resources": resources,
            "missingRequests": [n for n in names if n not in {r["resourcePath"] for r in resources}],
            "limitations": "Exact supplied paths only. Patch ordering is supplied by the author; this tool does not infer game versions or bus semantics."}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("container")
    parser.add_argument("--output", required=True)
    parser.add_argument("--resource", action="append", required=True)
    parser.add_argument("--layer", default="unspecified")
    parser.add_argument("--cipher-reference")
    raise SystemExit(run(parser, inspect))
