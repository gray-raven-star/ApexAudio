"""Bounded, read-only metadata helpers. No game identities or automatic downloads."""
import hashlib
import io
import json
from pathlib import Path
import struct
import sys
import zlib

MAX_INDEX = 64 * 1024 * 1024
MAX_RESOURCE = 16 * 1024 * 1024
MAX_ENTRIES = 500000


class Reader:
    def __init__(self, data):
        self.stream = io.BytesIO(data)

    def read(self, size):
        if not 0 <= size <= MAX_INDEX:
            raise ValueError("Metadata read exceeds limit")
        data = self.stream.read(size)
        if len(data) != size:
            raise ValueError("Truncated metadata")
        return data

    def value(self, fmt):
        return struct.unpack("<" + fmt, self.read(struct.calcsize("<" + fmt)))[0]

    def count(self):
        value = self.value("i")
        if not 0 <= value <= MAX_ENTRIES:
            raise ValueError("Entry count exceeds limit")
        return value

    def string(self):
        count = self.value("i")
        if not -65536 < count < 65536:
            raise ValueError("Invalid FString size")
        return self.read(count if count >= 0 else -count * 2).decode("utf-8" if count >= 0 else "utf-16le").rstrip("\0")


def read_at(stream, offset, size, limit=MAX_INDEX):
    if not 0 <= size <= limit or offset < 0:
        raise ValueError("Read range exceeds metadata limit")
    stream.seek(0, 2)
    if offset + size > stream.tell():
        raise ValueError("Read range exceeds source file")
    stream.seek(offset)
    data = stream.read(size)
    if len(data) != size:
        raise ValueError("Truncated source file")
    return data


def decode(packed, method, expected):
    if not 0 <= expected <= MAX_RESOURCE:
        raise ValueError("Requested resource exceeds 16 MiB")
    if method == "none":
        data = packed
    elif method == "deflate":
        decoder = zlib.decompressobj(-15)
        data = decoder.decompress(packed, expected + 1)
        if not decoder.eof or decoder.unconsumed_tail or decoder.unused_data:
            raise ValueError("Invalid or oversized DEFLATE resource")
    elif method == "zstd":
        import zstandard
        if zstandard.__version__ != "0.25.0":
            raise ValueError("Use the optional pinned zstandard==0.25.0 dependency")
        if zstandard.frame_content_size(packed) != expected:
            raise ValueError("Zstd frame must declare the verified bounded resource size")
        data = zstandard.ZstdDecompressor().decompress(packed, max_output_size=expected)
    else:
        raise ValueError("Unsupported compression: " + method)
    if len(data) != expected:
        raise ValueError("Decoded resource size mismatch")
    return data


def write_resource(output, resource_path, data):
    # A stable path hash prevents collisions between equal basenames and avoids archive traversal.
    leaf = Path(resource_path.replace("\\", "/")).name
    leaf = "".join(c if c.isalnum() or c in ".-_" else "_" for c in leaf)[:100] or "metadata.bin"
    target = Path(output).resolve() / (hashlib.sha256(resource_path.encode()).hexdigest()[:16] + "-" + leaf)
    if target.exists() and target.read_bytes() != data:
        raise ValueError("Existing output differs; choose a new output directory")
    if not target.exists():
        target.write_bytes(data)
    return {"outputPath": str(target), "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)}


def run(parser, execute):
    args = parser.parse_args()
    output = Path(args.output).resolve()
    report = {"schemaVersion": 1, "mode": "read-only-source", "source": str(Path(args.container).resolve()), "gameConnected": False}
    status = 0
    try:
        output.mkdir(parents=True, exist_ok=True)
        report.update(execute(args))
        report["outcome"] = "completed"
    except Exception as error:
        report.update(outcome="failed", error=str(error), type=type(error).__name__)
        status = 1
    try:
        (output / "metadata-report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    except OSError as error:
        report["reportWriteError"] = str(error)
        status = 1
    print(json.dumps(report, indent=2, ensure_ascii=False), file=sys.stderr if status else sys.stdout)
    return status
