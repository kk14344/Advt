#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""XXTEA payload codec for the game's .mu containers.

Only local research use.  This module decrypts in memory; writing plaintext is
the responsibility of tools/mu_decrypt.py.
"""

from __future__ import annotations

import struct
from typing import Any

XXTEA_DELTA = 0x9E3779B9
XXTEA_MASK = 0xFFFFFFFF
MU_KEY = b"10cc4fdee2fcd047"
MU_SIGN = b"gclR3cu9"


class MuPayloadError(ValueError):
    """Raised when a .mu payload does not have the expected structure."""


def _mx(y: int, z: int, p: int, e: int, s: int, k: list[int]) -> int:
    return (
        (((z >> 5) ^ ((y << 2) & XXTEA_MASK)) + ((y >> 3) ^ ((z << 4) & XXTEA_MASK)))
        ^ ((s ^ y) + (k[(p & 3) ^ e] ^ z))
    ) & XXTEA_MASK


def xxtea_decrypt(data: bytes, key: bytes = MU_KEY) -> bytes:
    """Decrypt a raw standard XXTEA block payload (no length header)."""

    if len(key) != 16:
        raise ValueError("XXTEA key must be exactly 16 bytes")
    if len(data) % 4:
        raise ValueError("XXTEA ciphertext length is not a multiple of 4")
    n = len(data) // 4
    if n < 2:
        return data
    values = list(struct.unpack("<%dI" % n, data))
    key_words = list(struct.unpack("<4I", key))
    rounds = 6 + 52 // n
    total = (rounds * XXTEA_DELTA) & XXTEA_MASK
    y = values[0]
    while total:
        e = (total >> 2) & 3
        for p in range(n - 1, 0, -1):
            z = values[p - 1]
            values[p] = (values[p] - _mx(y, z, p, e, total, key_words)) & XXTEA_MASK
            y = values[p]
        z = values[n - 1]
        values[0] = (values[0] - _mx(y, z, 0, e, total, key_words)) & XXTEA_MASK
        y = values[0]
        total = (total - XXTEA_DELTA) & XXTEA_MASK
    return struct.pack("<%dI" % n, *values)


def decrypt_mu_payload(payload: bytes, signed: bool = True) -> tuple[bytes, dict[str, Any]]:
    """Decrypt one .mu member.

    Layout:
        signed:  b"gclR3cu9" + XXTEA(plain + zero padding + LE32(plain_len))
        unsigned:            XXTEA(plain + zero padding + LE32(plain_len))
    """

    meta: dict[str, Any] = {
        "cipher_len": len(payload),
        "signed": signed,
        "sign_ok": None,
        "padded_len": None,
        "plain_len": None,
        "padding": None,
    }
    if signed:
        meta["sign_ok"] = payload[: len(MU_SIGN)] == MU_SIGN
        if not meta["sign_ok"]:
            raise MuPayloadError("bad or missing gclR3cu9 signature")
        payload = payload[len(MU_SIGN) :]
    if len(payload) % 4:
        raise MuPayloadError("encrypted payload length is not a multiple of 4")
    decrypted = xxtea_decrypt(payload, MU_KEY)
    meta["padded_len"] = len(decrypted)
    if len(decrypted) < 4:
        raise MuPayloadError("decrypted payload too short")
    plain_len = struct.unpack("<I", decrypted[-4:])[0]
    if not (0 < plain_len <= len(decrypted) - 4):
        raise MuPayloadError(f"invalid embedded plaintext length: {plain_len}")
    padding = decrypted[plain_len : len(decrypted) - 4]
    meta["plain_len"] = plain_len
    meta["padding"] = len(padding)
    if any(padding):
        raise MuPayloadError("non-zero padding in decrypted payload")
    return decrypted[:plain_len], meta
