#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Read-only probe for the supplied Adventure & Mining 1.1.5 APK.

This is a P0 evidence tool, not a game implementation.  It never extracts
assets or writes decrypted payloads to disk.  It produces machine-readable
facts under --out so docs/00-facts.md can be reproduced.

Usage:
    python tools/apk_probe.py --apk "F:\\base(2).apk.1" --out build/probe
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
import struct
import sys
import zipfile
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

try:
    from google.protobuf import descriptor_pb2
except Exception:  # pragma: no cover - optional dependency
    descriptor_pb2 = None

try:
    from .mu_codec import MU_SIGN, decrypt_mu_payload
except ImportError:  # direct script execution: python tools/apk_probe.py
    from mu_codec import MU_SIGN, decrypt_mu_payload


TOOL_VERSION = "0.1.0"
EXPECTED_APK_SHA256 = (
    "256c7e24c72ef9ff6ffd0a3b6f1ba864bbcb93db859a3fbcc293cb9496f8bd3d"
)

NESTED_ARCHIVES = [
    "assets/ui.zip",
    "assets/role.zip",
    "assets/spine.zip",
    "assets/sound.zip",
    "assets/animation.zip",
    "assets/effect.zip",
    "assets/particle.zip",
    "assets/fonts.zip",
    "assets/shader.zip",
    "assets/adventure.zip",
    "assets/battle_background.zip",
    "assets/language.zip",
]


# ---------------------------------------------------------------------------
# Small binary helpers for Android's binary XML format
# ---------------------------------------------------------------------------


def _u16(data: bytes, off: int) -> int:
    return struct.unpack_from("<H", data, off)[0]


def _u32(data: bytes, off: int) -> int:
    return struct.unpack_from("<I", data, off)[0]


def _parse_string_pool(data: bytes, start: int) -> tuple[list[str], int]:
    """Parse a ResStringPool chunk and return (strings, chunk_size)."""

    chunk_type = _u16(data, start)
    header_size = _u16(data, start + 2)
    chunk_size = _u32(data, start + 4)
    if chunk_type != 0x0001:
        raise ValueError(f"expected string pool at 0x{start:x}, got 0x{chunk_type:x}")
    if chunk_size < 8:
        raise ValueError("invalid string-pool size")

    string_count = _u32(data, start + 8)
    style_count = _u32(data, start + 12)
    flags = _u32(data, start + 16)
    strings_start = _u32(data, start + 20)
    offsets_start = start + header_size
    offsets_end = offsets_start + string_count * 4
    if offsets_end > start + chunk_size:
        raise ValueError("string pool offsets exceed chunk")
    offsets = [_u32(data, offsets_start + i * 4) for i in range(string_count)]
    base = start + strings_start
    utf8 = bool(flags & 0x100)
    strings: list[str] = []

    def read_len8(pos: int) -> tuple[int, int]:
        first = data[pos]
        pos += 1
        if first & 0x80:
            return ((first & 0x7F) << 8) | data[pos], pos + 1
        return first, pos

    for off in offsets:
        pos = base + off
        if pos >= start + chunk_size:
            strings.append("")
            continue
        if utf8:
            # UTF-16 length (1 or 2 bytes), then UTF-8 byte length (1 or 2).
            _utf16_len, pos = read_len8(pos)
            byte_len, pos = read_len8(pos)
            raw = data[pos : pos + byte_len]
            strings.append(raw.decode("utf-8", errors="replace"))
        else:
            length = _u16(data, pos)
            pos += 2
            if length & 0x8000:
                length = ((length & 0x7FFF) << 16) | _u16(data, pos)
                pos += 2
            raw = data[pos : pos + length * 2]
            strings.append(raw.decode("utf-16le", errors="replace"))

    # style_count is present for format completeness; this probe does not use
    # spans.  Validate the basic pool geometry, not style payload contents.
    _ = style_count
    return strings, chunk_size


