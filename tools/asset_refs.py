#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Resolve CSB image/font references against extracted plists and files."""

from __future__ import annotations

import argparse
import csv
import json
import plistlib
import sys
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

KNOWN_MISSING_PATHS = {
    "button/fb_share.png": "share-SDK button image is not present in the APK",
    "2.jpg": "referenced placeholder image is not present in any resource package",
    "icon/global/change_arrow.png": "frame is absent from ui.plist and no standalone file exists",
}


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


def iter_resources(value: Any, prefix: str = "") -> Any:
    if isinstance(value, dict):
        if "path" in value and "resource_type" in value:
            yield prefix, value
            return
        for key, child in value.items():
            yield from iter_resources(child, f"{prefix}.{key}" if prefix else key)
    elif isinstance(value, list):
        for index, child in enumerate(value):
            yield from iter_resources(child, f"{prefix}[{index}]")


def resolve_direct(path: str, csb_dir: Path, extracted: Path, basename_index: dict[str, list[Path]]) -> Path | None:
    candidates = [
        csb_dir / path,
        csb_dir.parent / path,
        extracted / path,
    ]
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    matches = basename_index.get(Path(path).name, [])
    if len(matches) == 1:
        return matches[0]
    return None


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Resolve CSB asset references.")
    parser.add_argument("--extracted", type=Path, default=Path("build/extracted"))
    parser.add_argument("--nodes", type=Path, default=Path("build/manifest/csb_nodes.csv"))
    parser.add_argument("--reports", type=Path, default=Path("build/reports"))
    parser.add_argument("--summary", type=Path, default=Path("build/manifest/asset_refs_summary.json"))
    parser.add_argument("--plists", type=Path, default=Path("build/manifest/plist_frames.csv"))
    args = parser.parse_args(argv)

    if not args.extracted.is_dir():
        print(f"extracted directory not found: {args.extracted}", file=sys.stderr)
        return 2
    if not args.nodes.is_file():
        print(f"csb_nodes.csv not found: {args.nodes}", file=sys.stderr)
        return 2

    all_files = [p for p in args.extracted.rglob("*") if p.is_file()]
    basename_index: dict[str, list[Path]] = defaultdict(list)
    for p in all_files:
        basename_index[p.name].append(p)
    general_font = next(
        (p for p in args.extracted.rglob("general.ttf") if p.is_file()), None
    )

    plist_rows: list[dict[str, Any]] = []
    plist_frames: dict[str, dict[str, Any]] = {}
    frame_index: dict[str, list[dict[str, Any]]] = defaultdict(list)
    plist_errors: list[str] = []
    for p in sorted(args.extracted.rglob("*.plist")):
        try:
            with p.open("rb") as handle:
                document = plistlib.load(handle)
            frames = document.get("frames", {})
            metadata = document.get("metadata", {})
            texture = metadata.get("textureFileName") or metadata.get("realTextureFileName") or ""
            texture_path = p.parent / texture if texture else None
            rel = p.relative_to(args.extracted).as_posix()
            info = {
                "rel": rel,
                "frames": set(frames.keys()),
                "texture": texture,
                "dir": p.parent,
                "texture_path": texture_path,
            }
            plist_frames[rel] = info
            for frame_name in frames:
                frame_index[frame_name].append(info)
            plist_rows.append(
                {
                    "plist": rel,
                    "frames": len(frames),
                    "texture": texture,
                    "texture_exists": bool(texture_path and texture_path.is_file()),
                    "width": metadata.get("size", ""),
                    "status": "OK" if frames else "NO_FRAMES",
                }
            )
        except Exception as exc:  # noqa: BLE001
            plist_errors.append(f"{p}: {type(exc).__name__}: {exc}")
            plist_rows.append(
                {
                    "plist": p.relative_to(args.extracted).as_posix(),
                    "frames": 0,
                    "texture": "",
                    "texture_exists": False,
                    "width": "",
                    "status": "ERROR",
                }
            )

    # Resolve a plist name from the CSB's directory first, then by basename.
    def resolve_plist(plist_name: str, csb_dir: Path) -> tuple[dict[str, Any] | None, str | None]:
        direct = csb_dir / plist_name
        if direct.is_file():
            rel = direct.relative_to(args.extracted).as_posix()
            return plist_frames.get(rel), rel
        candidates = [p for p in args.extracted.rglob(plist_name) if p.is_file()]
        if len(candidates) == 1:
            rel = candidates[0].relative_to(args.extracted).as_posix()
            return plist_frames.get(rel), rel
        return None, None

    def resolve_frame(frame_name: str, csb_dir: Path) -> tuple[dict[str, Any] | None, str]:
        options = frame_index.get(frame_name, [])
        if not options:
            return None, "NONE"
        same_dir = [p for p in options if p["dir"].resolve() == csb_dir.resolve()]
        if len(same_dir) == 1:
            return same_dir[0], "SAME_DIR"
        if len(options) == 1:
            return options[0], "UNIQUE"
        # Ambiguous: deterministic first by relative plist path.
        return sorted(options, key=lambda p: p["rel"])[0], "AMBIGUOUS"

    def resolve_frame_basename(path: str) -> tuple[dict[str, Any] | None, str]:
        basename = Path(path).name
        matches: list[tuple[str, dict[str, Any]]] = []
        for frame_name, infos in frame_index.items():
            if Path(frame_name).name == basename:
                for info in infos:
                    matches.append((frame_name, info))
        unique = {(name, info["rel"]): info for name, info in matches}
        if len(unique) == 1:
            name, info = next(iter(unique.items()))
            return info, name[0]
        return None, ""

    def texture_status(info: dict[str, Any]) -> tuple[str, str]:
        texture = info.get("texture_path")
        if texture is not None and texture.is_file():
            return "OK", texture.relative_to(args.extracted).as_posix()
        return "MISSING_TEXTURE", texture.as_posix() if texture else ""

    refs: list[dict[str, Any]] = []
    errors: list[str] = []
    with args.nodes.open(encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            try:
                fields = json.loads(row.get("class_fields_json") or "{}")
            except json.JSONDecodeError:
                fields = {}
            resources = list(iter_resources(fields))
            # Fall back to the flat primary image columns if class_fields_json is empty.
            if not resources and row.get("image_path"):
                resources = [
                    (
                        "image",
                        {
                            "path": row["image_path"],
                            "plist": row.get("image_plist", ""),
                            "resource_type": int(row.get("image_resource_type") or 0),
                        },
                    )
                ]
            if row.get("font_path") and row["font_path"] not in {
                (resource.get("path") or "") for _, resource in resources
            }:
                resources.append(
                    (
                        "font",
                        {
                            "path": row["font_path"],
                            "plist": "",
                            "resource_type": 0,
                        },
                    )
                )
            csb_file = row["file"]
            csb_dir = (args.extracted / csb_file).parent
            for slot, resource in resources:
                path = (resource.get("path") or "").strip()
                plist_name = (resource.get("plist") or "").strip()
                if not path:
                    continue
                kind = "font" if "font" in slot or path.lower().endswith((".ttf", ".fnt")) else "image"
                resolved = ""
                expected_texture = ""
                if kind == "image" and path.startswith("Default/"):
                    status = "DEFAULT"
                else:
                    chosen: dict[str, Any] | None = None
                    mode = ""
                    status = ""
                    plist, plist_rel = (
                        resolve_plist(plist_name, csb_dir) if plist_name else (None, None)
                    )
                    if plist is not None and path in plist["frames"]:
                        chosen = plist
                        status = "OK"
                    else:
                        chosen, mode = resolve_frame(path, csb_dir)
                        if chosen is not None:
                            if mode == "AMBIGUOUS":
                                status = "AMBIGUOUS_FRAME"
                            elif plist is not None and chosen["rel"] != plist_rel:
                                status = "OK_ALTERNATE_PLIST"
                            elif plist is None:
                                status = "OK_GLOBAL_FRAME"
                            else:
                                status = "OK"
                        else:
                            frame_info, frame_name = resolve_frame_basename(path)
                            if frame_info is not None:
                                chosen = frame_info
                                mode = f"BASENAME:{frame_name}"
                                status = "CANDIDATE_BASENAME_FRAME"
                    if chosen is not None:
                        texture_status_value, texture_path = texture_status(chosen)
                        expected_texture = (
                            chosen["texture_path"].as_posix()
                            if chosen.get("texture_path")
                            else ""
                        )
                        if texture_status_value == "OK":
                            resolved = texture_path
                        else:
                            status = "MISSING_TEXTURE"
                            resolved = ""
                    else:
                        target = resolve_direct(path, csb_dir, args.extracted, basename_index)
                        if target is not None:
                            status = "OK"
                            resolved = target.relative_to(args.extracted).as_posix()
                        elif kind == "font":
                            if general_font is not None:
                                status = "FALLBACK_FONT"
                                resolved = general_font.relative_to(args.extracted).as_posix()
                            else:
                                status = "MISSING_FILE"
                        elif plist_name:
                            status = "MISSING_FRAME" if plist is not None else "MISSING_PLIST"
                        else:
                            status = "MISSING_FILE"
                refs.append(
                    {
                        "file": csb_file,
                        "node_id": row["node_id"],
                        "node_name": row.get("name", ""),
                        "node_class": row.get("class", ""),
                        "slot": slot,
                        "kind": kind,
                        "path": path,
                        "plist": plist_name,
                        "expected_texture": expected_texture,
                        "resolved": resolved,
                        "status": status,
                        "known_gap": KNOWN_MISSING_PATHS.get(path, "") if status.startswith("MISSING") else "",
                    }
                )
                if status.startswith("MISSING"):
                    errors.append(f"{csb_file}#{row['node_id']} {slot}: {status} {path}")

    missing = [r for r in refs if r["status"].startswith("MISSING")]
    known_missing = [r for r in missing if r.get("known_gap")]
    unknown_missing = [r for r in missing if not r.get("known_gap")]
    warning_statuses = {
        "AMBIGUOUS_FRAME",
        "CANDIDATE_BASENAME_FRAME",
        "FALLBACK_FONT",
        "OK_ALTERNATE_PLIST",
        "OK_GLOBAL_FRAME",
    }
    warnings = [r for r in refs if r["status"] in warning_statuses]
    counts = Counter(r["status"] for r in refs)
    write_csv(
        args.reports / "asset_refs.csv",
        refs,
        [
            "file",
            "node_id",
            "node_name",
            "node_class",
            "slot",
            "kind",
            "path",
            "plist",
            "expected_texture",
            "resolved",
            "status",
            "known_gap",
        ],
    )
    write_csv(
        args.reports / "missing_assets.csv",
        missing,
        [
            "file",
            "node_id",
            "node_name",
            "node_class",
            "slot",
            "kind",
            "path",
            "plist",
            "expected_texture",
            "resolved",
            "status",
            "known_gap",
        ],
    )
    write_csv(
        args.plists,
        plist_rows,
        ["plist", "frames", "texture", "texture_exists", "width", "status"],
    )
    summary = {
        "tool": "asset_refs.py",
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "extracted": args.extracted.as_posix(),
        "nodes": args.nodes.as_posix(),
        "plists": len(plist_rows),
        "plist_errors": plist_errors,
        "references": len(refs),
        "status_counts": dict(counts),
        "missing": len(missing),
        "known_missing": len(known_missing),
        "unknown_missing": len(unknown_missing),
        "known_missing_paths": sorted({r["path"] for r in known_missing}),
        "warnings": len(warnings),
        "warning_status_counts": dict(
            Counter(r["status"] for r in warnings)
        ),
        "errors": errors[:200],
        "reports": {
            "refs": (args.reports / "asset_refs.csv").as_posix(),
            "missing": (args.reports / "missing_assets.csv").as_posix(),
            "plists": args.plists.as_posix(),
        },
    }
    write_json(args.summary, summary)
    print("ASSET_REFS P1b")
    print(f"  plists: {len(plist_rows)}")
    print(f"  references: {len(refs)}")
    print(f"  status: {dict(counts)}")
    print(f"  missing: {len(missing)} known={len(known_missing)} unknown={len(unknown_missing)}")
    print(f"  summary: {args.summary}")
    if unknown_missing:
        for item in unknown_missing[:10]:
            print(f"  UNKNOWN_MISSING {item['file']}#{item['node_id']} {item['path']} ({item['status']})")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
