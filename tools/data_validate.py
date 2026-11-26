#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Validate the 114 decrypted CSV tables.

The table layout is:
    row 0: Chinese display labels
    row 1: type row (number|string|boolean)
    row 2: field names
    row 3+: data

This tool checks structure, primary keys, type syntax, empty cells and
value-based foreign-key candidates.  It does not invent server semantics.
"""

from __future__ import annotations

import argparse
import csv
import io
import json
import math
import re
import sys
import time
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

TYPE_TOKENS = {"number", "string", "boolean"}
BOOL_TRUE = {"1", "true", "TRUE", "True"}
BOOL_FALSE = {"0", "false", "FALSE", "False"}


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


def normalize_table(name: str) -> str:
    return re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_")


def is_number(text: str) -> bool:
    try:
        value = float(text)
        return math.isfinite(value)
    except (TypeError, ValueError):
        return False


def validate_table(path: Path) -> tuple[dict[str, Any], list[dict[str, Any]], list[dict[str, Any]], list[dict[str, Any]], list[str]]:
    errors: list[str] = []
    raw = path.read_bytes()
    bom = raw.startswith(b"\xef\xbb\xbf")
    try:
        text = raw.decode("utf-8-sig")
    except UnicodeDecodeError as exc:
        return (
            {
                "file": path.name,
                "status": "ERROR",
                "error": f"UTF-8 decode: {exc}",
                "bytes": len(raw),
            },
            [],
            [],
            [],
            [f"{path.name}: UTF-8 decode failed: {exc}"],
        )
    try:
        parsed = list(csv.reader(io.StringIO(text)))
    except csv.Error as exc:
        return (
            {
                "file": path.name,
                "status": "ERROR",
                "error": f"CSV parse: {exc}",
                "bytes": len(raw),
            },
            [],
            [],
            [],
            [f"{path.name}: CSV parse failed: {exc}"],
        )

    rows = [row for row in parsed if any(cell != "" for cell in row)]
    if len(rows) < 3:
        return (
            {
                "file": path.name,
                "status": "ERROR",
                "error": f"expected at least 3 header rows, got {len(rows)}",
                "bytes": len(raw),
            },
            [],
            [],
            [],
            [f"{path.name}: insufficient header rows"],
        )

    labels, type_row, fields, data = rows[0], rows[1], rows[2], rows[3:]
    header_notes: list[str] = []
    width = max(len(labels), len(type_row), len(fields))
    if not (len(labels) == len(type_row) == len(fields)):
        header_notes.append(
            f"header lengths differ, padded: labels={len(labels)} types={len(type_row)} fields={len(fields)}"
        )
    labels = labels + [""] * (width - len(labels))
    type_row = type_row + [""] * (width - len(type_row))
    fields = fields + [""] * (width - len(fields))

    # A trailing unnamed, entirely empty column is a serializer artifact, not
    # a game field.  Drop it and record the normalization.
    if width > 1 and fields[-1] == "" and all(
        len(row) < width or row[-1] == "" for row in data
    ):
        labels = labels[:-1]
        type_row = type_row[:-1]
        fields = fields[:-1]
        width -= 1
        header_notes.append("dropped trailing unnamed empty column")

    if any(t not in TYPE_TOKENS for t in type_row):
        errors.append(f"unknown type token(s): {sorted(set(type_row) - TYPE_TOKENS)}")
    for index, field_name in enumerate(fields):
        if not field_name:
            fields[index] = f"field_{index}"
            header_notes.append(f"empty field name at column {index} -> field_{index}")
    duplicate_fields = [name for name, count in Counter(fields).items() if count > 1]
    if duplicate_fields:
        errors.append(f"duplicate field names: {duplicate_fields}")

    ragged = 0
    for line_no, row in enumerate(data, start=4):
        if len(row) != width:
            ragged += 1
            if len(errors) < 20:
                errors.append(f"row {line_no}: {len(row)} columns, expected {width}")

    # Normalize data rows to the header width so column checks are safe.
    normalized = []
    for row in data:
        if len(row) < width:
            row = row + [""] * (width - len(row))
        normalized.append(row[:width])

    primary_key = fields[0] if fields else ""
    ids = [row[0] if row else "" for row in normalized]
    id_counts = Counter(ids)
    duplicate_ids = sorted([value for value, count in id_counts.items() if value and count > 1])
    empty_ids = sum(1 for value in ids if not value)

    type_errors = 0
    empty_cells = 0
    columns: list[dict[str, Any]] = []
    enum_candidates: list[dict[str, Any]] = []
    for index, (label, type_name, field_name) in enumerate(zip(labels, type_row, fields)):
        values = [row[index] for row in normalized]
        empty = sum(1 for value in values if value.strip() == "")
        empty_cells += empty
        invalid: list[str] = []
        numeric_values: list[float] = []
        unique_values: set[str] = set()
        for value in values:
            stripped = value.strip()
            if stripped == "":
                continue
            unique_values.add(stripped)
            if type_name == "number":
                if not is_number(stripped):
                    if len(invalid) < 10:
                        invalid.append(value)
                else:
                    numeric_values.append(float(stripped))
            elif type_name == "boolean":
                if stripped not in BOOL_TRUE and stripped not in BOOL_FALSE:
                    if len(invalid) < 10:
                        invalid.append(value)
        type_errors += len(invalid)
        column = {
            "index": index,
            "label": label,
            "field": field_name,
            "type": type_name,
            "rows": len(values),
            "empty": empty,
            "unique": len(unique_values),
            "invalid_values": invalid,
            "invalid_count": len(invalid),
            "min": min(numeric_values) if numeric_values else "",
            "max": max(numeric_values) if numeric_values else "",
        }
        columns.append(column)
        if type_name == "number" and 1 < len(unique_values) <= 20 and not invalid:
            enum_candidates.append(
                {
                    "file": path.name,
                    "field": field_name,
                    "label": label,
                    "values": sorted(unique_values, key=lambda x: (len(x), x)),
                    "status": "CANDIDATE_UNVERIFIED",
                }
            )

    status = "OK"
    if not normalized:
        status = "EMPTY"
    elif errors or ragged or type_errors or duplicate_ids or empty_ids or header_notes:
        status = "WARN"
    table = {
        "file": path.name,
        "path": path.as_posix(),
        "bytes": len(raw),
        "bom": bom,
        "crlf_rows": raw.count(b"\r\n"),
        "labels": labels,
        "types": type_row,
        "fields": fields,
        "data_rows": len(data),
        "columns": width,
        "ragged_rows": ragged,
        "duplicate_fields": duplicate_fields,
        "primary_key": primary_key,
        "duplicate_ids": duplicate_ids,
        "duplicate_id_count": len(duplicate_ids),
        "empty_ids": empty_ids,
        "type_errors": type_errors,
        "empty_cells": empty_cells,
        "header_notes": header_notes,
        "errors": errors,
        "status": status,
    }
    return table, columns, enum_candidates, [], errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Validate decrypted CSV tables.")
    parser.add_argument("--data", type=Path, default=Path("build/plain/data"))
    parser.add_argument("--reports", type=Path, default=Path("build/reports"))
    parser.add_argument("--schema", type=Path, default=Path("build/manifest/data_schema.json"))
    parser.add_argument("--summary", type=Path, default=Path("build/manifest/data_summary.json"))
    args = parser.parse_args(argv)

    if not args.data.is_dir():
        print(f"data directory not found: {args.data}", file=sys.stderr)
        return 2
    started = time.time()
    files = sorted(args.data.glob("*.csv"))
    tables: list[dict[str, Any]] = []
    all_columns: list[dict[str, Any]] = []
    all_enums: list[dict[str, Any]] = []
    all_errors: list[str] = []
    total_data_rows = 0
    for path in files:
        table, columns, enums, _unused, errors = validate_table(path)
        tables.append(table)
        all_columns.extend({"file": path.name, **column} for column in columns)
        all_enums.extend(enums)
        all_errors.extend(f"{path.name}: {e}" for e in errors)
        total_data_rows += int(table.get("data_rows", 0))

    # Value-based FK candidates: column "foo_id" -> table foo.csv, if present.
    pk_sets: dict[str, set[str]] = {}
    for table in tables:
        if table.get("status") == "ERROR":
            continue
        path = args.data / table["file"]
        rows = list(csv.reader(io.StringIO(path.read_text(encoding="utf-8-sig"))))
        rows = [row for row in rows if any(cell != "" for cell in row)]
        pk_sets[normalize_table(table["file"].removesuffix(".csv"))] = {
            row[0] for row in rows[3:] if row and row[0]
        }

    fk_rows: list[dict[str, Any]] = []
    for table in tables:
        if table.get("status") == "ERROR":
            continue
        source_path = args.data / table["file"]
        rows = list(csv.reader(io.StringIO(source_path.read_text(encoding="utf-8-sig"))))
        rows = [row for row in rows if any(cell != "" for cell in row)]
        fields = table["fields"]
        for index, field_name in enumerate(fields):
            if index == 0:
                continue
            lower = field_name.lower()
            if not lower.endswith("_id"):
                continue
            target_key = normalize_table(lower[:-3])
            if target_key not in pk_sets:
                continue
            values = [row[index] for row in rows[3:] if index < len(row) and row[index]]
            if not values:
                continue
            found = sum(1 for value in values if value in pk_sets[target_key])
            coverage = found / len(values)
            fk_rows.append(
                {
                    "source_file": table["file"],
                    "source_field": field_name,
                    "target_file": f"{target_key}.csv",
                    "target_field": "ID",
                    "checked_values": len(values),
                    "found": found,
                    "coverage": round(coverage, 4),
                    "status": "CANDIDATE_MATCH" if coverage == 1.0 else "CANDIDATE_PARTIAL",
                }
            )

    write_csv(
        args.reports / "data_validation.csv",
        [
            {
                "file": t["file"],
                "data_rows": t.get("data_rows", ""),
                "columns": t.get("columns", ""),
                "primary_key": t.get("primary_key", ""),
                "duplicate_ids": t.get("duplicate_id_count", ""),
                "empty_ids": t.get("empty_ids", ""),
                "ragged_rows": t.get("ragged_rows", ""),
                "type_errors": t.get("type_errors", ""),
                "empty_cells": t.get("empty_cells", ""),
                "status": t.get("status", "ERROR"),
                "errors": " | ".join(t.get("errors", [])),
            }
            for t in tables
        ],
        [
            "file",
            "data_rows",
            "columns",
            "primary_key",
            "duplicate_ids",
            "empty_ids",
            "ragged_rows",
            "type_errors",
            "empty_cells",
            "status",
            "errors",
        ],
    )
    write_csv(
        args.reports / "data_columns.csv",
        all_columns,
        [
            "file",
            "index",
            "label",
            "field",
            "type",
            "rows",
            "empty",
            "unique",
            "invalid_count",
            "invalid_values",
            "min",
            "max",
        ],
    )
    write_csv(
        args.reports / "data_fk.csv",
        fk_rows,
        [
            "source_file",
            "source_field",
            "target_file",
            "target_field",
            "checked_values",
            "found",
            "coverage",
            "status",
        ],
    )
    write_csv(
        args.reports / "data_enum_candidates.csv",
        all_enums,
        ["file", "field", "label", "values", "status"],
    )
    write_json(
        args.schema,
        {"tables": tables, "columns": all_columns, "fk_candidates": fk_rows},
    )

    counts = Counter(t.get("status", "ERROR") for t in tables)
    summary = {
        "tool": "data_validate.py",
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "data_dir": args.data.as_posix(),
        "tables": len(tables),
        "data_rows": total_data_rows,
        "status_counts": dict(counts),
        "parse_errors": len([t for t in tables if t.get("status") == "ERROR"]),
        "tables_with_warnings": len([t for t in tables if t.get("status") == "WARN"]),
        "empty_tables": [t["file"] for t in tables if t.get("status") == "EMPTY"],
        "header_normalizations": [
            {"file": t["file"], "notes": t.get("header_notes", [])}
            for t in tables
            if t.get("header_notes")
        ],
        "duplicate_id_tables": [
            {"file": t["file"], "duplicates": t["duplicate_ids"]}
            for t in tables
            if t.get("duplicate_ids")
        ],
        "ragged_tables": [t["file"] for t in tables if t.get("ragged_rows")],
        "type_error_tables": [t["file"] for t in tables if t.get("type_errors")],
        "columns": len(all_columns),
        "empty_cells": sum(int(t.get("empty_cells", 0) or 0) for t in tables),
        "fk_candidates": len(fk_rows),
        "enum_candidates": len(all_enums),
        "errors": all_errors[:100],
        "elapsed_seconds": round(time.time() - started, 3),
    }
    write_json(args.summary, summary)

    print("DATA_VALIDATE P1b")
    print(f"  tables: {len(tables)}")
    print(f"  data_rows: {total_data_rows}")
    print(f"  status: {dict(counts)}")
    print(f"  duplicate_id_tables: {len(summary['duplicate_id_tables'])}")
    print(f"  parse_errors: {summary['parse_errors']}")
    print(f"  fk_candidates: {len(fk_rows)}")
    print(f"  summary: {args.summary}")
    return 1 if summary["parse_errors"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
