"""Finite Wwise FNV-1 name candidates. Matches are references, never semantic confirmation."""
import argparse
import hashlib
import json
from pathlib import Path


def wwise_id(name):
    value = 2166136261
    for byte in name.encode("utf-8"):
        if 65 <= byte <= 90:
            byte += 32
        value = ((value * 16777619) & 0xffffffff) ^ byte
    return value


def match(definition, candidate_file):
    source = Path(candidate_file)
    if source.stat().st_size > 8 * 1024 * 1024:
        raise ValueError("Candidate file exceeds 8 MiB")
    groups = json.loads(Path(definition).read_text(encoding="utf-8-sig"))["buses"]
    ids = {int(group["id"]) for group in groups}
    matches = {}
    words = source.read_text(encoding="utf-8-sig").splitlines()
    if len(words) > 100000:
        raise ValueError("Candidate count exceeds 100000")
    for word in words:
        word = word.strip()
        if not word or len(word) > 256:
            continue
        value = wwise_id(word)
        if value in ids and word not in matches.setdefault(str(value), []):
            matches[str(value)].append(word)
    return {"schemaVersion": 1, "method": "Wwise FNV-1 over UTF-8 with ASCII lowercase", "candidateFileSha256": hashlib.sha256(source.read_bytes()).hexdigest(),
        "matches": [{"id": value, "candidates": names, "originalCaseKnown": False, "multipleCandidates": len(names) > 1,
                     "auditionConfirmed": False, "nameSource": "有限词汇哈希候选"} for value, names in matches.items()],
        "limitations": "A hash match is not proof of object type, original spelling or sound semantics. Preserve raw IDs and all competing candidates."}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--definition", required=True)
    parser.add_argument("--candidates", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    result = match(args.definition, args.candidates)
    output = Path(args.output).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8")
    print(json.dumps({"matchedGroups": len(result["matches"]), "semanticConfirmation": False, "output": str(output)}))
