#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Reconstruct .proto files from the plaintext FileDescriptorSet in msg.pb."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from google.protobuf import descriptor_pb2


TYPE_NAMES = {
    1: "double",
    2: "float",
    3: "int64",
    4: "uint64",
    5: "int32",
    6: "fixed64",
    7: "fixed32",
    8: "bool",
    9: "string",
    10: "group",
    11: "message",
    12: "bytes",
    13: "uint32",
    14: "enum",
    15: "sfixed32",
    16: "sfixed64",
    17: "sint32",
    18: "sint64",
}

LABELS = {
    1: "optional",
    2: "required",
    3: "repeated",
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


def field_line(field: descriptor_pb2.FieldDescriptorProto) -> str:
    label = LABELS.get(field.label, "optional")
    if field.type in (11, 14):
        type_name = field.type_name or "UNKNOWN"
        return f"  {label} {type_name} {field.name} = {field.number};"
    type_name = TYPE_NAMES.get(field.type, f"UNKNOWN_TYPE_{field.type}")
    return f"  {label} {type_name} {field.name} = {field.number};"


def enum_block(enum: descriptor_pb2.EnumDescriptorProto, indent: str = "") -> list[str]:
    lines = [f"{indent}enum {enum.name} {{"]
    for value in enum.value:
        lines.append(f"{indent}  {value.name} = {value.number};")
    lines.append(f"{indent}}}")
    return lines


def message_block(message: descriptor_pb2.DescriptorProto, indent: str = "") -> list[str]:
    lines = [f"{indent}message {message.name} {{"]
    for enum in message.enum_type:
        lines.extend(enum_block(enum, indent + "  "))
        lines.append("")
    for nested in message.nested_type:
        lines.extend(message_block(nested, indent + "  "))
        lines.append("")
    for field in message.field:
        lines.append(f"{indent}  {field_line(field).strip()}")
    lines.append(f"{indent}}}")
    return lines


def render_file(file_descriptor: descriptor_pb2.FileDescriptorProto) -> str:
    lines: list[str] = ['syntax = "proto2";', ""]
    if file_descriptor.package:
        lines.append(f"package {file_descriptor.package};")
        lines.append("")
    for dependency in file_descriptor.dependency:
        lines.append(f'import "{dependency}";')
    if file_descriptor.dependency:
        lines.append("")
    for enum in file_descriptor.enum_type:
        lines.extend(enum_block(enum))
        lines.append("")
    for message in file_descriptor.message_type:
        lines.extend(message_block(message))
        lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Generate .proto files from msg.pb.")
    parser.add_argument("--pb", type=Path, default=Path("build/plain/proto/msg.pb"))
    parser.add_argument("--out", type=Path, default=Path("proto"))
    parser.add_argument("--index", type=Path, default=Path("build/manifest/proto_index.csv"))
    parser.add_argument("--summary", type=Path, default=Path("build/manifest/proto_summary.json"))
    args = parser.parse_args(argv)

    if not args.pb.is_file():
        print(f"msg.pb not found: {args.pb}", file=sys.stderr)
        return 2
    raw = args.pb.read_bytes()
    fds = descriptor_pb2.FileDescriptorSet()
    try:
        fds.ParseFromString(raw)
    except Exception as exc:  # noqa: BLE001
        print(f"msg.pb parse failed: {exc}", file=sys.stderr)
        return 2

    args.out.mkdir(parents=True, exist_ok=True)
    rows: list[dict[str, Any]] = []
    errors: list[str] = []
    total_messages = 0
    total_enums = 0
    total_fields = 0
    for file_descriptor in fds.file:
        target = args.out / file_descriptor.name
        try:
            target.parent.mkdir(parents=True, exist_ok=True)
            text = render_file(file_descriptor)
            target.write_text(text, encoding="utf-8", newline="\n")
            messages = len(file_descriptor.message_type)
            enums = len(file_descriptor.enum_type)
            fields = sum(len(m.field) for m in file_descriptor.message_type)
            total_messages += messages
            total_enums += enums
            total_fields += fields
            rows.append(
                {
                    "file": file_descriptor.name,
                    "package": file_descriptor.package,
                    "syntax": file_descriptor.syntax or "proto2",
                    "messages": messages,
                    "enums": enums,
                    "fields": fields,
                    "dependencies": len(file_descriptor.dependency),
                    "size": len(text.encode("utf-8")),
                    "sha256": hashlib.sha256(text.encode("utf-8")).hexdigest(),
                    "status": "OK",
                }
            )
        except Exception as exc:  # noqa: BLE001
            errors.append(f"{file_descriptor.name}: {type(exc).__name__}: {exc}")
            rows.append(
                {
                    "file": file_descriptor.name,
                    "package": file_descriptor.package,
                    "syntax": file_descriptor.syntax or "proto2",
                    "messages": 0,
                    "enums": 0,
                    "fields": 0,
                    "dependencies": 0,
                    "size": 0,
                    "sha256": "",
                    "status": "ERROR",
                }
            )

    write_csv(
        args.index,
        rows,
        [
            "file",
            "package",
            "syntax",
            "messages",
            "enums",
            "fields",
            "dependencies",
            "size",
            "sha256",
            "status",
        ],
    )
    summary = {
        "tool": "proto_extract.py",
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "pb": args.pb.as_posix(),
        "pb_sha256": hashlib.sha256(raw).hexdigest(),
        "files": len(fds.file),
        "messages": total_messages,
        "enums": total_enums,
        "fields": total_fields,
        "errors": errors,
        "out": args.out.as_posix(),
        "index": args.index.as_posix(),
        "status": "OK" if not errors else "ERROR",
    }
    write_json(args.summary, summary)
    print("PROTO_EXTRACT P1b")
    print(f"  files: {summary['files']}")
    print(f"  messages: {summary['messages']}")
    print(f"  fields: {summary['fields']}")
    print(f"  enums: {summary['enums']}")
    print(f"  errors: {len(errors)}")
    print(f"  out: {args.out}")
    print(f"  summary: {args.summary}")
    return 0 if not errors else 1


if __name__ == "__main__":
    raise SystemExit(main())
