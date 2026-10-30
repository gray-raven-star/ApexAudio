"""Append a verified package to an existing release; never replace old assets."""
from pathlib import Path
import hashlib
import json
import os
import urllib.parse
import urllib.request
import zipfile

repo = os.environ["GITHUB_REPOSITORY"]
root = Path("release-inputs/dragonsdogma2")
meta = json.loads((root / "release.json").read_text(encoding="utf-8"))
assert repo == "gray-raven-star/ApexAudio"
assert meta["releaseTag"] == "bundle-2026.10.18"
package = root / meta["file"]
assert package.parent == root and package.suffix == ".gpack"
raw = package.read_bytes()
assert hashlib.sha256(raw).hexdigest() == meta["sha256"]
with zipfile.ZipFile(package) as z:
    assert z.testzip() is None
    manifest = json.loads(z.read("manifest.json"))
    assert manifest["id"] == "local.dragonsdogma2" and manifest["version"] == "0.1.1"
    for name, digest in manifest["files"].items():
        assert hashlib.sha256(z.read(name)).hexdigest().upper() == digest.upper()
    preset = json.loads(z.read(meta["presetPath"]))
    assert preset["id"] == "action-environment"
    assert sum(bool(v.get("enabled")) for v in preset["groups"].values()) == meta["enabledGroups"]
checksum = root / meta["checksumFile"]
assert checksum.parent == root
assert checksum.read_text(encoding="utf-8").strip() == meta["sha256"] + "  " + meta["file"]
api = "https://api.github.com/repos/" + repo
headers = {"Authorization": "Bearer " + os.environ["GH_TOKEN"], "Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28", "User-Agent": "ApexAudio-package-release"}

def request(url, method="GET", data=None, mime="application/json"):
    assert urllib.parse.urlparse(url).hostname in ("api.github.com", "uploads.github.com")
    h = dict(headers)
    if data is not None:
        h["Content-Type"] = mime
    req = urllib.request.Request(url, headers=h, method=method, data=data)
    with urllib.request.urlopen(req, timeout=60) as response:
        return json.load(response)

release = request(api + "/releases/tags/" + meta["releaseTag"])
assert not release["draft"]
assets = {a["name"]: a for a in release["assets"]}
for file in (package, checksum):
    data = file.read_bytes()
    digest = "sha256:" + hashlib.sha256(data).hexdigest()
    if file.name in assets:
        if assets[file.name].get("digest") != digest:
            raise RuntimeError("An asset with a different hash already exists: " + file.name)
        print("Already uploaded:", file.name)
        continue
    url = "https://uploads.github.com/repos/" + repo + "/releases/" + str(release["id"]) + "/assets?name=" + urllib.parse.quote(file.name)
    result = request(url, "POST", data, "application/octet-stream")
    assert result["state"] == "uploaded" and result["size"] == len(data)
    assert result.get("digest") == digest
    print("Uploaded:", result["browser_download_url"])
body = release.get("body") or ""
if meta["notesLine"] not in body:
    request(api + "/releases/" + str(release["id"]), "PATCH", json.dumps({"body": body.rstrip() + "\n" + meta["notesLine"] + "\n"}, ensure_ascii=False).encode("utf-8"))
print("Published to", release["html_url"])
