"""Version, file names, and provenance shared by the Windows build and packager."""
import hashlib
import json
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION = (ROOT / "release" / "version.txt").read_text(encoding="utf-8").strip()
if not re.fullmatch(r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*))?", VERSION):
    raise ValueError("release/version.txt must contain a two- or three-part version number")
PARTS = VERSION.split(".")
if any(int(part) > 65534 for part in PARTS):
    raise ValueError("Version components must fit the Windows assembly version")
FILE_VERSION = ".".join(PARTS + ["0"] * (4 - len(PARTS)))
NAME = "冒险与挖矿助手" + VERSION


def git(*args):
    return subprocess.run(["git", *args], cwd=ROOT, check=True, stdout=subprocess.PIPE).stdout


def provenance():
    if (ROOT / ".git").exists():
        commit = git("rev-parse", "HEAD").decode("ascii").strip()
        dirty = bool(git("status", "--porcelain", "--untracked-files=normal"))
    else:
        manifest = ROOT / "source-provenance.json"
        commit = json.loads(manifest.read_text(encoding="utf-8"))["commit"] if manifest.exists() else "local"
        dirty = True
    build = f"{VERSION}+{commit[:12]}" + (".local" if dirty else "")
    return {"version": VERSION, "fileVersion": FILE_VERSION, "build": build, "commit": commit, "dirty": dirty}


def sha256(path):
    with path.open("rb") as stream:
        digest = hashlib.sha256()
        for block in iter(lambda: stream.read(1048576), b""):
            digest.update(block)
        return digest.hexdigest()

