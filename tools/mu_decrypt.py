#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Materialize decrypted Lua, CSV and the plaintext protobuf descriptor.

Outputs are local research artifacts under build/plain and must not be
committed.  The tool writes only the known game sources; it does not execute
any Lua.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
import shutil
import sys
import time
import zipfile
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath
from typing import Any

try:
    from .mu_codec import decrypt_mu_payload
except ImportError:  # direct script execution
    from mu_codec import decrypt_mu_payload

EXPECTED_APK_SHA256 = (
    "256c7e24c72ef9ff6ffd0a3b6f1ba864bbcb93db859a3fbcc293cb9496f8bd3d"
)
EXPECTED_LUA = 483
EXPECTED_CSV = 114
EXPECTED_ROWS = 28595


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


def member_rel(name: str) -> PurePosixPath:
    if not name or "\\" in name:
        raise ValueError(f"invalid member name: {name!r}")
    rel = PurePosixPath(name)
    if rel.is_absolute() or any(part in ("", ".", "..") for part in rel.parts):
        raise ValueError(f"unsafe member name: {name!r}")
    return rel


def safe_target(root: Path, rel: PurePosixPath) -> Path:
    target = (root / Path(*rel.parts)).resolve()
    if not target.is_relative_to(root.resolve()):
        raise ValueError(f"target escapes root: {rel}")
    return target


def display_path(path: Path) -> str:
    try:
        return path.resolve().relative_to(Path.cwd().resolve()).as_posix()
    except ValueError:
        return path.resolve().as_posix()


