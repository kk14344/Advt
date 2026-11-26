#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Inspect Cocos Studio 2.1 .csb files and emit node trees.

The parser is intentionally read-only and schema-specific.  It reads the
CSParseBinary layout used by Cocos2d-x/cocostudio 2.x and does not execute any
game code.

Usage:
    python tools/csb_inspect.py --extracted build/extracted
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import shutil
import sys
import time
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

try:
    from .flatbuf import FlatBufferError, Table, root_table
except ImportError:  # direct script execution
    from flatbuf import FlatBufferError, Table, root_table


# ---------------------------------------------------------------------------
# Widget fields (slot index; 0-based field order from CSParseBinary.fbs)
# ---------------------------------------------------------------------------

WIDGET_NAME = 0
WIDGET_ACTION_TAG = 1
WIDGET_ROTATION = 2
WIDGET_Z = 3
WIDGET_VISIBLE = 4
WIDGET_ALPHA = 5
WIDGET_TAG = 6
WIDGET_POSITION = 7
WIDGET_SCALE = 8
WIDGET_ANCHOR = 9
WIDGET_COLOR = 10
WIDGET_SIZE = 11
WIDGET_FLIP_X = 12
WIDGET_FLIP_Y = 13
WIDGET_IGNORE_SIZE = 14
WIDGET_TOUCH = 15
WIDGET_FRAME_EVENT = 16
WIDGET_CUSTOM_PROPERTY = 17
WIDGET_CALLBACK_TYPE = 18
WIDGET_CALLBACK_NAME = 19

CLASS_FIELDS: dict[str, dict[str, int]] = {
    "Text": {
        "font_resource": 1,
        "font_name": 2,
        "font_size": 3,
        "text": 4,
        "area_w": 5,
        "area_h": 6,
        "h_align": 7,
        "v_align": 8,
        "touch_scale": 9,
        "is_custom_size": 10,
        "outline_enabled": 11,
        "outline_color": 12,
        "outline_size": 13,
        "shadow_enabled": 14,
        "shadow_color": 15,
        "shadow_dx": 16,
        "shadow_dy": 17,
        "shadow_blur": 18,
        "is_localized": 19,
    },
    "TextBMFont": {
        "font_resource": 1,
        "text": 2,
        "is_localized": 3,
    },
    "TextAtlas": {
        "font_resource": 1,
        "text": 2,
        "start_char": 3,
        "item_width": 4,
        "item_height": 5,
    },
    "Button": {
        "normal": 1,
        "pressed": 2,
        "disabled": 3,
        "font_resource": 4,
        "text": 5,
        "font_name": 6,
        "font_size": 7,
        "text_color": 8,
        "cap_insets": 9,
        "scale9_size": 10,
        "scale9_enabled": 11,
        "display_state": 12,
        "outline_enabled": 13,
        "outline_color": 14,
        "outline_size": 15,
        "shadow_enabled": 16,
        "shadow_color": 17,
        "shadow_dx": 18,
        "shadow_dy": 19,
        "shadow_blur": 20,
        "is_localized": 21,
    },
    "TextField": {
        "font_resource": 1,
        "font_name": 2,
        "font_size": 3,
        "text": 4,
        "placeholder": 5,
        "password": 6,
        "password_style": 7,
        "max_length_enabled": 8,
        "max_length": 9,
        "area_w": 10,
        "area_h": 11,
        "is_custom_size": 12,
        "is_localized": 13,
    },
    "ImageView": {
        "image": 1,
        "cap_insets": 2,
        "scale9_size": 3,
        "scale9_enabled": 4,
    },
    "Sprite": {
        "image": 1,
        "blend": 2,
    },
    "Particle": {
        "image": 1,
        "blend": 2,
    },
    "GameMap": {
        "image": 1,
    },
    "CheckBox": {
        "image": 1,
        "selected_image": 2,
        "front_cross": 3,
        "disabled_image": 4,
        "front_cross_disabled": 5,
        "selected": 6,
        "display_state": 7,
    },
    "LoadingBar": {
        "image": 1,
        "percent": 2,
        "direction": 3,
    },
    "Slider": {
        "bar_image": 1,
        "ball_normal": 2,
        "ball_pressed": 3,
        "ball_disabled": 4,
        "progress_image": 5,
        "percent": 6,
        "display_state": 7,
    },
    "Panel": {
        "image": 1,
        "clip": 2,
        "bg_color": 3,
        "bg_start": 4,
        "bg_end": 5,
        "color_type": 6,
        "bg_opacity": 7,
        "color_vector": 8,
        "cap_insets": 9,
        "scale9_size": 10,
        "scale9_enabled": 11,
    },
    "ScrollView": {
        "image": 1,
        "clip": 2,
        "bg_color": 3,
        "bg_start": 4,
        "bg_end": 5,
        "color_type": 6,
        "bg_opacity": 7,
        "color_vector": 8,
        "cap_insets": 9,
        "scale9_size": 10,
        "scale9_enabled": 11,
        "inner_size": 12,
        "direction": 13,
        "bounce": 14,
        "scrollbar": 15,
        "scrollbar_auto_hide": 16,
        "scrollbar_auto_hide_time": 17,
    },
    "ListView": {
        "image": 1,
        "clip": 2,
        "bg_color": 3,
        "bg_start": 4,
        "bg_end": 5,
        "color_type": 6,
        "bg_opacity": 7,
        "color_vector": 8,
        "cap_insets": 9,
        "scale9_size": 10,
        "scale9_enabled": 11,
        "inner_size": 12,
        "direction": 13,
        "bounce": 14,
        "item_margin": 15,
        "direction_type": 16,
        "horizontal_type": 17,
        "vertical_type": 18,
    },
    "PageView": {
        "image": 1,
        "clip": 2,
        "bg_color": 3,
        "bg_start": 4,
        "bg_end": 5,
        "color_type": 6,
        "bg_opacity": 7,
        "color_vector": 8,
        "cap_insets": 9,
        "scale9_size": 10,
        "scale9_enabled": 11,
    },
    "ProjectNode": {
        "file_name": 1,
        "inner_action_speed": 2,
    },
    "ComAudio": {
        "name": 1,
        "enabled": 2,
        "loop": 3,
        "volume": 4,
        "file": 5,
    },
}