def _parse_axml(data: bytes) -> dict[str, Any]:
    """Extract manifest attributes needed by P0 from AndroidManifest.xml."""

    if len(data) < 8 or _u16(data, 0) != 0x0003:
        raise ValueError("not an Android binary XML document")
    strings: list[str] = []
    elements: list[dict[str, Any]] = []
    pos = 8
    while pos + 8 <= len(data):
        chunk_type = _u16(data, pos)
        header_size = _u16(data, pos + 2)
        chunk_size = _u32(data, pos + 4)
        if chunk_size < 8 or pos + chunk_size > len(data):
            break
        if chunk_type == 0x0001:
            strings, _ = _parse_string_pool(data, pos)
        elif chunk_type == 0x0102:
            body = pos + header_size
            if body + 20 > pos + chunk_size:
                break
            name_idx = _u32(data, body + 4)
            attr_start = _u16(data, body + 8)
            attr_size = _u16(data, body + 10)
            attr_count = _u16(data, body + 12)
            attrs: dict[str, Any] = {}
            attr_base = body + attr_start
            for i in range(attr_count):
                ap = attr_base + i * attr_size
                if ap + 20 > pos + chunk_size:
                    break
                name = strings[_u32(data, ap + 4)] if _u32(data, ap + 4) < len(strings) else None
                raw_idx = _u32(data, ap + 8)
                data_type = data[ap + 15]
                value_u32 = _u32(data, ap + 16)
                if raw_idx != 0xFFFFFFFF and raw_idx < len(strings):
                    value: Any = strings[raw_idx]
                elif data_type == 0x03 and value_u32 < len(strings):
                    value = strings[value_u32]
                elif data_type in (0x10, 0x11, 0x12):
                    value = value_u32
                else:
                    value = value_u32
                if name:
                    attrs[name] = value
            elements.append(
                {
                    "name": strings[name_idx] if name_idx < len(strings) else None,
                    "attrs": attrs,
                }
            )
        pos += chunk_size

    manifest = next((e for e in elements if e["name"] == "manifest"), None)
    uses_sdk = next((e for e in elements if e["name"] == "uses-sdk"), None)
    app = next((e for e in elements if e["name"] == "application"), None)
    activities = [e for e in elements if e["name"] == "activity"]
    if manifest is None:
        raise ValueError("manifest element not found")

    def attr(element: dict[str, Any] | None, key: str) -> Any:
        return element["attrs"].get(key) if element else None

    result = {
        "package": attr(manifest, "package"),
        "versionName": attr(manifest, "versionName"),
        "versionCode": attr(manifest, "versionCode"),
        "compileSdkVersion": attr(manifest, "compileSdkVersion"),
        "platformBuildVersionCode": attr(manifest, "platformBuildVersionCode"),
        "minSdkVersion": attr(uses_sdk, "minSdkVersion"),
        "targetSdkVersion": attr(uses_sdk, "targetSdkVersion"),
        "application_name": attr(app, "name"),
        "activities": [
            {
                "name": a["attrs"].get("name"),
                "screenOrientation": a["attrs"].get("screenOrientation"),
                "exported": a["attrs"].get("exported"),
            }
            for a in activities
        ],
    }
    return result


# ---------------------------------------------------------------------------
# Small helpers
# ---------------------------------------------------------------------------


def _sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _write_json(path: Path, value: Any) -> None:
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def _write_csv(path: Path, rows: list[dict[str, Any]], fieldnames: list[str]) -> None:
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames, lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


def _normalise_ext(name: str) -> str:
    base = name.rsplit("/", 1)[-1]
    if "." not in base or base.endswith("."):
        return ""
    return "." + base.rsplit(".", 1)[-1].lower()


def _count_extensions(names: list[str]) -> dict[str, int]:
    counter: Counter[str] = Counter()
    for name in names:
        ext = _normalise_ext(name)
        if ext:
            counter[ext] += 1
    return dict(sorted(counter.items()))


# ---------------------------------------------------------------------------
# Probe
# ---------------------------------------------------------------------------