def clean_output(path: Path) -> None:
    resolved = path.resolve()
    build_root = (Path.cwd() / "build").resolve()
    if not resolved.is_relative_to(build_root) or resolved == build_root:
        raise ValueError(f"refusing to clean outside build/: {resolved}")
    if resolved.exists():
        shutil.rmtree(resolved)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Decrypt .mu payloads into build/plain.")
    parser.add_argument("--apk", required=True, type=Path)
    parser.add_argument("--out", type=Path, default=Path("build/plain"))
    parser.add_argument(
        "--manifest", type=Path, default=Path("build/manifest/mu_files.csv")
    )
    parser.add_argument(
        "--summary", type=Path, default=Path("build/manifest/mu_summary.json")
    )
    parser.add_argument(
        "--clean",
        action="store_true",
        help="remove the output directory before writing (must be under ./build)",
    )
    args = parser.parse_args(argv)

    if not args.apk.is_file():
        print(f"APK not found: {args.apk}", file=sys.stderr)
        return 2
    if args.clean:
        try:
            clean_output(args.out)
        except ValueError as exc:
            print(str(exc), file=sys.stderr)
            return 2

    started = time.time()
    args.out.mkdir(parents=True, exist_ok=True)
    src_root = args.out / "src"
    data_root = args.out / "data"
    proto_root = args.out / "proto"
    src_root.mkdir(parents=True, exist_ok=True)
    data_root.mkdir(parents=True, exist_ok=True)
    proto_root.mkdir(parents=True, exist_ok=True)

    rows: list[dict[str, Any]] = []
    errors: list[str] = []
    lua_count = 0
    lua_bytes = 0
    csv_count = 0
    csv_bytes = 0
    csv_rows = 0
    pb_count = 0

    with zipfile.ZipFile(args.apk) as apk:
        apk_sha = sha256_file(args.apk)
        for container, root, signed in (
            ("assets/src.mu", src_root, True),
            ("assets/data.mu", data_root, False),
        ):
            raw = apk.read(container)
            with zipfile.ZipFile(io.BytesIO(raw)) as nested:
                entries = [i for i in nested.infolist() if not i.is_dir()]
                for info in entries:
                    try:
                        rel = member_rel(info.filename)
                        encrypted = nested.read(info)
                        if container == "assets/src.mu" and rel.suffix.lower() == ".pb":
                            # msg.pb is deliberately plaintext inside src.mu.
                            payload = encrypted
                            meta = {"cipher_len": len(payload), "plain_len": len(payload)}
                            target = safe_target(proto_root, PurePosixPath(rel.name))
                            target.write_bytes(payload)
                            pb_count += 1
                            kind = "proto_descriptor"
                            out_path = target
                            row_count = ""
                        elif container == "assets/src.mu" and rel.suffix.lower() == ".lua":
                            payload, meta = decrypt_mu_payload(encrypted, signed=signed)
                            target_rel = PurePosixPath(*rel.parts[1:])
                            target = safe_target(src_root, target_rel)
                            text = payload.decode("utf-8")
                            if "\x00" in text:
                                raise ValueError("NUL in Lua plaintext")
                            target.parent.mkdir(parents=True, exist_ok=True)
                            target.write_bytes(payload)
                            lua_count += 1
                            lua_bytes += len(payload)
                            kind = "lua"
                            out_path = target
                            row_count = ""
                        elif container == "assets/data.mu" and rel.suffix.lower() == ".csv":
                            payload, meta = decrypt_mu_payload(encrypted, signed=signed)
                            target = safe_target(data_root, PurePosixPath(rel.name))
                            text = payload.decode("utf-8-sig")
                            parsed = list(csv.reader(io.StringIO(text)))
                            if not parsed or not parsed[0]:
                                raise ValueError("empty CSV or header")
                            target.write_bytes(payload)
                            csv_count += 1
                            csv_bytes += len(payload)
                            csv_rows += len(parsed)
                            kind = "csv"
                            out_path = target
                            row_count = len(parsed)
                        else:
                            raise ValueError(f"unexpected member type: {info.filename}")
                        rows.append(
                            {
                                "container": container,
                                "apk_path": info.filename,
                                "kind": kind,
                                "relative_out": out_path.resolve()
                                .relative_to(args.out.resolve())
                                .as_posix(),
                                "cipher_size": meta["cipher_len"],
                                "plain_size": meta["plain_len"],
                                "sha256": sha256_bytes(payload),
                                "rows_including_header": row_count,
                                "status": "OK",
                            }
                        )
                    except Exception as exc:  # noqa: BLE001 - collect all errors
                        errors.append(
                            f"{container}!{info.filename}: {type(exc).__name__}: {exc}"
                        )

    rows.sort(key=lambda r: (r["kind"], r["apk_path"]))
    write_csv(
        args.manifest,
        rows,
        [
            "container",
            "apk_path",
            "kind",
            "relative_out",
            "cipher_size",
            "plain_size",
            "sha256",
            "rows_including_header",
            "status",
        ],
    )

    checks = {
        "apk_sha256_matches_probe": apk_sha == EXPECTED_APK_SHA256,
        "lua_count": lua_count == EXPECTED_LUA,
        "csv_count": csv_count == EXPECTED_CSV,
        "csv_rows": csv_rows == EXPECTED_ROWS,
        "proto_descriptor_count": pb_count == 1,
        "no_errors": not errors,
    }
    summary = {
        "tool": "mu_decrypt.py",
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "apk": {"path": str(args.apk), "size": args.apk.stat().st_size, "sha256": apk_sha},
        "out": display_path(args.out),
        "manifest": display_path(args.manifest),
        "counts": {
            "lua": lua_count,
            "csv": csv_count,
            "proto_descriptor": pb_count,
            "csv_rows_including_header": csv_rows,
        },
        "bytes": {"lua": lua_bytes, "csv": csv_bytes},
        "checks": checks,
        "errors": errors,
        "elapsed_seconds": round(time.time() - started, 3),
    }
    write_json(args.summary, summary)

    print("MU_DECRYPT P1a")
    print(f"  apk_sha256: {apk_sha}")
    print(f"  lua: {lua_count} ({lua_bytes:,} B)")
    print(f"  csv: {csv_count} ({csv_rows:,} rows, {csv_bytes:,} B)")
    print(f"  proto descriptor: {pb_count}")
    print(f"  manifest: {args.manifest}")
    print(f"  summary: {args.summary}")
    if errors:
        for error in errors[:10]:
            print(f"  ERROR {error}")
        return 1
    if not all(checks.values()):
        print("  ERROR checks failed", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