RESOURCE_FIELDS = {"image", "normal", "pressed", "disabled", "font_resource", "selected_image",
                   "front_cross", "disabled_image", "front_cross_disabled", "bar_image",
                   "ball_normal", "ball_pressed", "ball_disabled", "progress_image", "file"}
PAIR_FIELDS = {"scale9_size", "inner_size"}
COLOR_FIELDS = {"bg_color", "bg_start", "bg_end", "outline_color", "shadow_color", "text_color"}
CAP_FIELDS = {"cap_insets"}
FLOAT_FIELDS = {"shadow_dx", "shadow_dy", "inner_action_speed"}
BOOL_FIELDS = {
    "is_localized",
    "scale9_enabled",
    "is_custom_size",
    "outline_enabled",
    "shadow_enabled",
    "clip",
    "bounce",
    "scrollbar",
    "scrollbar_auto_hide",
    "max_length_enabled",
    "password",
    "selected",
    "display_state",
    "touch_scale",
    "enabled",
    "loop",
}
U8_FIELDS = {"bg_opacity"}
INT_FIELDS = {
    "font_size",
    "area_w",
    "area_h",
    "h_align",
    "v_align",
    "outline_size",
    "shadow_blur",
    "start_char",
    "item_width",
    "item_height",
    "percent",
    "direction",
    "color_type",
    "item_margin",
    "max_length",
    "volume",
}


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


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


def read_resource(table: Table | None, slot: int) -> dict[str, Any] | None:
    if table is None:
        return None
    resource = table.table(slot)
    if resource is None:
        return None
    return {
        "path": resource.string(0) or "",
        "plist": resource.string(1) or "",
        "resource_type": resource.i32(2, 0),
    }


def looks_like_widget(table: Table | None) -> bool:
    if table is None:
        return False
    try:
        name = table.string(WIDGET_NAME)
        size = table.pair_f32(WIDGET_SIZE)
        position = table.pair_f32(WIDGET_POSITION)
        if name is None or size is None or position is None:
            return False
        if not (0 <= size[0] <= 100000 and 0 <= size[1] <= 100000):
            return False
        if not (-1_000_000 <= position[0] <= 1_000_000 and -1_000_000 <= position[1] <= 1_000_000):
            return False
        return True
    except Exception:
        return False


