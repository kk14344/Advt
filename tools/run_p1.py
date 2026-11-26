#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Run the complete P1 extraction/decryption/inspection pipeline."""

from __future__ import annotations

import argparse
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

try:
    from . import (
        apk_extract,
        asset_refs,
        csb_inspect,
        data_validate,
        mu_decrypt,
        proto_extract,
    )
except ImportError:  # direct script execution
    import apk_extract
    import asset_refs
    import csb_inspect
    import data_validate
    import mu_decrypt
    import proto_extract


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Run P1: extract, decrypt, inspect CSB, validate CSV, generate proto, resolve assets."
    )
    parser.add_argument("--apk", required=True, type=Path)
    parser.add_argument("--root", type=Path, default=Path("build"))
    parser.add_argument(
        "--clean",
        action="store_true",
        help="replace build/extracted, build/plain and build/csb_json before running",
    )
    args = parser.parse_args(argv)

    root = args.root
    manifest_dir = root / "manifest"
    reports_dir = root / "reports"
    clean_arg = ["--clean"] if args.clean else []

    code = apk_extract.main(
        [
            "--apk",
            str(args.apk),
            "--out",
            str(root / "extracted"),
            "--index",
            str(manifest_dir / "assets.csv"),
            "--summary",
            str(manifest_dir / "extraction_summary.json"),
            *clean_arg,
        ]
    )
    if code != 0:
        print("P1 FAILED at apk_extract", file=sys.stderr)
        return code

    code = mu_decrypt.main(
        [
            "--apk",
            str(args.apk),
            "--out",
            str(root / "plain"),
            "--manifest",
            str(manifest_dir / "mu_files.csv"),
            "--summary",
            str(manifest_dir / "mu_summary.json"),
            *clean_arg,
        ]
    )
    if code != 0:
        print("P1 FAILED at mu_decrypt", file=sys.stderr)
        return code

    code = csb_inspect.main(
        [
            "--extracted",
            str(root / "extracted"),
            "--json-dir",
            str(root / "csb_json"),
            "--index",
            str(manifest_dir / "csb_index.csv"),
            "--nodes",
            str(manifest_dir / "csb_nodes.csv"),
            "--summary",
            str(manifest_dir / "csb_summary.json"),
            *clean_arg,
        ]
    )
    if code != 0:
        print("P1 FAILED at csb_inspect", file=sys.stderr)
        return code

    code = data_validate.main(
        [
            "--data",
            str(root / "plain" / "data"),
            "--reports",
            str(reports_dir),
            "--schema",
            str(manifest_dir / "data_schema.json"),
            "--summary",
            str(manifest_dir / "data_summary.json"),
        ]
    )
    if code != 0:
        print("P1 FAILED at data_validate", file=sys.stderr)
        return code

    code = proto_extract.main(
        [
            "--pb",
            str(root / "plain" / "proto" / "msg.pb"),
            "--out",
            "proto",
            "--index",
            str(manifest_dir / "proto_index.csv"),
            "--summary",
            str(manifest_dir / "proto_summary.json"),
        ]
    )
    if code != 0:
        print("P1 FAILED at proto_extract", file=sys.stderr)
        return code

    code = asset_refs.main(
        [
            "--extracted",
            str(root / "extracted"),
            "--nodes",
            str(manifest_dir / "csb_nodes.csv"),
            "--reports",
            str(reports_dir),
            "--summary",
            str(manifest_dir / "asset_refs_summary.json"),
            "--plists",
            str(manifest_dir / "plist_frames.csv"),
        ]
    )
    if code != 0:
        print("P1 FAILED at asset_refs", file=sys.stderr)
        return code

    extraction = json.loads((manifest_dir / "extraction_summary.json").read_text(encoding="utf-8"))
    decryption = json.loads((manifest_dir / "mu_summary.json").read_text(encoding="utf-8"))
    csb = json.loads((manifest_dir / "csb_summary.json").read_text(encoding="utf-8"))
    data = json.loads((manifest_dir / "data_summary.json").read_text(encoding="utf-8"))
    proto = json.loads((manifest_dir / "proto_summary.json").read_text(encoding="utf-8"))
    assets = json.loads((manifest_dir / "asset_refs_summary.json").read_text(encoding="utf-8"))

    checks = {
        "extraction": extraction["checks"],
        "decryption": decryption["checks"],
        "csb_parse": {"all_files_ok": csb["ok"] == csb["files"] and not csb["errors"]},
        "data_parse": {"no_parse_errors": data["parse_errors"] == 0},
        "proto_generate": {"ok": proto["status"] == "OK"},
        "asset_refs": {"no_unknown_missing": assets["unknown_missing"] == 0},
    }
    ok = all(bool(v) for section in checks.values() for v in section.values())
    known_gaps = {
        "empty_tables": data.get("empty_tables", []),
        "data_warning_tables": [],
        "asset_known_missing_paths": assets.get("known_missing_paths", []),
        "asset_warning_counts": assets.get("warning_status_counts", {}),
    }
    # Keep the warning list directly from the data validation CSV.
    data_csv = reports_dir / "data_validation.csv"
    if data_csv.is_file():
        import csv as _csv

        with data_csv.open(encoding="utf-8", newline="") as handle:
            known_gaps["data_warning_tables"] = [
                row["file"] for row in _csv.DictReader(handle) if row["status"] == "WARN"
            ]

    summary = {
        "tool": "run_p1.py",
        "phase": "P1",
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "apk": extraction["apk"],
        "outputs": {
            "extracted": extraction["out"],
            "plaintext": decryption["out"],
            "csb_json": csb["json_dir"],
            "proto": proto["out"],
            "assets_manifest": extraction["index"],
            "mu_manifest": decryption["manifest"],
            "csb_index": csb["index"],
            "csb_nodes": csb["nodes"],
            "data_schema": (manifest_dir / "data_schema.json").as_posix(),
            "data_summary": (manifest_dir / "data_summary.json").as_posix(),
            "asset_refs": (reports_dir / "asset_refs.csv").as_posix(),
            "missing_assets": (reports_dir / "missing_assets.csv").as_posix(),
        },
        "counts": {
            "extracted_files": extraction["actual_manifest_rows"],
            "lua": decryption["counts"]["lua"],
            "csv": decryption["counts"]["csv"],
            "csb_files": csb["files"],
            "csb_nodes": csb["total_nodes"],
            "data_tables": data["tables"],
            "data_rows": data["data_rows"],
            "proto_files": proto["files"],
            "proto_messages": proto["messages"],
            "asset_refs": assets["references"],
            "asset_missing": assets["missing"],
            "asset_known_missing": assets["known_missing"],
        },
        "checks": checks,
        "known_gaps": known_gaps,
        "ok": ok,
    }
    write_json(manifest_dir / "p1_summary.json", summary)
    print(f"P1 {'OK' if ok else 'FAILED'} (known gaps: {len(known_gaps['data_warning_tables']) + len(known_gaps['asset_known_missing_paths'])} categories)")
    print(f"  summary: {manifest_dir / 'p1_summary.json'}")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