def probe(apk_path: Path, out_dir: Path) -> dict[str, Any]:
    out_dir.mkdir(parents=True, exist_ok=True)
    apk = zipfile.ZipFile(apk_path)
    entries = apk.infolist()
    names = apk.namelist()

    top_level = Counter(
        name.split("/", 1)[0] for name in names if name and not name.endswith("/")
    )
    abis = sorted(
        {
            name.split("/")[1]
            for name in names
            if name.startswith("lib/") and name.count("/") >= 2
        }
    )

    input_info = {
        "path": str(apk_path),
        "size": apk_path.stat().st_size,
        "sha256": _sha256_file(apk_path),
        "zip_entries": len(entries),
        "zip_test": apk.testzip(),
        "top_level_file_counts": dict(sorted(top_level.items())),
        "asset_entries": [n for n in names if n.startswith("assets/")],
        "native_abis": abis,
        "native_libs": [n for n in names if n.startswith("lib/") and n.endswith(".so")],
    }

    manifest_bytes = apk.read("AndroidManifest.xml")
    android_manifest = _parse_axml(manifest_bytes)

    resources = apk.read("resources.arsc")
    title = "冒险与挖矿"
    title_bytes = title.encode("utf-8")
    title_offset = resources.find(title_bytes)
    game_name = {
        "text": title if title_offset >= 0 else None,
        "utf8_offset": title_offset if title_offset >= 0 else None,
        "resource_sha256": _sha256_bytes(resources),
    }

    version_manifest = json.loads(apk.read("assets/version.manifest").decode("utf-8"))
    assets_manifest = json.loads(apk.read("assets/assets.manifest").decode("utf-8"))
    channel_text = apk.read("assets/channel.txt").decode("utf-8", errors="replace")
    channel = {}
    for line in channel_text.splitlines():
        if "=" in line:
            key, value = line.split("=", 1)
            if key.strip() == "talkingdata_channel":
                channel["talkingdata_channel"] = value.strip()

    nested: dict[str, Any] = {}
    for archive_name in NESTED_ARCHIVES:
        raw = apk.read(archive_name)
        with zipfile.ZipFile(io.BytesIO(raw)) as nested_zip:
            nested_names = nested_zip.namelist()
            files = [n for n in nested_names if not n.endswith("/")]
            dirs = len(nested_names) - len(files)
            info = {
                "compressed_size": apk.getinfo(archive_name).compress_size,
                "size": len(raw),
                "sha256": _sha256_bytes(raw),
                "entries": len(nested_names),
                "files": len(files),
                "dirs": dirs,
                "extensions": _count_extensions(files),
                "total_uncompressed": sum(nested_zip.getinfo(n).file_size for n in files),
                "zip_test": nested_zip.testzip(),
            }
            if archive_name == "assets/ui.zip":
                csb_names = [n for n in files if n.endswith(".csb")]
                info["csb_count"] = len(csb_names)
                info["csb_version_2_1_0_0"] = sum(
                    b"2.1.0.0" in nested_zip.read(n) for n in csb_names
                )
                info["csb_referencing_ui_plist"] = sum(
                    b"ui.plist" in nested_zip.read(n) for n in csb_names
                )
            if archive_name == "assets/spine.zip":
                json_names = [n for n in files if n.endswith(".json")]
                versions: Counter[str] = Counter()
                errors: list[str] = []
                for json_name in json_names:
                    try:
                        document = json.loads(nested_zip.read(json_name))
                        versions[str(document.get("skeleton", {}).get("spine"))] += 1
                    except Exception as exc:  # noqa: BLE001 - report, don't hide
                        errors.append(f"{json_name}: {type(exc).__name__}: {exc}")
                info["spine_json_count"] = len(json_names)
                info["spine_atlas_count"] = sum(n.endswith(".atlas") for n in files)
                info["spine_png_count"] = sum(n.endswith(".png") for n in files)
                info["spine_versions"] = dict(sorted(versions.items()))
                info["spine_errors"] = errors
            if archive_name == "assets/sound.zip":
                mp3 = {Path(n).stem for n in files if n.endswith(".mp3")}
                ogg = {Path(n).stem for n in files if n.endswith(".ogg")}
                info["mp3_count"] = len(mp3)
                info["ogg_count"] = len(ogg)
                info["paired_basenames"] = len(mp3 & ogg)
                info["mp3_only"] = sorted(mp3 - ogg)
                info["ogg_only"] = sorted(ogg - mp3)
            nested[archive_name] = info

    # Compare the hot-update manifest with the bytes actually shipped in the APK.
    integrity: list[dict[str, Any]] = []
    for name, expected in assets_manifest.get("assets", {}).items():
        archive_member = "assets/" + name
        try:
            raw = apk.read(archive_member)
        except KeyError:
            integrity.append({"name": name, "status": "MISSING_FROM_APK"})
            continue
        actual_md5 = hashlib.md5(raw).hexdigest()
        integrity.append(
            {
                "name": name,
                "expected_size": expected.get("size"),
                "actual_size": len(raw),
                "expected_md5": expected.get("md5"),
                "actual_md5": actual_md5,
                "size_match": len(raw) == expected.get("size"),
                "md5_match": actual_md5 == expected.get("md5"),
                "status": "MATCH" if (len(raw) == expected.get("size") and actual_md5 == expected.get("md5")) else "MISMATCH",
            }
        )

    # .mu container / payload probe.
    mu: dict[str, Any] = {}
    src_zip = zipfile.ZipFile(io.BytesIO(apk.read("assets/src.mu")))
    data_zip = zipfile.ZipFile(io.BytesIO(apk.read("assets/data.mu")))
    src_infos = [i for i in src_zip.infolist() if not i.is_dir()]
    data_infos = [i for i in data_zip.infolist() if not i.is_dir()]
    for label, infos, archive in (
        ("src", src_infos, src_zip),
        ("data", data_infos, data_zip),
    ):
        mu[label] = {
            "entries": len(infos),
            "methods": dict(sorted(Counter(i.compress_type for i in infos).items())),
            "flag_bits": dict(sorted(Counter(i.flag_bits for i in infos).items())),
            "zip_encrypted_entries": sum(bool(i.flag_bits & 1) for i in infos),
            "extensions": dict(sorted(Counter(_normalise_ext(i.filename) for i in infos).items())),
            "bytes": sum(i.file_size for i in infos),
        }

    lua_infos = [i for i in src_infos if i.filename.endswith(".lua")]
    lua_ok = 0
    lua_errors: list[str] = []
    lua_plain_bytes = 0
    lua_signs = 0
    lua_sample = None
    for info in lua_infos:
        try:
            payload, meta = decrypt_mu_payload(src_zip.read(info), signed=True)
            text = payload.decode("utf-8")
            if "\x00" in text:
                raise ValueError("NUL in plaintext")
            lua_ok += 1
            lua_plain_bytes += len(payload)
            lua_signs += int(meta["sign_ok"])
            if info.filename == "src/main.lua":
                lua_sample = {
                    "name": info.filename,
                    "cipher_len": meta["cipher_len"],
                    "plain_len": meta["plain_len"],
                    "first_line": text.splitlines()[0] if text.splitlines() else "",
                }
        except Exception as exc:  # noqa: BLE001 - report, don't hide
            lua_errors.append(f"{info.filename}: {type(exc).__name__}: {exc}")
    mu["src"].update(
        {
            "lua_count": len(lua_infos),
            "lua_decrypted_utf8": lua_ok,
            "lua_sign_ok": lua_signs,
            "lua_plaintext_bytes": lua_plain_bytes,
            "lua_errors": lua_errors,
            "lua_sample": lua_sample,
        }
    )

    pb_info = next((i for i in src_infos if i.filename.endswith(".pb")), None)
    pb_bytes = src_zip.read(pb_info) if pb_info else None
    if pb_info is not None:
        mu["src"]["pb"] = {
            "name": pb_info.filename,
            "size": len(pb_bytes),
            "starts_with_sign": bool(pb_bytes.startswith(MU_SIGN)),
            "head_hex": pb_bytes[:16].hex(),
            "sha256": _sha256_bytes(pb_bytes),
        }

    csv_infos = [i for i in data_infos if i.filename.endswith(".csv")]
    csv_ok = 0
    csv_errors: list[str] = []
    csv_total_rows = 0
    csv_plain_bytes = 0
    csv_sample = None
    for info in csv_infos:
        try:
            payload, meta = decrypt_mu_payload(data_zip.read(info), signed=False)
            text = payload.decode("utf-8-sig")
            rows = list(csv.reader(io.StringIO(text)))
            if not rows or not rows[0]:
                raise ValueError("empty CSV or header")
            csv_ok += 1
            csv_total_rows += len(rows)
            csv_plain_bytes += len(payload)
            if info.filename == "res/data/achievement.csv":
                csv_sample = {
                    "name": info.filename,
                    "cipher_len": meta["cipher_len"],
                    "plain_len": meta["plain_len"],
                    "rows_including_header": len(rows),
                    "header": rows[0],
                }
        except Exception as exc:  # noqa: BLE001 - report, don't hide
            csv_errors.append(f"{info.filename}: {type(exc).__name__}: {exc}")
    mu["data"].update(
        {
            "csv_count": len(csv_infos),
            "csv_decrypted_parsed": csv_ok,
            "csv_total_rows_including_header": csv_total_rows,
            "csv_plaintext_bytes": csv_plain_bytes,
            "csv_errors": csv_errors,
            "csv_sample": csv_sample,
        }
    )

    # protobuf schema (plaintext FileDescriptorSet inside src.mu).
    protobuf: dict[str, Any] = {"available": descriptor_pb2 is not None}
    if descriptor_pb2 is not None and pb_bytes is not None:
        try:
            fds = descriptor_pb2.FileDescriptorSet()
            fds.ParseFromString(pb_bytes)
            messages = 0
            fields = 0
            services = 0
            c2gs_fields = None
            gs2c_fields = None
            for file_descriptor in fds.file:
                services += len(file_descriptor.service)
                for message in file_descriptor.message_type:
                    messages += 1
                    fields += len(message.field)
                    if message.name == "C2GS":
                        c2gs_fields = len(message.field)
                    if message.name == "GS2C":
                        gs2c_fields = len(message.field)
            protobuf.update(
                {
                    "parse_ok": True,
                    "files": len(fds.file),
                    "top_level_messages": messages,
                    "top_level_fields": fields,
                    "services": services,
                    "c2gs_fields": c2gs_fields,
                    "gs2c_fields": gs2c_fields,
                    "file_names": [f.name for f in fds.file],
                }
            )
        except Exception as exc:  # noqa: BLE001 - report, don't hide
            protobuf.update({"parse_ok": False, "error": f"{type(exc).__name__}: {exc}"})
    else:
        protobuf["parse_ok"] = False
        protobuf["error"] = "google.protobuf unavailable or msg.pb missing"

    checks: list[dict[str, Any]] = []

    def add_check(name: str, expected: Any, actual: Any, status: str | None = None) -> None:
        if status is None:
            status = "PASS" if actual == expected else "FAIL"
        checks.append(
            {
                "name": name,
                "expected": expected,
                "actual": actual,
                "status": status,
            }
        )

    add_check("apk_sha256", EXPECTED_APK_SHA256, input_info["sha256"])
    add_check("apk_zip_integrity", None, input_info["zip_test"])
    add_check("android_package", "com.zkmxywk1.lhyy.xyaz", android_manifest["package"])
    add_check("android_version_name", "1.1.5", android_manifest["versionName"])
    add_check("android_version_code", 11, android_manifest["versionCode"])
    add_check("android_screen_orientation_portrait", 1, android_manifest["activities"][0]["screenOrientation"] if android_manifest["activities"] else None)
    add_check("hot_update_version", "2.4.0", version_manifest.get("version"))
    add_check("hot_update_build", 14, version_manifest.get("build"))
    add_check("hot_update_operator", "lehui", version_manifest.get("operator"))
    add_check("engine_version", "3.4", assets_manifest.get("engineVersion"))
    add_check("game_name_utf8_present", True, bool(game_name["text"]))
    add_check("ui_csb_count", 337, nested.get("assets/ui.zip", {}).get("csb_count"))
    add_check("ui_png_count", 321, nested.get("assets/ui.zip", {}).get("extensions", {}).get(".png"))
    add_check("ui_csb_version_2_1_0_0", 337, nested.get("assets/ui.zip", {}).get("csb_version_2_1_0_0"))
    add_check("ui_csb_referencing_ui_plist", 299, nested.get("assets/ui.zip", {}).get("csb_referencing_ui_plist"))
    add_check("role_png_count", 613, nested.get("assets/role.zip", {}).get("extensions", {}).get(".png"))
    add_check("spine_json_count", 60, nested.get("assets/spine.zip", {}).get("spine_json_count"))
    add_check("spine_atlas_count", 60, nested.get("assets/spine.zip", {}).get("spine_atlas_count"))
    add_check("sound_mp3_count", 62, nested.get("assets/sound.zip", {}).get("mp3_count"))
    add_check("sound_ogg_count", 62, nested.get("assets/sound.zip", {}).get("ogg_count"))
    add_check("sound_pairs", 62, nested.get("assets/sound.zip", {}).get("paired_basenames"))
    add_check("src_lua_count", 483, mu["src"].get("lua_count"))
    add_check("src_lua_decrypted_utf8", 483, mu["src"].get("lua_decrypted_utf8"))
    add_check("src_lua_sign_ok", 483, mu["src"].get("lua_sign_ok"))
    add_check("src_zip_encrypted_entries", 0, mu["src"].get("zip_encrypted_entries"))
    add_check("data_csv_count", 114, mu["data"].get("csv_count"))
    add_check("data_csv_decrypted_parsed", 114, mu["data"].get("csv_decrypted_parsed"))
    add_check("data_zip_encrypted_entries", 0, mu["data"].get("zip_encrypted_entries"))
    add_check("csv_total_rows_including_header", 28595, mu["data"].get("csv_total_rows_including_header"))
    add_check("protobuf_parse_ok", True, protobuf.get("parse_ok"))
    add_check("protobuf_files", 52, protobuf.get("files"))
    add_check("protobuf_top_level_messages", 884, protobuf.get("top_level_messages"))
    add_check("protobuf_top_level_fields", 3193, protobuf.get("top_level_fields"))
    add_check("protobuf_c2gs_fields", 311, protobuf.get("c2gs_fields"))
    add_check("protobuf_gs2c_fields", 349, protobuf.get("gs2c_fields"))
    mismatches = [item for item in integrity if item.get("status") != "MATCH"]
    add_check("assets_manifest_match", 0, len(mismatches), status="INFO")
    add_check("assets_manifest_checked", 14, len(integrity))

    facts = {
        "tool": {"name": "apk_probe.py", "version": TOOL_VERSION},
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "input": input_info,
        "android_manifest": android_manifest,
        "game_name": game_name,
        "version_manifest": version_manifest,
        "assets_manifest": {
            "version": assets_manifest.get("version"),
            "build": assets_manifest.get("build"),
            "operator": assets_manifest.get("operator"),
            "engineVersion": assets_manifest.get("engineVersion"),
            "packageUrl": assets_manifest.get("packageUrl"),
            "asset_count": len(assets_manifest.get("assets", {})),
            "asset_names": sorted(assets_manifest.get("assets", {}).keys()),
        },
        "channel": channel,
        "nested_archives": nested,
        "manifest_integrity": {
            "checked": len(integrity),
            "mismatches": mismatches,
            "items": integrity,
        },
        "mu": mu,
        "protobuf": protobuf,
        "checks": checks,
    }

    entry_rows = []
    for info in entries:
        if info.is_dir():
            continue
        entry_rows.append(
            {
                "path": info.filename,
                "size": info.file_size,
                "compressed_size": info.compress_size,
                "method": info.compress_type,
                "crc32": f"{info.CRC:08x}",
                "flags": info.flag_bits,
            }
        )
    _write_csv(
        out_dir / "apk_entries.csv",
        entry_rows,
        ["path", "size", "compressed_size", "method", "crc32", "flags"],
    )

    archive_rows = []
    for name, info in nested.items():
        archive_rows.append(
            {
                "archive": name,
                "entries": info["entries"],
                "files": info["files"],
                "dirs": info["dirs"],
                "size": info["size"],
                "total_uncompressed": info["total_uncompressed"],
                "sha256": info["sha256"],
                "zip_test": info["zip_test"],
            }
        )
    _write_csv(
        out_dir / "nested_archives.csv",
        archive_rows,
        ["archive", "entries", "files", "dirs", "size", "total_uncompressed", "sha256", "zip_test"],
    )

    checks_path = out_dir / "checks.csv"
    _write_csv(
        checks_path,
        checks,
        ["name", "expected", "actual", "status"],
    )
    _write_json(out_dir / "apk_facts.json", facts)

    # Stable, simple checksum list for generated evidence files.
    evidence_files = [
        out_dir / "apk_entries.csv",
        out_dir / "nested_archives.csv",
        out_dir / "checks.csv",
        out_dir / "apk_facts.json",
    ]
    sums = []
    for path in evidence_files:
        sums.append(f"{_sha256_bytes(path.read_bytes())}  {path.name}")
    (out_dir / "sha256sums.txt").write_text(
        "\n".join(sums) + "\n", encoding="utf-8", newline="\n"
    )

    return facts