def parse_widget(widget: Table) -> dict[str, Any]:
    rotation = widget.pair_f32(WIDGET_ROTATION) or (0.0, 0.0)
    position = widget.pair_f32(WIDGET_POSITION) or (0.0, 0.0)
    scale = widget.pair_f32(WIDGET_SCALE) or (1.0, 1.0)
    anchor = widget.pair_f32(WIDGET_ANCHOR) or (0.0, 0.0)
    size = widget.pair_f32(WIDGET_SIZE) or (0.0, 0.0)
    color = widget.color(WIDGET_COLOR) or {"a": 255, "r": 255, "g": 255, "b": 255}
    return {
        "name": widget.string(WIDGET_NAME) or "",
        "action_tag": widget.i32(WIDGET_ACTION_TAG, 0),
        "rotation_x": rotation[0],
        "rotation_y": rotation[1],
        "z_order": widget.i32(WIDGET_Z, 0),
        "visible": widget.bool(WIDGET_VISIBLE, True),
        "alpha": widget.u8(WIDGET_ALPHA, 255),
        "tag": widget.i32(WIDGET_TAG, 0),
        "x": position[0],
        "y": position[1],
        "scale_x": scale[0],
        "scale_y": scale[1],
        "anchor_x": anchor[0],
        "anchor_y": anchor[1],
        "color": color,
        "width": size[0],
        "height": size[1],
        "flip_x": widget.bool(WIDGET_FLIP_X, False),
        "flip_y": widget.bool(WIDGET_FLIP_Y, False),
        "ignore_size": widget.bool(WIDGET_IGNORE_SIZE, False),
        "touch_enabled": widget.bool(WIDGET_TOUCH, False),
        "frame_event": widget.string(WIDGET_FRAME_EVENT) or "",
        "custom_property": widget.string(WIDGET_CUSTOM_PROPERTY) or "",
        "callback_type": widget.string(WIDGET_CALLBACK_TYPE) or "",
        "callback_name": widget.string(WIDGET_CALLBACK_NAME) or "",
    }


def parse_class_fields(class_table: Table | None, classname: str) -> dict[str, Any]:
    fields = CLASS_FIELDS.get(classname)
    if class_table is None or fields is None:
        return {}
    result: dict[str, Any] = {}
    for name, slot in fields.items():
        try:
            if name in RESOURCE_FIELDS:
                value = read_resource(class_table, slot)
            elif name in PAIR_FIELDS:
                pair = class_table.pair_f32(slot)
                value = {"width": pair[0], "height": pair[1]} if pair else None
            elif name in CAP_FIELDS:
                quad = class_table.quad_f32(slot)
                value = (
                    {"x": quad[0], "y": quad[1], "width": quad[2], "height": quad[3]}
                    if quad
                    else None
                )
            elif name in COLOR_FIELDS:
                value = class_table.color(slot)
            elif name in FLOAT_FIELDS:
                value = class_table.f32(slot, 0.0)
            elif name in BOOL_FIELDS:
                value = class_table.bool(slot, False)
            elif name in U8_FIELDS:
                value = class_table.u8(slot, 255)
            elif name in INT_FIELDS:
                value = class_table.i32(slot, 0)
            else:
                value = class_table.string(slot)
            if value not in (None, "", {}, []):
                result[name] = value
        except FlatBufferError:
            result[f"{name}__error"] = True
    return result


def parse_node(
    tree: Table,
    parent_id: int,
    depth: int,
    index_in_parent: int,
    node_id_counter: list[int],
    nodes: list[dict[str, Any]],
    errors: list[str],
) -> int:
    node_id_counter[0] += 1
    node_id = node_id_counter[0]
    classname = tree.string(0) or ""
    custom_class = tree.string(3) or ""
    children = tree.vector_tables(1)
    options = tree.table(2)
    data = options.table(0) if options is not None else None

    class_table: Table | None = None
    widget: Table | None = None
    try:
        if depth == 0:
            # Root Node stores WidgetOptions directly in Options.data.
            widget = data
        elif data is not None:
            # Child option tables start with widgetOptions/nodeOptions at slot 0.
            first = data.table(0)
            if looks_like_widget(first):
                class_table = data
                widget = first
            elif looks_like_widget(data):
                # Defensive fallback for a child that directly stores WidgetOptions.
                widget = data
    except FlatBufferError as exc:
        errors.append(f"node {node_id} option layout: {exc}")

    if widget is None:
        raise FlatBufferError(f"node {node_id} ({classname}) has no WidgetOptions")

    info = parse_widget(widget)
    info.update(
        {
            "id": node_id,
            "parent_id": parent_id,
            "depth": depth,
            "index_in_parent": index_in_parent,
            "class": classname,
            "custom_class": custom_class,
            "class_parsed": classname in CLASS_FIELDS,
            "class_fields": parse_class_fields(class_table, classname),
            "child_count": len(children),
        }
    )
    nodes.append(info)
    for child_index, child in enumerate(children):
        parse_node(
            child,
            node_id,
            depth + 1,
            child_index,
            node_id_counter,
            nodes,
            errors,
        )
    return node_id


