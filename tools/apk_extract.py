#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Extract the game asset packages from the APK into a local working tree.

This is a P1 data-pipeline tool, not gameplay code.  It writes extracted
resources to a disposable build directory; do not commit them.

Usage:
    python tools/apk_extract.py --apk "F:\\base(2).apk.1" --out build/extracted
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
import shutil
import stat
import sys
import time
import zipfile
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath
from typing import Any

EXPECTED_APK_SHA256 = (
    "256c7e24c72ef9ff6ffd0a3b6f1ba864bbcb93db859a3fbcc293cb9496f8bd3d"
)

# Package name -> (expected file count, usage)
PACKAGES: dict[str, tuple[int, str]] = {
    "ui": (676, "ui"),
    "role": (615, "character_art"),
    "spine": (182, "spine_animation"),
    "sound": (124, "audio"),
    "animation": (139, "animation"),
    "effect": (25, "effect_image"),
    "particle": (8, "particle_config"),
    "fonts": (9, "font"),
    "shader": (8, "shader"),
    "adventure": (35, "adventure_background"),
    "battle_background": (18, "battle_background"),
    "language": (0, "locale_placeholder"),
}

META_ASSETS = [
    "assets/assets.manifest",
    "assets/version.manifest",
    "assets/channel.txt",
    "assets/quicksdk.xml",
    "assets/pingpp_web.html",
    "assets/com.qk.plugin.qkfx.Manager",
    "assets/data.bin",
    "assets/src.mu",
    "assets/data.mu",
]


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def write_csv(path: Path, rows: list[dict[str, Any]], fields: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields, lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


def check_member_path(name: str) -> PurePosixPath:
    """Reject absolute paths, traversal, drive letters and symlinks."""

    if not name or name.endswith("/"):
        raise ValueError(f"invalid empty/directory member: {name!r}")
    if "\\" in name:
        raise ValueError(f"backslash in ZIP member name: {name!r}")
    pure = PurePosixPath(name)
    if pure.is_absolute() or any(part in ("", ".", "..") for part in pure.parts):
        raise ValueError(f"unsafe ZIP member path: {name!r}")
    if any(":" in part for part in pure.parts):
        raise ValueError(f"drive/colon in ZIP member path: {name!r}")
    return pure


def safe_target(root: Path, rel: PurePosixPath) -> Path:
    target = (root / Path(*rel.parts)).resolve()
    if not target.is_relative_to(root.resolve()):
        raise ValueError(f"target escapes output root: {rel}")
    return target


def display_path(path: Path) -> str:
    try:
        return path.resolve().relative_to(Path.cwd().resolve()).as_posix()
    except ValueError:
        return path.resolve().as_posix()


def extract_zip(
    apk: zipfile.ZipFile,
    archive_name: str,
    out_root: Path,
    rows: list[dict[str, Any]],
    errors: list[str],
) -> dict[str, Any]:
    package = Path(archive_name).stem
    expected_count, usage = PACKAGES[package]
    package_root = out_root / package
    package_root.mkdir(parents=True, exist_ok=True)
    raw = apk.read(archive_name)
    with zipfile.ZipFile(io.BytesIO(raw)) as nested:
        infos = [i for i in nested.infolist() if not i.is_dir()]
        extracted = 0
        total_bytes = 0
        for info in infos:
            try:
                mode = info.external_attr >> 16
                if stat.S_ISLNK(mode):
                    raise ValueError("symlink member is not allowed")
                rel = check_member_path(info.filename)
                target = safe_target(package_root, rel)
                data = nested.read(info)
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(data)
                digest = sha256_bytes(data)
                rows.append(
                    {
                        "apk_path": archive_name,
                        "package": package,
                        "rel_path": info.filename,
                        "ext": Path(info.filename).suffix.lower(),
                        "size": len(data),
                        "sha256": digest,
                        "usage": usage,
                        "extracted_path": f"{package}/{rel.as_posix()}",
                        "status": "OK",
                    }
                )
                extracted += 1
                total_bytes += len(data)
            except Exception as exc:  # noqa: BLE001 - collect all extraction errors
                errors.append(f"{archive_name}!{info.filename}: {type(exc).__name__}: {exc}")
        count_ok = extracted == expected_count
        return {
            "archive": archive_name,
            "package": package,
            "expected_files": expected_count,
            "extracted_files": extracted,
            "total_bytes": total_bytes,
            "count_ok": count_ok,
            "zip_sha256": sha256_bytes(raw),
            "out_dir": display_path(package_root),
        }


def extract_meta(
    apk: zipfile.ZipFile,
    out_root: Path,
    rows: list[dict[str, Any]],
    errors: list[str],
) -> dict[str, Any]:
    meta_root = out_root / "meta"
    meta_root.mkdir(parents=True, exist_ok=True)
    extracted = 0
    total_bytes = 0
    for apk_path in META_ASSETS:
        try:
            if apk_path not in apk.namelist():
                raise KeyError(apk_path)
            data = apk.read(apk_path)
            target = meta_root / Path(apk_path).name
            target.write_bytes(data)
            rows.append(
                {
                    "apk_path": apk_path,
                    "package": "meta",
                    "rel_path": Path(apk_path).name,
                    "ext": Path(apk_path).suffix.lower(),
                    "size": len(data),
                    "sha256": sha256_bytes(data),
                    "usage": "config_or_raw_package",
                    "extracted_path": f"meta/{Path(apk_path).name}",
                    "status": "OK",
                }
            )
            extracted += 1
            total_bytes += len(data)
        except Exception as exc:  # noqa: BLE001
            errors.append(f"{apk_path}: {type(exc).__name__}: {exc}")
    return {
        "package": "meta",
        "expected_files": len(META_ASSETS),
        "extracted_files": extracted,
        "total_bytes": total_bytes,
        "count_ok": extracted == len(META_ASSETS),
        "out_dir": display_path(meta_root),
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Extract game assets from the APK.")
    parser.add_argument("--apk", required=True, type=Path)
    parser.add_argument("--out", type=Path, default=Path("build/extracted"))
    parser.add_argument(
        "--index", type=Path, default=Path("build/manifest/assets.csv")
    )
    parser.add_argument(
        "--summary", type=Path, default=Path("build/manifest/extraction_summary.json")
    )
    parser.add_argument(
        "--clean",
        action="store_true",
        help="remove the output directory before extracting (must be under ./build)",
    )
    args = parser.parse_args(argv)

    if not args.apk.is_file():
        print(f"APK not found: {args.apk}", file=sys.stderr)
        return 2
    if args.clean and args.out.exists():
        out_resolved = args.out.resolve()
        build_root = (Path.cwd() / "build").resolve()
        if not out_resolved.is_relative_to(build_root) or out_resolved == build_root:
            print(f"refusing --clean outside build/: {out_resolved}", file=sys.stderr)
            return 2
        shutil.rmtree(out_resolved)

    started = time.time()
    args.out.mkdir(parents=True, exist_ok=True)
    apk_sha = sha256_file(args.apk)
    errors: list[str] = []
    rows: list[dict[str, Any]] = []
    package_summaries: list[dict[str, Any]] = []
    with zipfile.ZipFile(args.apk) as apk:
        archive_names = sorted(
            n for n in apk.namelist() if n.startswith("assets/") and n.endswith(".zip")
        )
        missing_packages = set(PACKAGES) - {Path(n).stem for n in archive_names}
        for package in sorted(missing_packages):
            errors.append(f"missing package: {package}")
        for archive_name in archive_names:
            package = Path(archive_name).stem
            if package not in PACKAGES:
                continue
            summary = extract_zip(apk, archive_name, args.out, rows, errors)
            package_summaries.append(summary)
        package_summaries.append(extract_meta(apk, args.out, rows, errors))
        apk_entries = len(apk.infolist())

    rows.sort(key=lambda r: (r["package"], r["rel_path"]))
    fields = [
        "apk_path",
        "package",
        "rel_path",
        "ext",
        "size",
        "sha256",
        "usage",
        "extracted_path",
        "status",
    ]
    write_csv(args.index, rows, fields)

    expected_rows = sum(v[0] for v in PACKAGES.values()) + len(META_ASSETS)
    counts = Counter(r["package"] for r in rows)
    summary = {
        "tool": "apk_extract.py",
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "apk": {
            "path": str(args.apk),
            "size": args.apk.stat().st_size,
            "sha256": apk_sha,
            "zip_entries": apk_entries,
        },
        "out": display_path(args.out),
        "index": display_path(args.index),
        "expected_manifest_rows": expected_rows,
        "actual_manifest_rows": len(rows),
        "packages": package_summaries,
        "counts": dict(sorted(counts.items())),
        "errors": errors,
        "checks": {
            "apk_sha256_matches_probe": apk_sha == EXPECTED_APK_SHA256,
            "manifest_rows_match": len(rows) == expected_rows,
            "no_errors": not errors,
            "all_package_counts_match": all(
                p.get("count_ok", True) for p in package_summaries
            ),
        },
        "elapsed_seconds": round(time.time() - started, 3),
    }
    write_json(args.summary, summary)

    print("APK_EXTRACT P1a")
    print(f"  apk_sha256: {apk_sha}")
    print(f"  package_summaries: {len(package_summaries)}")
    print(f"  files: {len(rows)} / expected {expected_rows}")
    print(f"  bytes: {sum(r['size'] for r in rows):,}")
    print(f"  index: {args.index}")
    print(f"  summary: {args.summary}")
    if errors:
        for error in errors[:10]:
            print(f"  ERROR {error}")
        return 1
    if len(rows) != expected_rows:
        print("  ERROR manifest row count mismatch", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
