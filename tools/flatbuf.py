#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Minimal read-only FlatBuffers table reader used by csb_inspect.py.

This is intentionally small and schema-agnostic.  Callers know the field slots
from the Cocos Studio CSParseBinary schema.  It does not build buffers.
"""

from __future__ import annotations

import struct


class FlatBufferError(ValueError):
    pass


class Table:
    __slots__ = ("buf", "pos")

    def __init__(self, buf: bytes, pos: int):
        if pos < 0 or pos + 4 > len(buf):
            raise FlatBufferError(f"table position out of bounds: {pos}")
        self.buf = buf
        self.pos = pos

    def _vtable(self) -> tuple[int, int]:
        soffset = struct.unpack_from("<i", self.buf, self.pos)[0]
        vtable = self.pos - soffset
        if vtable < 0 or vtable + 4 > len(self.buf):
            raise FlatBufferError(f"vtable out of bounds: {vtable}")
        vtable_len = struct.unpack_from("<H", self.buf, vtable)[0]
        object_len = struct.unpack_from("<H", self.buf, vtable + 2)[0]
        if vtable_len < 4 or vtable + vtable_len > len(self.buf):
            raise FlatBufferError(f"invalid vtable length: {vtable_len}")
        if object_len < 4 or self.pos + object_len > len(self.buf):
            raise FlatBufferError(f"invalid object length: {object_len}")
        return vtable, vtable_len

    def field_pos(self, slot: int) -> int | None:
        vtable, vtable_len = self._vtable()
        entry = 4 + slot * 2
        if entry + 2 > vtable_len:
            return None
        offset = struct.unpack_from("<H", self.buf, vtable + entry)[0]
        if offset == 0:
            return None
        pos = self.pos + offset
        if pos >= len(self.buf):
            raise FlatBufferError(f"field {slot} out of bounds")
        return pos

    def _indirect(self, field_pos: int) -> int:
        if field_pos + 4 > len(self.buf):
            raise FlatBufferError("indirect offset out of bounds")
        target = field_pos + struct.unpack_from("<I", self.buf, field_pos)[0]
        if target >= len(self.buf):
            raise FlatBufferError(f"indirect target out of bounds: {target}")
        return target

    def string(self, slot: int) -> str | None:
        pos = self.field_pos(slot)
        if pos is None:
            return None
        target = self._indirect(pos)
        if target + 4 > len(self.buf):
            raise FlatBufferError("string length out of bounds")
        length = struct.unpack_from("<I", self.buf, target)[0]
        start = target + 4
        end = start + length
        if end > len(self.buf):
            raise FlatBufferError("string bytes out of bounds")
        return self.buf[start:end].decode("utf-8", errors="replace")

    def table(self, slot: int) -> "Table | None":
        pos = self.field_pos(slot)
        if pos is None:
            return None
        return Table(self.buf, self._indirect(pos))

    def vector_count(self, slot: int) -> int:
        pos = self.field_pos(slot)
        if pos is None:
            return 0
        target = self._indirect(pos)
        if target + 4 > len(self.buf):
            raise FlatBufferError("vector length out of bounds")
        count = struct.unpack_from("<I", self.buf, target)[0]
        if count > 1_000_000:
            raise FlatBufferError(f"unreasonable vector length: {count}")
        return count

    def vector_tables(self, slot: int) -> list["Table"]:
        pos = self.field_pos(slot)
        if pos is None:
            return []
        target = self._indirect(pos)
        if target + 4 > len(self.buf):
            raise FlatBufferError("vector length out of bounds")
        count = struct.unpack_from("<I", self.buf, target)[0]
        if count > 1_000_000:
            raise FlatBufferError(f"unreasonable vector length: {count}")
        start = target + 4
        if start + count * 4 > len(self.buf):
            raise FlatBufferError("table vector out of bounds")
        result = []
        for i in range(count):
            elem = start + i * 4
            result.append(Table(self.buf, self._indirect(elem)))
        return result

    def vector_strings(self, slot: int) -> list[str]:
        pos = self.field_pos(slot)
        if pos is None:
            return []
        target = self._indirect(pos)
        if target + 4 > len(self.buf):
            raise FlatBufferError("vector length out of bounds")
        count = struct.unpack_from("<I", self.buf, target)[0]
        if count > 1_000_000:
            raise FlatBufferError(f"unreasonable vector length: {count}")
        start = target + 4
        if start + count * 4 > len(self.buf):
            raise FlatBufferError("string vector out of bounds")
        result = []
        for i in range(count):
            elem = start + i * 4
            s_target = self._indirect(elem)
            if s_target + 4 > len(self.buf):
                raise FlatBufferError("string length out of bounds")
            length = struct.unpack_from("<I", self.buf, s_target)[0]
            s_start = s_target + 4
            s_end = s_start + length
            if s_end > len(self.buf):
                raise FlatBufferError("string bytes out of bounds")
            result.append(self.buf[s_start:s_end].decode("utf-8", errors="replace"))
        return result

    def u8(self, slot: int, default: int = 0) -> int:
        pos = self.field_pos(slot)
        return default if pos is None else self.buf[pos]

    def bool(self, slot: int, default: bool = False) -> bool:
        pos = self.field_pos(slot)
        return default if pos is None else bool(self.buf[pos])

    def i32(self, slot: int, default: int = 0) -> int:
        pos = self.field_pos(slot)
        if pos is None:
            return default
        if pos + 4 > len(self.buf):
            raise FlatBufferError("int32 out of bounds")
        return struct.unpack_from("<i", self.buf, pos)[0]

    def f32(self, slot: int, default: float = 0.0) -> float:
        pos = self.field_pos(slot)
        if pos is None:
            return default
        if pos + 4 > len(self.buf):
            raise FlatBufferError("float out of bounds")
        return struct.unpack_from("<f", self.buf, pos)[0]

    def pair_f32(self, slot: int) -> tuple[float, float] | None:
        pos = self.field_pos(slot)
        if pos is None:
            return None
        if pos + 8 > len(self.buf):
            raise FlatBufferError("pair of floats out of bounds")
        return struct.unpack_from("<ff", self.buf, pos)

    def quad_f32(self, slot: int) -> tuple[float, float, float, float] | None:
        pos = self.field_pos(slot)
        if pos is None:
            return None
        if pos + 16 > len(self.buf):
            raise FlatBufferError("quad of floats out of bounds")
        return struct.unpack_from("<ffff", self.buf, pos)

    def color(self, slot: int) -> dict[str, int] | None:
        pos = self.field_pos(slot)
        if pos is None:
            return None
        if pos + 4 > len(self.buf):
            raise FlatBufferError("color out of bounds")
        a, r, g, b = struct.unpack_from("<4B", self.buf, pos)
        return {"a": a, "r": r, "g": g, "b": b}


def root_table(buf: bytes) -> Table:
    if len(buf) < 4:
        raise FlatBufferError("buffer too short for root offset")
    pos = struct.unpack_from("<I", buf, 0)[0]
    return Table(buf, pos)