def inspect_file(path: Path, root: Path, json_root: Path) -> tuple[dict[str, Any], list[dict[str, Any]], list[str]]:
    rel = path.relative_to(root).as_posix()
    data = path.read_bytes()
    errors: list[str] = []
    nodes: list[dict[str, Any]] = []
    version = ""
    root_class = ""
    root_width = 0.0
    root_height = 0.0
    textures: list[str] = []
    texture_pngs: list[str] = []
    try:
        root_table_obj = root_table(data)
        version = root_table_obj.string(0) or ""
        textures = root_table_obj.vector_strings(1)
        texture_pngs = root_table_obj.vector_strings(2)
        node_tree = root_table_obj.table(3)
        if node_tree is None:
            raise FlatBufferError("CSParseBinary.nodeTree is missing")
        root_class = node_tree.string(0) or ""
        counter = [0]
        parse_node(node_tree, 0, 0, 0, counter, nodes, errors)
        root_node = nodes[0]
        root_width = root_node["width"]
        root_height = root_node["height"]
        node_json = {
            "file": rel,
            "version": version,
            "root_class": root_class,
            "root_width": root_width,
            "root_height": root_height,
            "textures": textures,
            "texture_pngs": texture_pngs,
            "node_count": len(nodes),
            "max_depth": max((n["depth"] for n in nodes), default=0),
            "class_counts": dict(sorted(Counter(n["class"] for n in nodes).items())),
            "nodes": nodes,
        }
        json_path = json_root / rel
        json_path = json_path.with_suffix(".json")
        write_json(json_path, node_json)
        status = "OK" if not errors else "OK_WITH_NODE_ERRORS"
        index_row = {
            "file": rel,
            "size": len(data),
            "sha256": sha256_bytes(data),
            "version": version,
            "root_class": root_class,
            "root_width": root_width,
            "root_height": root_height,
            "node_count": len(nodes),
            "max_depth": node_json["max_depth"],
            "class_counts": json.dumps(node_json["class_counts"], ensure_ascii=False, separators=(",", ":")),
            "status": status,
            "error": " | ".join(errors),
        }
    except Exception as exc:  # noqa: BLE001 - collect per-file failures
        errors.append(f"{type(exc).__name__}: {exc}")
        index_row = {
            "file": rel,
            "size": len(data),
            "sha256": sha256_bytes(data),
            "version": version,
            "root_class": root_class,
            "root_width": root_width,
            "root_height": root_height,
            "node_count": len(nodes),
            "max_depth": 0,
            "class_counts": "",
            "status": "ERROR",
            "error": " | ".join(errors),
        }
    return index_row, nodes, errors


