"""Merge normalized metadata reports in explicit lowest-to-highest priority order."""
import argparse
import json
from pathlib import Path


def merge(files):
    chosen, overrides, layers = {}, [], []
    for filename in files:
        path = Path(filename).resolve()
        report = json.loads(path.read_text(encoding="utf-8-sig"))
        if report.get("schemaVersion") != 1 or report.get("outcome") != "completed":
            raise ValueError("Only completed normalized metadata reports can be merged")
        layers.append({"report": str(path), "source": report["source"], "indexDigest": report.get("indexSha256", report.get("indexSha1"))})
        for resource in report["resources"]:
            key = resource["resourcePath"].replace("\\", "/").casefold()
            if key in chosen:
                overrides.append({"resourcePath": resource["resourcePath"], "previous": chosen[key], "selected": resource})
            chosen[key] = {**resource, "source": report["source"]}
    return {"schemaVersion": 1, "priority": "explicit-report-order; last wins", "layers": layers, "resources": list(chosen.values()), "overrides": overrides}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", action="append", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    result = merge(args.report)
    target = Path(args.output).resolve()
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8")
    print(json.dumps({"resources": len(result["resources"]), "overrides": len(result["overrides"]), "output": str(target)}))
