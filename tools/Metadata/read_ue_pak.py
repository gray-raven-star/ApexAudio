"""Limited UE Pak v11 full-directory-index recipe. Format reference: CUE4Parse.
Supports plain and DEFLATE metadata; optional trusted OozDecompress ABI for Oodle.
"""
import argparse
import ctypes
import hashlib
from pathlib import Path
import struct
from common import MAX_ENTRIES, MAX_RESOURCE, Reader, decode, read_at, run, write_resource


def inspect(args):
    decoder_library = None
    decoder_hash = None
    if args.ooz:
        if not args.decoder_sha256 or hashlib.sha256(Path(args.ooz).read_bytes()).hexdigest().lower() != args.decoder_sha256.lower():
            raise ValueError("Provide the trusted decoder's matching --decoder-sha256")
        decoder_hash = args.decoder_sha256.lower()
    with open(args.container, "rb") as source:
        source.seek(0, 2)
        footer = read_at(source, source.tell() - 221, 221)
        if footer[16] != 0:
            raise ValueError("Encrypted UE index is unsupported")
        magic, version, position, length = struct.unpack_from("<IIqq", footer, 17)
        if magic != 0x5a6f12e1 or version != 11:
            raise ValueError("Only the standard 221-byte Pak v11 footer is supported")
        methods = [footer[i:i + 32].split(b"\0", 1)[0].decode("ascii") for i in range(61, 221, 32)]

        def index(offset, size, digest):
            data = read_at(source, offset, size)
            if hashlib.sha1(data).digest() != digest:
                raise ValueError("Index SHA1 mismatch")
            return data

        reader = Reader(index(position, length, footer[41:61]))
        mount, count = reader.string(), reader.count()
        reader.read(8)
        if reader.value("I") != 1:
            raise ValueError("Path-hash index is required")
        reader.read(36)
        if reader.value("I") != 1:
            raise ValueError("Full directory index is required")
        directory_at, directory_size, directory_hash = reader.value("q"), reader.value("q"), reader.read(20)
        encoded = Reader(reader.read(reader.value("i")))
        if reader.count() != 0:
            raise ValueError("Non-encoded entries are unsupported")
        directory = Reader(index(directory_at, directory_size, directory_hash))
        resources = []
        visited = 0
        for _ in range(directory.count()):
            folder = directory.string()
            for _ in range(directory.count()):
                visited += 1
                if visited > MAX_ENTRIES:
                    raise ValueError("Directory count exceeds limit")
                leaf, location = directory.string(), directory.value("i")
                if location == -2147483648:
                    continue
                if location < 0 or location >= len(encoded.stream.getbuffer()):
                    raise ValueError("Invalid encoded entry offset")
                path = mount + folder + leaf
                if not any(path.casefold().endswith(target.casefold()) for target in args.target):
                    continue
                encoded.stream.seek(location)
                bits = encoded.value("I")
                block_size = encoded.value("I") if bits & 63 == 63 else (bits & 63) << 11
                method_id, blocks = (bits >> 23) & 63, (bits >> 6) & 65535
                offset = encoded.value("I" if bits >> 31 & 1 else "Q")
                size = encoded.value("I" if bits >> 30 & 1 else "Q")
                packed_size = encoded.value("I" if bits >> 29 & 1 else "Q") if method_id else size
                if max(size, packed_size) > MAX_RESOURCE or bits >> 22 & 1:
                    raise ValueError("Requested resource is encrypted or exceeds 16 MiB")
                method = "none" if method_id == 0 else methods[method_id - 1] if method_id <= len(methods) else "unknown"
                header_size = 53 + (4 + blocks * 16 if method_id else 0)
                header = read_at(source, offset, header_size)
                if struct.unpack_from("<QQI", header, 8) != (packed_size, size, method_id):
                    raise ValueError("Entry header does not match index")
                if not method_id:
                    data = decode(read_at(source, offset + header_size, packed_size, MAX_RESOURCE), "none", size)
                else:
                    if not 0 < blocks <= 8192 or (blocks > 1 and not block_size):
                        raise ValueError("Invalid compressed block layout")
                    decoded = []
                    remaining = size
                    previous_end = header_size
                    for block in range(blocks):
                        start, end = struct.unpack_from("<QQ", header, 52 + block * 16)
                        if start < previous_end or end <= start:
                            raise ValueError("Invalid compressed block range")
                        packed = read_at(source, offset + start, end - start, MAX_RESOURCE)
                        expected = size if blocks == 1 else min(block_size, remaining)
                        if expected <= 0:
                            raise ValueError("Too many compression blocks")
                        if method.casefold() == "zlib":
                            # UE Zlib blocks have the zlib wrapper, unlike RE's raw DEFLATE.
                            import zlib
                            dec = zlib.decompressobj()
                            part = dec.decompress(packed, expected + 1)
                            if not dec.eof or dec.unconsumed_tail or dec.unused_data or len(part) != expected:
                                raise ValueError("Invalid Zlib block")
                        elif method.casefold() == "oodle" and args.ooz:
                            if decoder_library is None:
                                decoder_library = ctypes.CDLL(str(Path(args.ooz).resolve()))
                                decoder_library.OozDecompress.argtypes = [ctypes.c_void_p, ctypes.c_size_t, ctypes.c_void_p, ctypes.c_size_t]
                                decoder_library.OozDecompress.restype = ctypes.c_int
                            buffer = ctypes.create_string_buffer(expected + 64)
                            if decoder_library.OozDecompress(packed, len(packed), buffer, expected) != expected:
                                raise ValueError("Oodle decoder failed")
                            part = buffer.raw[:expected]
                        else:
                            raise ValueError("Unsupported compression/dependency: " + method)
                        decoded.append(part)
                        remaining -= len(part)
                        previous_end = end
                    data = b"".join(decoded)
                    if remaining:
                        raise ValueError("Decoded size mismatch")
                if len(resources) >= 256:
                    raise ValueError("More than 256 matching metadata resources; narrow --target")
                resources.append({"resourcePath": path, "offset": offset, "compressedSize": packed_size, "size": size, "compression": method,
                    "sourceLayer": args.layer, **write_resource(args.output, path, data)})
        return {"format": "ue-pak", "version": version, "entryCount": count, "visitedEntries": visited,
            "indexSha1": footer[41:61].hex(), "directorySha1": directory_hash.hex(), "decoderSha256": decoder_hash,
            "resources": resources, "missingRequests": [t for t in args.target if not any(r["resourcePath"].casefold().endswith(t.casefold()) for r in resources)],
            "limitations": "Standard v11 full-directory indexes only. No encrypted index, IoStore, unknown codec or arbitrary format support."}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("container")
    parser.add_argument("--output", required=True)
    parser.add_argument("--target", action="append", required=True)
    parser.add_argument("--layer", default="unspecified")
    parser.add_argument("--ooz", help="Execute a trusted library exporting the documented OozDecompress wrapper")
    parser.add_argument("--decoder-sha256")
    raise SystemExit(run(parser, inspect))