def print_summary(facts: dict[str, Any], out_dir: Path) -> None:
    checks = facts["checks"]
    passed = sum(c["status"] == "PASS" for c in checks)
    failed = sum(c["status"] == "FAIL" for c in checks)
    info = sum(c["status"] == "INFO" for c in checks)
    print("APK_PROBE P0")
    print(f"  apk_sha256: {facts['input']['sha256']}")
    print(f"  zip_entries: {facts['input']['zip_entries']}")
    print(f"  package: {facts['android_manifest']['package']}")
    print(f"  version: {facts['android_manifest']['versionName']} ({facts['android_manifest']['versionCode']})")
    print(f"  game_name_utf8: {bool(facts['game_name']['text'])} @ resources.arsc:{facts['game_name']['utf8_offset']}")
    print(f"  ui.csb: {facts['nested_archives']['assets/ui.zip']['csb_count']}")
    print(f"  src.lua: {facts['mu']['src']['lua_decrypted_utf8']}/{facts['mu']['src']['lua_count']}")
    print(f"  data.csv: {facts['mu']['data']['csv_decrypted_parsed']}/{facts['mu']['data']['csv_count']}")
    print(f"  proto: {facts['protobuf'].get('files')} files, {facts['protobuf'].get('top_level_messages')} messages")
    print(f"  checks: {passed} PASS, {failed} FAIL, {info} INFO")
    print(f"  output: {out_dir}")
    if failed:
        for check in checks:
            if check["status"] == "FAIL":
                print(f"  FAIL {check['name']}: expected={check['expected']!r} actual={check['actual']!r}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Read-only P0 facts probe for the APK.")
    parser.add_argument("--apk", required=True, type=Path, help="path to base(2).apk.1")
    parser.add_argument(
        "--out",
        type=Path,
        default=Path("build/probe"),
        help="output directory (default: build/probe)",
    )
    args = parser.parse_args(argv)
    if not args.apk.is_file():
        print(f"APK not found: {args.apk}", file=sys.stderr)
        return 2
    try:
        facts = probe(args.apk, args.out)
    except Exception as exc:  # noqa: BLE001 - clear failure for a probe
        print(f"PROBE FAILED: {type(exc).__name__}: {exc}", file=sys.stderr)
        return 2
    print_summary(facts, args.out)
    return 1 if any(c["status"] == "FAIL" for c in facts["checks"]) else 0


if __name__ == "__main__":
    raise SystemExit(main())
