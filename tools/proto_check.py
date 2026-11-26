#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Optional check: compile proto/*.proto and compare with msg.pb descriptors.

Requires grpcio-tools.  It is not a runtime dependency of the game; it is a
build-time validation tool.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from google.protobuf import descriptor_pb2

try:
    from grpc_tools import protoc
except Exception:  # pragma: no cover - optional dependency
    protoc = None


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def canonical(fds: descriptor_pb2.FileDescriptorSet) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for f in fds.file:
        result[f.name] = (
            f.name,
            f.package,
            f.syntax,
            tuple(f.dependency),
            tuple(
                (
                    m.name,
                    tuple(
                        (x.name, x.number, x.label, x.type, x.type_name)
                        for x in m.field
                    ),
                )
                for m in f.message_type
            ),
            tuple(
                (e.name, tuple((v.name, v.number) for v in e.value))
                for e in f.enum_type
            ),
        )
    return result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Compile and compare generated proto files.")
    parser.add_argument("--proto", type=Path, default=Path("proto"))
    parser.add_argument("--pb", type=Path, default=Path("build/plain/proto/msg.pb"))
    parser.add_argument("--out", type=Path, default=Path("build/manifest/proto_compile_check.json"))
    args = parser.parse_args(argv)

    if protoc is None:
        print("grpc_tools is not installed; skipping proto compile check", file=sys.stderr)
        return 2
    if not args.proto.is_dir() or not args.pb.is_file():
        print("proto directory or msg.pb missing", file=sys.stderr)
        return 2

    files = sorted(
        p.relative_to(args.proto).as_posix()
        for p in args.proto.rglob("*.proto")
        if p.is_file()
    )
    with tempfile.TemporaryDirectory(prefix="aam_proto_") as tmp:
        descriptor_out = Path(tmp) / "regen.pb"
        old_cwd = Path.cwd()
        try:
            os.chdir(args.proto)
            result = protoc.main(
                [
                    "protoc",
                    "-I.",
                    f"--descriptor_set_out={descriptor_out}",
                    "--include_imports",
                    *files,
                ]
            )
        finally:
            os.chdir(old_cwd)
        if result != 0:
            print(f"protoc failed with exit {result}", file=sys.stderr)
            return 1
        generated = descriptor_pb2.FileDescriptorSet()
        generated.ParseFromString(descriptor_out.read_bytes())

    original = descriptor_pb2.FileDescriptorSet()
    original.ParseFromString(args.pb.read_bytes())
    a = canonical(original)
    b = canonical(generated)
    differences = sorted(set(a) ^ set(b)) + sorted(
        name for name in set(a) & set(b) if a[name] != b[name]
    )
    summary = {
        "tool": "proto_check.py",
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "protoc_available": True,
        "proto_files": len(files),
        "original_files": len(a),
        "generated_files": len(b),
        "canonical_equal": not differences,
        "differences": differences,
        "original_messages": sum(len(f.message_type) for f in original.file),
        "generated_messages": sum(len(f.message_type) for f in generated.file),
        "original_fields": sum(len(m.field) for f in original.file for m in f.message_type),
        "generated_fields": sum(len(m.field) for f in generated.file for m in f.message_type),
        "status": "OK" if not differences else "MISMATCH",
    }
    write_json(args.out, summary)
    print("PROTO_CHECK")
    print(f"  files: {len(files)}")
    print(f"  canonical_equal: {summary['canonical_equal']}")
    print(f"  differences: {len(differences)}")
    print(f"  summary: {args.out}")
    return 0 if not differences else 1


if __name__ == "__main__":
    raise SystemExit(main())