def node_rows(file_rel: str, nodes: list[dict[str, Any]]) -> list[dict[str, Any]]:
    rows = []
    for node in nodes:
        fields = node.get("class_fields", {})
        image = fields.get("image") or fields.get("normal") or fields.get("bar_image") or {}
        font = fields.get("font_resource") or {}
        rows.append(
            {
                "file": file_rel,
                "node_id": node["id"],
                "parent_id": node["parent_id"],
                "depth": node["depth"],
                "index_in_parent": node["index_in_parent"],
                "class": node["class"],
                "name": node["name"],
                "action_tag": node["action_tag"],
                "tag": node["tag"],
                "x": node["x"],
                "y": node["y"],
                "width": node["width"],
                "height": node["height"],
                "anchor_x": node["anchor_x"],
                "anchor_y": node["anchor_y"],
                "scale_x": node["scale_x"],
                "scale_y": node["scale_y"],
                "rotation_x": node["rotation_x"],
                "rotation_y": node["rotation_y"],
                "z_order": node["z_order"],
                "visible": node["visible"],
                "alpha": node["alpha"],
                "color": json.dumps(node["color"], separators=(",", ":")),
                "text": fields.get("text", ""),
                "font_name": fields.get("font_name", ""),
                "font_size": fields.get("font_size", ""),
                "font_path": font.get("path", "") if isinstance(font, dict) else "",
                "image_path": image.get("path", "") if isinstance(image, dict) else "",
                "image_plist": image.get("plist", "") if isinstance(image, dict) else "",
                "image_resource_type": image.get("resource_type", "") if isinstance(image, dict) else "",
                "child_count": node["child_count"],
                "class_parsed": node["class_parsed"],
                "class_fields_json": json.dumps(fields, ensure_ascii=False, separators=(",", ":")),
            }
        )
    return rows


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Inspect Cocos Studio CSB files.")
    parser.add_argument("--extracted", type=Path, default=Path("build/extracted"))
    parser.add_argument("--json-dir", type=Path, default=Path("build/csb_json"))
    parser.add_argument("--index", type=Path, default=Path("build/manifest/csb_index.csv"))
    parser.add_argument("--nodes", type=Path, default=Path("build/manifest/csb_nodes.csv"))
    parser.add_argument("--summary", type=Path, default=Path("build/manifest/csb_summary.json"))
    parser.add_argument("--limit", type=int, default=0, help="parse at most N files (debug)")
    parser.add_argument("--only", default="", help="only files whose relative path contains this")
    parser.add_argument("--clean", action="store_true", help="remove the JSON output dir under build/")
    args = parser.parse_args(argv)

    if not args.extracted.is_dir():
        print(f"extracted directory not found: {args.extracted}", file=sys.stderr)
        return 2
    if args.clean:
        resolved = args.json_dir.resolve()
        build_root = (Path.cwd() / "build").resolve()
        if not resolved.is_relative_to(build_root) or resolved == build_root:
            print(f"refusing --clean outside build/: {resolved}", file=sys.stderr)
            return 2
        if resolved.exists():
            shutil.rmtree(resolved)

    started = time.time()
    files = sorted(p for p in args.extracted.rglob("*.csb") if p.is_file())
    if args.only:
        files = [p for p in files if args.only in p.relative_to(args.extracted).as_posix()]
    if args.limit:
        files = files[: args.limit]
    if not files:
        print("no CSB files found", file=sys.stderr)
        return 2

    index_rows: list[dict[str, Any]] = []
    nodes_rows: list[dict[str, Any]] = []
    all_errors: list[dict[str, str]] = []
    total_nodes = 0
    class_counter: Counter[str] = Counter()
    version_counter: Counter[str] = Counter()
    root_size_counter: Counter[str] = Counter()

    for path in files:
        rel = path.relative_to(args.extracted).as_posix()
        index_row, nodes, errors = inspect_file(path, args.extracted, args.json_dir)
        index_rows.append(index_row)
        nodes_rows.extend(node_rows(rel, nodes))
        total_nodes += len(nodes)
        if index_row["version"]:
            version_counter[index_row["version"]] += 1
        if index_row["root_width"] or index_row["root_height"]:
            root_size_counter[f'{index_row["root_width"]:g}x{index_row["root_height"]:g}'] += 1
        for node in nodes:
            class_counter[node["class"]] += 1
        if errors:
            all_errors.append({"file": rel, "errors": " | ".join(errors)})
        print(f"  {rel}: {index_row['status']} nodes={len(nodes)}")

    write_csv(
        args.index,
        index_rows,
        [
            "file",
            "size",
            "sha256",
            "version",
            "root_class",
            "root_width",
            "root_height",
            "node_count",
            "max_depth",
            "class_counts",
            "status",
            "error",
        ],
    )
    write_csv(
        args.nodes,
        nodes_rows,
        [
            "file",
            "node_id",
            "parent_id",
            "depth",
            "index_in_parent",
            "class",
            "name",
            "action_tag",
            "tag",
            "x",
            "y",
            "width",
            "height",
            "anchor_x",
            "anchor_y",
            "scale_x",
            "scale_y",
            "rotation_x",
            "rotation_y",
            "z_order",
            "visible",
            "alpha",
            "color",
            "text",
            "font_name",
            "font_size",
            "font_path",
            "image_path",
            "image_plist",
            "image_resource_type",
            "child_count",
            "class_parsed",
            "class_fields_json",
        ],
    )

    errors = [row for row in index_rows if row["status"] != "OK"]
    summary = {
        "tool": "csb_inspect.py",
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "extracted": args.extracted.as_posix(),
        "files": len(files),
        "ok": len(files) - len(errors),
        "errors": errors,
        "node_errors": all_errors,
        "total_nodes": total_nodes,
        "versions": dict(sorted(version_counter.items())),
        "root_sizes": dict(sorted(root_size_counter.items())),
        "class_counts": dict(sorted(class_counter.items())),
        "index": args.index.as_posix(),
        "nodes": args.nodes.as_posix(),
        "json_dir": args.json_dir.as_posix(),
        "elapsed_seconds": round(time.time() - started, 3),
    }
    write_json(args.summary, summary)

    print("CSB_INSPECT P1b")
    print(f"  files: {len(files)}")
    print(f"  ok: {summary['ok']} errors: {len(errors)}")
    print(f"  nodes: {total_nodes}")
    print(f"  versions: {summary['versions']}")
    print(f"  index: {args.index}")
    print(f"  nodes: {args.nodes}")
    print(f"  summary: {args.summary}")
    if errors:
        for row in errors[:10]:
            print(f"  ERROR {row['file']}: {row['error']}")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
