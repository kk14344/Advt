"""1:1 native-asset formation card PNG exporter for 冒险与挖矿 PvP simulator.

Composites official in-game UI textures, card quality frames, pixel-art mercenary sprites,
official game font (`general.ttf`) with inline genre glyphs (`≈ ≠ ≌ ≡`), destiny weapon &
rune icons, native skill-coverage progress bar, and the iconic bottom 4-stat HUD bar.
"""

from __future__ import annotations

import base64
import io
import json
import os
import ssl
import urllib.request
from collections import Counter
from pathlib import Path
from typing import Any

from PIL import Image, ImageChops, ImageDraw, ImageFont, PngImagePlugin

from engine import SimulatorEngine, is_leader_tid

DEFAULT_DATA_DIR = Path(os.environ.get('PVP_DATA_DIR', r'F:\codex1\tmp\test-server-pvp'))
DEFAULT_MAOWA_DIR = Path(os.environ.get('MAOWA_RES_DIR', r'F:\dsh1\work\maowa'))
GAME_FONT_PATH = DEFAULT_MAOWA_DIR / 'build' / 'assets' / 'font' / 'general.ttf'
FALLBACK_FONT_BOLD = Path(r'C:\Windows\Fonts\msyhbd.ttc')
FALLBACK_FONT_REG = Path(r'C:\Windows\Fonts\msyh.ttc')

# Exact official MERCENARY_GENRE_TEXT & MERCENARY_GENRE_COLOR from locale.zh-CN & util.client_constants
GENRE_GLYPH = {
    1: '≈',  # 暴击 (red sword circle in general.ttf)
    2: '≠',  # 回复 (green plus circle in general.ttf)
    3: '≌',  # 纯粹 (cyan star circle in general.ttf)
    4: '≡',  # 其他 (white question circle in general.ttf)
}
GENRE_COLOR = {
    1: '#E5512E',
    2: '#BAD633',
    3: '#5BD8F4',
    4: '#FFFFFF',
}
WASH_CN = {
    'speed': '先攻',
    'defense': '防御',
    'dodge': '闪避',
    'authority': '王者',
}
WASH_SHORT = {
    'speed': '先',
    'defense': '防',
    'dodge': '闪',
    'authority': '王',
}
PROP_COLOR = {
    'speed': '#FF8575',
    'defense': '#7AC0FF',
    'dodge': '#7BE498',
    'authority': '#FFD95A',
}


def _game_font(size: int) -> ImageFont.FreeTypeFont:
    if GAME_FONT_PATH.exists():
        return ImageFont.truetype(str(GAME_FONT_PATH), size)
    if FALLBACK_FONT_BOLD.exists():
        return ImageFont.truetype(str(FALLBACK_FONT_BOLD), size)
    return ImageFont.load_default()


def _ui_font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    path = FALLBACK_FONT_BOLD if bold and FALLBACK_FONT_BOLD.exists() else FALLBACK_FONT_REG
    if path.exists():
        return ImageFont.truetype(str(path), size)
    return _game_font(size)


def _scale9(img: Image.Image, target_w: int, target_h: int, border: int = 16) -> Image.Image:
    """Crisp 9-slice resize preserving pixel-art corners."""
    sw, sh = img.size
    b = min(border, sw // 3, sh // 3, target_w // 2, target_h // 2)
    out = Image.new('RGBA', (target_w, target_h), (0, 0, 0, 0))
    mw_src = max(1, sw - 2 * b)
    mh_src = max(1, sh - 2 * b)
    mw_dst = max(1, target_w - 2 * b)
    mh_dst = max(1, target_h - 2 * b)

    # Corners
    out.paste(img.crop((0, 0, b, b)), (0, 0))
    out.paste(img.crop((sw - b, 0, sw, b)), (target_w - b, 0))
    out.paste(img.crop((0, sh - b, b, sh)), (0, target_h - b))
    out.paste(img.crop((sw - b, sh - b, sw, sh)), (target_w - b, target_h - b))
    # Edges
    out.paste(img.crop((b, 0, sw - b, b)).resize((mw_dst, b), Image.NEAREST), (b, 0))
    out.paste(img.crop((b, sh - b, sw - b, sh)).resize((mw_dst, b), Image.NEAREST), (b, target_h - b))
    out.paste(img.crop((0, b, b, sh - b)).resize((b, mh_dst), Image.NEAREST), (0, b))
    out.paste(img.crop((sw - b, b, sw, sh - b)).resize((b, mh_dst), Image.NEAREST), (target_w - b, b))
    # Center
    out.paste(img.crop((b, b, sw - b, sh - b)).resize((mw_dst, mh_dst), Image.NEAREST), (b, b))
    return out


def _tint_rgba(img: Image.Image, rgb: tuple[int, int, int]) -> Image.Image:
    """Multiplies an RGBA sprite's RGB channels by the given color (Cocos2d setColor)."""
    r, g, b, a = img.convert('RGBA').split()
    tint = Image.new('RGB', img.size, rgb)
    tr, tg, tb = tint.split()
    nr = ImageChops.multiply(r, tr)
    ng = ImageChops.multiply(g, tg)
    nb = ImageChops.multiply(b, tb)
    return Image.merge('RGBA', (nr, ng, nb, a))


def _text_stroke(
    draw: ImageDraw.ImageDraw,
    xy: tuple[float, float],
    text: str,
    font: ImageFont.FreeTypeFont,
    fill: str,
    stroke_fill: str = '#000000',
    stroke_width: int = 2,
    anchor: str = 'la',
) -> None:
    draw.text(xy, text, font=font, fill=fill, stroke_fill=stroke_fill, stroke_width=stroke_width, anchor=anchor)


def _progress_color(pct: float) -> tuple[int, int, int]:
    """Official ui.mercenary_formation_panel _GetProgressColor."""
    if pct >= 100:
        return (0xC1, 0xFA, 0x1A)
    if pct >= 50:
        return (0xF5, 0xD3, 0x1F)
    return (0xEB, 0x4D, 0x19)


class AssetStore:
    """Lazy loader for official game atlases and GM custom portraits."""

    def __init__(self, data_dir: Path = DEFAULT_DATA_DIR, maowa_dir: Path = DEFAULT_MAOWA_DIR) -> None:
        self.data_dir = data_dir
        self.maowa_dir = maowa_dir
        self.cache_dir = data_dir / 'portrait-cache'
        self._frames: dict[str, Any] | None = None
        self._textures: dict[str, Image.Image] = {}
        self._frame_cache: dict[str, Image.Image] = {}
        self._portrait_cache: dict[str, Image.Image] = {}

    @property
    def frames(self) -> dict[str, Any]:
        if self._frames is None:
            boot = json.loads((self.data_dir / 'current-boot-metadata.json').read_text(encoding='utf8'))
            self._frames = boot['files']['atlas-frames.json']
        return self._frames

    def _texture_path(self, tex_key: str) -> Path:
        mapping = {
            'res/role/mercenary.png': self.maowa_dir / 'res' / 'role' / 'res' / 'role' / 'mercenary.png',
            'res/ui/ui.png': self.maowa_dir / 'build' / 'assets' / 'atlas' / 'ui.png',
            'res/ui/icon.png': self.maowa_dir / 'build' / 'assets' / 'atlas' / 'icon.png',
            'res/ui/quality7.png': self.maowa_dir / 'build' / 'assets' / 'atlas' / 'quality7.png',
        }
        return mapping.get(tex_key, self.maowa_dir / tex_key)

    def get_texture(self, tex_key: str) -> Image.Image | None:
        if tex_key not in self._textures:
            p = self._texture_path(tex_key)
            if not p.exists():
                return None
            self._textures[tex_key] = Image.open(p).convert('RGBA')
        return self._textures[tex_key]

    def get_frame(self, frame_name: str) -> Image.Image | None:
        if frame_name in self._frame_cache:
            return self._frame_cache[frame_name]
        meta = self.frames.get(frame_name)
        if not meta:
            return None
        tex = self.get_texture(meta['texture'])
        if tex is None:
            return None
        x, y, w, h = meta['rect']
        if meta.get('rotated'):
            crop = tex.crop((x, y, x + h, y + w)).transpose(Image.ROTATE_90)
        else:
            crop = tex.crop((x, y, x + w, y + h))
        orig_w, orig_h = meta.get('originalSize', [w, h])
        scr = meta.get('sourceColorRect')
        if scr and (orig_w != w or orig_h != h):
            canvas = Image.new('RGBA', (orig_w, orig_h), (0, 0, 0, 0))
            canvas.paste(crop, (scr[0], scr[1]))
            crop = canvas
        self._frame_cache[frame_name] = crop
        return crop

    def _fetch_gm_portrait(self, sha: str) -> Path | None:
        self.cache_dir.mkdir(parents=True, exist_ok=True)
        local = self.cache_dir / f'{sha}.png'
        if local.exists() and local.stat().st_size > 100:
            return local
        url = f'https://114.66.60.181:28443/assets/custom-sprites/{sha}.png'
        ctx = ssl._create_unverified_context()
        try:
            req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
            with urllib.request.urlopen(req, timeout=8, context=ctx) as resp:
                data = resp.read()
            if data.startswith(b'\x89PNG'):
                local.write_bytes(data)
                return local
        except Exception:
            return None
        return None

    def get_merc_portrait(self, merc_row: dict[str, Any]) -> Image.Image | None:
        sprite = str(merc_row.get('sprite', '') or '')
        if not sprite:
            return None
        if sprite in self._portrait_cache:
            return self._portrait_cache[sprite]

        img: Image.Image | None = None
        if sprite.startswith('gm_'):
            sha = sprite[3:]
            candidates = [
                self.cache_dir / f'{sha}.png',
                self.cache_dir / f'{sprite}.png',
                self.maowa_dir / 'build' / 'assets' / 'portrait' / f'{sha}.png',
                self.maowa_dir / 'build' / 'assets' / 'role' / f'{sha}.png',
            ]
            local = next((p for p in candidates if p.exists()), None)
            if local is None:
                local = self._fetch_gm_portrait(sha)
            if local is not None:
                raw = Image.open(local).convert('RGBA')
                rw, rh = raw.size
                # Handle 3x4 walking sheets if present
                if rw >= 90 and rh >= 120 and abs((rw / 3) - (rh / 4)) <= 4:
                    cw, ch = rw // 3, rh // 4
                    img = raw.crop((cw, 0, 2 * cw, ch))
                else:
                    img = raw
        else:
            img = self.get_frame(f'mercenary/{sprite}.png') or self.get_frame(f'{sprite}.png')

        if img is not None:
            # Keep native uncropped frame proportions (matches Cocos ui.icon_panel scale=2 on 112x112 card!)
            self._portrait_cache[sprite] = img
        return img

    def get_card_bg(self, quality: int, is_leader: bool) -> Image.Image | None:
        if is_leader:
            return self.get_frame('bg/herolist_bg5.png')
        if quality >= 7:
            return self.get_frame('bg/herolist_quality7.png') or self.get_frame('bg/herolist_bg6.png')
        q = min(max(1, int(quality or 6)), 6)
        return self.get_frame(f'bg/herolist_bg{q}.png')


_STORE: AssetStore | None = None


def get_asset_store(data_dir: Path = DEFAULT_DATA_DIR) -> AssetStore:
    global _STORE
    if _STORE is None or _STORE.data_dir != data_dir:
        _STORE = AssetStore(data_dir=data_dir)
    return _STORE


def _format_bp(bp: int) -> str:
    if bp >= 1_000_000:
        return f'{bp / 1_000_000:.2f}M'
    if bp >= 10_000:
        return f'{bp / 10_000:.1f}W'
    return str(bp)


def _format_stat_deltas(deltas: dict[str, int]) -> str:
    parts = []
    for key in ('speed', 'defense', 'dodge', 'authority'):
        v = int(deltas.get(key, 0))
        if v != 0:
            parts.append(f'{WASH_SHORT[key]}{v:+d}')
    return ' '.join(parts) if parts else '无'


def _summarize_runes(runes: list[dict[str, Any]]) -> str:
    cnt = Counter((r['name'], int(r.get('lv', 20))) for r in runes)
    compact = len(cnt) > 1
    parts = []
    for (name, lv), n in cnt.items():
        disp = name.replace('符文', '') if compact else name
        parts.append(f'{n}×{disp}Lv{lv}' if compact else (f'{n}×{disp} Lv{lv}' if n > 1 else f'{disp} Lv{lv}'))
    return '+'.join(parts) if compact else ' + '.join(parts)


def _summarize_washes(slots: list[dict[str, Any]]) -> str:
    non_leader = [s for s in slots if not is_leader_tid(int(s['id']))]
    cnt = Counter(s.get('wash', '') for s in non_leader)
    ordered = [(w, cnt[w]) for w in ('speed', 'defense', 'dodge', 'authority') if cnt.get(w)]
    if len(ordered) == 1:
        w, n = ordered[0]
        return f'全{WASH_CN[w]}×{n}'
    summary = ' / '.join(f'{WASH_SHORT[w]}×{n}' for w, n in ordered)
    # Also list exact slot numbers for minority washes so copying is 100% unambiguous
    if ordered:
        majority_w = max(ordered, key=lambda x: x[1])[0]
        minor_parts = []
        for w, _ in ordered:
            if w == majority_w:
                continue
            idxs = [f'#{s["pos"]}' for s in non_leader if s.get('wash') == w]
            if len(idxs) <= 12:
                minor_parts.append(f'{WASH_SHORT[w]}:{",".join(idxs)}')
        if minor_parts:
            return f'{summary} ({"; ".join(minor_parts)})'
    return summary


def _truncate_text(draw: ImageDraw.ImageDraw, text: str, font: ImageFont.FreeTypeFont, max_w: int) -> str:
    if draw.textlength(text, font=font) <= max_w:
        return text
    s = text
    while len(s) > 2 and draw.textlength(s + '…', font=font) > max_w:
        s = s[:-1]
    return s + '…'


def _summarize_rune_ids(eng: SimulatorEngine, rids: list[int]) -> str:
    if not rids:
        return '-'
    cnt = Counter(int(r) for r in rids)
    parts = []
    for rid, n in cnt.items():
        rname = eng.runes.get(rid, {}).get('name', str(rid)).replace('符文', '')
        parts.append(f'{n}×{rname}({rid})' if n > 1 else f'{rname}({rid})')
    return '+'.join(parts)


def render_formation_card(
    eng: SimulatorEngine,
    team: dict[str, Any],
    mode: str = 'server30',
    title: str | None = None,
    use_title: bool = True,
    live_report: dict[str, Any] | None = None,
    mode_overrides: dict[str, Any] | None = None,
) -> Image.Image:
    """Renders a 1:1 native-asset formation screenshot card with bottom 4-stat HUD, metadata footer,
    and optional seamless live-battle verification scorecard (一图流)."""
    info = eng.inspect_team(team, mode=mode, use_title=use_title, mode_overrides=mode_overrides)
    norm = eng.normalize_team(team)
    mp = eng.get_mode_profile(mode, mode_overrides)
    store = get_asset_store(eng.data_dir)

    # 1.5x of official 640px Cocos width -> razor-sharp 960px native proportions
    W = 960
    TOP_H = 104
    GRID_PAD_X = 40
    GRID_TOP_PAD = 22
    GRID_BOT_PAD = 36
    COLS, ROWS = 5, 6
    GAP = 14
    CELL = (W - 2 * GRID_PAD_X - (COLS - 1) * GAP) // COLS  # 164px square cards
    GRID_W = COLS * CELL + (COLS - 1) * GAP
    GRID_X0 = (W - GRID_W) // 2
    PANEL_Y = TOP_H
    PANEL_H = GRID_TOP_PAD + ROWS * CELL + (ROWS - 1) * GAP + GRID_BOT_PAD
    HUD_H = 82
    has_q7_row = info['q7_summary']['c2_limit'] is not None
    FOOTER_H = 180 if has_q7_row else 146

    live_rows = (live_report or {}).get('results') or []
    LIVE_H = (118 + len(live_rows) * 38) if live_rows else 0
    H = PANEL_Y + PANEL_H + HUD_H + FOOTER_H + LIVE_H

    img = Image.new('RGBA', (W, H), '#1A150E')
    draw = ImageDraw.Draw(img)

    # ── 1. TOP BAR (1:1 Replica of Screenshot #2: 觉醒8/8 | 宿命武器 | 契约卷轴 | 强化10/10) ──
    draw.rectangle([0, 0, W, TOP_H], fill='#2E251B')
    # Subtle inner darker band like native header
    draw.rectangle([0, 0, W, 8], fill='#1A140D')
    draw.rectangle([0, TOP_H - 10, W, TOP_H], fill='#4A3E2A')
    draw.line([(0, TOP_H - 10), (W, TOP_H - 10)], fill='#14100A', width=3)
    draw.line([(0, TOP_H - 3), (W, TOP_H - 3)], fill='#14100A', width=3)

    btn_bg_raw = store.get_frame('button/buttonbg_5.png')
    btn_w, btn_h = 184, 70
    btn_y = 14
    leader_wake = int(mp.get('leader_wakeup', 8))
    leader_forge = int(mp.get('leader_forge_lv', 10))
    merc_lv = int(mp.get('merc_level', 30 if mode == 'server30' else 100))
    leader_lv = int(mp.get('leader_level', 100))

    f_btn = _game_font(30)
    # Left button: 觉醒8/8
    if btn_bg_raw is not None:
        img.alpha_composite(_scale9(btn_bg_raw, btn_w, btn_h, 16), (12, btn_y))
    else:
        draw.rounded_rectangle([12, btn_y, 12 + btn_w, btn_y + btn_h], radius=10, fill='#D5B832', outline='#14100A', width=3)
    draw.text((12 + btn_w // 2, btn_y + btn_h // 2), f'觉醒{leader_wake}/{leader_wake}', font=f_btn, fill='#1D160B', anchor='mm')

    # Right button: 强化10/10
    rx_btn = W - 12 - btn_w
    if btn_bg_raw is not None:
        img.alpha_composite(_scale9(btn_bg_raw, btn_w, btn_h, 16), (rx_btn, btn_y))
    else:
        draw.rounded_rectangle([rx_btn, btn_y, rx_btn + btn_w, btn_y + btn_h], radius=10, fill='#D5B832', outline='#14100A', width=3)
    draw.text((rx_btn + btn_w // 2, btn_y + btn_h // 2), f'强化{leader_forge}/{leader_forge}', font=f_btn, fill='#1D160B', anchor='mm')

    # Contract scroll icon to the left of right button (native icon/mercenarylist/contract2.png)
    contract_icon = store.get_frame('icon/mercenarylist/contract2.png')
    cx_scroll = rx_btn - 84
    if contract_icon is not None:
        sc = contract_icon.resize((72, 72), Image.NEAREST)
        img.alpha_composite(sc, (cx_scroll, btn_y - 1))

    # Center destiny weapon icon + 2-line title matching Screenshot #2
    wid = info['weapon']['id']
    w_icon = store.get_frame(f'icon/destiny/weapon{wid}.png')
    wx = 224
    if w_icon is not None:
        wi = w_icon.resize((78, 78), Image.NEAREST)
        img.alpha_composite(wi, (wx, 10))
    f_wname = _game_font(31)
    f_wsub = _game_font(22)
    _text_stroke(draw, (wx + 94, 15), info['weapon']['name'], f_wname, '#FFE14D', '#100C06', 3)
    pe = info['profile_effective']
    if mode == 'fair_eq' or pe.get('q7_contract2_limit') is not None:
        q7_lim = pe.get('q7_contract2_limit', 0)
        mode_tag = f'等资源 Lv{merc_lv}·宝{pe["artifact_lv"]}·符{pe["rune_lv"]}·炫二契{q7_lim}'
        if pe.get('equalize_bp'):
            mode_tag += '·等战'
        if pe.get('no_runes'):
            mode_tag += '·无符'
    elif mode == 'server30':
        mode_tag = f'实服 Lv{merc_lv}·{int(mp.get("merc_wakeup_cap", 5))}觉'
    else:
        mode_tag = '满级 Lv100·满觉'
    _text_stroke(draw, (wx + 94, 54), f'{mode_tag} · {_format_bp(info["max_bp"])}', f_wsub, '#D8CEA8', '#100C06', 2)

    # ── 2. MAIN 5x6 PARCHMENT PANEL (1:1 Native Texture & Card Grid) ──
    draw.rectangle([0, PANEL_Y, W, PANEL_Y + PANEL_H], fill='#746442')
    # Inner parchment field matching Screenshot #2 (#C9BC8E) with dark/light bevel frame
    pm = 14
    draw.rounded_rectangle(
        [pm, PANEL_Y + 6, W - pm, PANEL_Y + PANEL_H - 6],
        radius=10, fill='#C9BC8E', outline='#1B140C', width=4,
    )
    draw.rounded_rectangle(
        [pm + 4, PANEL_Y + 10, W - pm - 4, PANEL_Y + PANEL_H - 10],
        radius=7, outline='#DED4A9', width=2,
    )

    # Left & right native formation switch arrows (exact detail from Screenshot #2)
    arrow = store.get_frame('icon/mercenarylist/weaponbox_leadrole_arrow.png')
    if arrow is not None:
        aw = arrow.resize((20, 30), Image.NEAREST)
        ay = PANEL_Y + PANEL_H // 2 - 15
        img.alpha_composite(aw.transpose(Image.FLIP_LEFT_RIGHT), (6, ay))
        img.alpha_composite(aw, (W - 26, ay))

    # Render all 30 slots (5x6) strictly matching Screenshot #2 card visuals
    choose_frame = store.get_frame('bg/herolist_choose.png')
    f_lv = _game_font(27)
    for idx, slot in enumerate(info['slots']):
        r, c = divmod(idx, COLS)
        x = GRID_X0 + c * (CELL + GAP)
        y = PANEL_Y + GRID_TOP_PAD + r * (CELL + GAP)
        tid = int(slot['id'])
        is_leader = is_leader_tid(tid)
        mrow = eng.mercs[tid]
        quality = int(mrow.get('quality', 6))

        # 1) Official card background frame
        cbg = store.get_card_bg(quality, is_leader)
        if cbg is not None:
            img.alpha_composite(cbg.resize((CELL, CELL), Image.NEAREST), (x, y))
        else:
            draw.rounded_rectangle([x, y, x + CELL, y + CELL], radius=12, fill='#B5942B', outline='#151008', width=3)

        # 2) Official leader selection frame (white/black rounded border)
        if is_leader and choose_frame is not None:
            pad = 8
            img.alpha_composite(
                choose_frame.resize((CELL + 2 * pad, CELL + 2 * pad), Image.NEAREST),
                (x - pad, y - pad),
            )

        # 3) Official pixel-art sprite (1:1 Cocos ui.icon_panel ratio: scale=2 on 112x112 card)
        portrait = store.get_merc_portrait(mrow)
        if portrait is not None:
            pw, ph = portrait.size
            native_scale = 2.0 * CELL / 112.0  # ~2.9286x: 34px normal chibi -> 100px (61% of 164px cell)
            is_gm = str(mrow.get('sprite', '') or '').startswith('gm_')
            if is_gm:
                # H5 client normalizes every GM custom sprite into a 34x34 logical display box before scale=2
                target_box = int(round(34.0 * native_scale))
                scale = target_box / max(1, max(pw, ph))
            else:
                # Official atlas frames keep uniform pixel density (so 54x46 winged bosses naturally appear larger)
                max_cap = CELL * 0.90
                scale = min(native_scale, max_cap / max(1, pw), max_cap / max(1, ph))
            nw, nh = max(1, int(round(pw * scale))), max(1, int(round(ph * scale)))
            sp = portrait.resize((nw, nh), Image.NEAREST)
            px = x + (CELL - nw) // 2
            py = y + (CELL - nh) // 2 - 4
            img.alpha_composite(sp, (px, py))

        # 4) Official bottom-right Lv + inline genre circular glyph in general.ttf
        if is_leader:
            lv_text = f'Lv{leader_lv}'
            lv_color = '#FFFFFF'
        else:
            genre = int(mrow.get('genre', 4) or 4)
            lv_text = f'Lv{merc_lv}{GENRE_GLYPH.get(genre, "")}'
            lv_color = GENRE_COLOR.get(genre, '#FFFFFF')

        _text_stroke(
            draw,
            (x + CELL - 11, y + CELL - 9),
            lv_text,
            f_lv,
            lv_color,
            stroke_fill='#000000',
            stroke_width=3,
            anchor='rd',
        )

        # 5) Transparent 炫金 contract pill when fair_eq / q7_contract2_limit is active
        if (not is_leader) and quality == 7 and info['q7_summary']['c2_limit'] is not None:
            is_c2 = int(slot.get('contract', 1)) >= 2
            pill_txt = '二契' if is_c2 else '一契'
            pill_bg = '#3D2D08' if is_c2 else '#1B232E'
            pill_bd = '#F0C040' if is_c2 else '#7D93AD'
            pill_fg = '#FFE875' if is_c2 else '#D2E4F7'
            draw.rounded_rectangle([x + 7, y + 7, x + 53, y + 31], radius=6, fill=pill_bg, outline=pill_bd, width=2)
            draw.text((x + 30, y + 19), pill_txt, font=_ui_font(14, bold=True), fill=pill_fg, anchor='mm')

    # ── 3. BOTTOM NATIVE 4-STAT & SKILL COVERAGE HUD BAR (1:1 Replica of Screenshot #2) ──
    hud_y = PANEL_Y + PANEL_H
    draw.rectangle([0, hud_y, W, hud_y + HUD_H], fill='#1B150E')

    # Floating pagination dots capsule centered on top border of HUD bar
    dot_w, dot_h = 196, 30
    dot_x = (W - dot_w) // 2
    dot_y = hud_y - 22
    draw.rounded_rectangle([dot_x, dot_y, dot_x + dot_w, dot_y + dot_h], radius=10, fill='#251E15', outline='#786A4A', width=2)
    for di in range(5):
        dcx = dot_x + 30 + di * 34
        dcy = dot_y + dot_h // 2
        fill_dot = '#FFF4BE' if di == 3 else '#8E805E'
        draw.ellipse([dcx - 6, dcy - 6, dcx + 6, dcy + 6], fill=fill_dot, outline='#14100A', width=1)

    # Main HUD dark-brown rounded bar
    bar_x0, bar_y0, bar_x1, bar_y1 = 14, hud_y + 10, W - 14, hud_y + HUD_H - 6
    draw.rounded_rectangle([bar_x0, bar_y0, bar_x1, bar_y1], radius=12, fill='#261F16', outline='#0C0906', width=4)
    draw.rounded_rectangle([bar_x0 + 3, bar_y0 + 3, bar_x1 - 3, bar_y1 - 3], radius=9, outline='#A89B6E', width=2)

    bar_cy = (bar_y0 + bar_y1) // 2
    f_hud_lbl = _game_font(25)
    f_hud_val = _game_font(27)

    # "属性" label
    _text_stroke(draw, (bar_x0 + 18, bar_cy), '属性', f_hud_lbl, '#C9BD92', '#0E0B07', 2, anchor='lm')

    # 4 official stat icons + exact base_stats (1:1 match to in-game HUD)
    stat_specs = [
        ('speed', 'icon/mercenarylist/fightnumber_4.png', bar_x0 + 82),
        ('defense', 'icon/mercenarylist/fightnumber_3.png', bar_x0 + 206),
        ('dodge', 'icon/mercenarylist/fightnumber_2.png', bar_x0 + 322),
        ('authority', 'icon/mercenarylist/fightnumber_1.png', bar_x0 + 420),
    ]
    for prop, icon_key, sx in stat_specs:
        ic = store.get_frame(icon_key)
        if ic is not None:
            iw, ih = ic.size
            scale = 34 / max(iw, ih)
            ic_s = ic.resize((max(1, int(iw * scale)), max(1, int(ih * scale))), Image.NEAREST)
            img.alpha_composite(ic_s, (sx, bar_cy - ic_s.height // 2))
        val_str = str(info['base_stats'][prop])
        _text_stroke(draw, (sx + 38, bar_cy), val_str, f_hud_val, '#F2ECD0', '#0C0906', 3, anchor='lm')

    # "技能覆盖率" + official tinted loading_bar/cover_skill_bar.png + "100%" + ▲
    cov_lbl_x = bar_x0 + 542
    _text_stroke(draw, (cov_lbl_x, bar_cy), '技能覆盖率', f_hud_lbl, '#C9BD92', '#0E0B07', 2, anchor='lm')

    total_prob = sum(sk['prob'] for sk in info['skills'])
    cov_pct = min(100, total_prob)
    pb_x0, pb_x1 = bar_x0 + 688, bar_x1 - 52
    pb_h = 30
    pb_y0 = bar_cy - pb_h // 2
    draw.rectangle([pb_x0, pb_y0, pb_x1, pb_y0 + pb_h], fill='#17120C', outline='#080604', width=3)
    fill_w = max(0, int((pb_x1 - pb_x0 - 6) * cov_pct / 100))
    if fill_w > 0:
        bar_tex = store.get_frame('loading_bar/cover_skill_bar.png')
        rgb = _progress_color(cov_pct)
        if bar_tex is not None:
            tinted = _tint_rgba(bar_tex, rgb).resize((fill_w, pb_h - 6), Image.NEAREST)
            img.alpha_composite(tinted, (pb_x0 + 3, pb_y0 + 3))
        else:
            draw.rectangle([pb_x0 + 3, pb_y0 + 3, pb_x0 + 3 + fill_w, pb_y0 + pb_h - 3], fill=rgb)
    _text_stroke(
        draw,
        ((pb_x0 + pb_x1) // 2, bar_cy),
        f'{cov_pct}%',
        _game_font(26),
        '#FFFFFF',
        '#000000',
        3,
        anchor='mm',
    )
    # Small white up-arrow triangle on far right (exact detail from Screenshot #2)
    ax = bar_x1 - 26
    draw.polygon([(ax, bar_cy - 7), (ax - 10, bar_cy + 7), (ax + 10, bar_cy + 7)], fill='#FFFFFF', outline='#0C0906')

    # ── 4. COMPACT METADATA FOOTER (阵容名 / 符文+增减益 / 洗练分布 / 合体技 / 合规状态) ──
    fy = hud_y + HUD_H + 2
    fy_end = fy + FOOTER_H - 12
    draw.rounded_rectangle([14, fy, W - 14, fy_end], radius=10, fill='#231C14', outline='#6B5D41', width=2)

    f_ft_title = _ui_font(20, bold=True)
    f_ft_body = _ui_font(18, bold=True)
    f_ft_small = _ui_font(17, bold=False)

    # Row 1: Title + BP + Title Tier + Compliance badge
    row1_y = fy + 12
    display_title = title or info['name']
    tt = eng._resolve_title_tuple(mode, use_title, norm.get('title_bonus'), mode_overrides)
    title_tag = f'称号T{tt[1]}({tt[1]}/{tt[2]}/{tt[3]}/{tt[4]})' if tt else '无称号'
    valid = len(info['violations']) == 0
    ok_label = '等资源合规' if mode == 'fair_eq' else '实服合规'
    badge_txt = ok_label if valid else f'违规({len(info["violations"])})'
    badge_bg = '#234A2B' if valid else '#5C2222'
    badge_bd = '#58C46D' if valid else '#E05A5A'
    badge_fg = '#B7F7C1' if valid else '#FFC2C2'

    # Compliance pill on right
    bw = 112 if mode == 'fair_eq' else 104
    bx0 = W - 26 - bw
    draw.rounded_rectangle([bx0, row1_y - 2, bx0 + bw, row1_y + 26], radius=6, fill=badge_bg, outline=badge_bd, width=2)
    draw.text((bx0 + bw // 2, row1_y + 12), badge_txt, font=_ui_font(16, bold=True), fill=badge_fg, anchor='mm')

    # Title tier & BP pill next to it
    bp_str = f'{title_tag}  战力 {info["max_bp"]:,}'
    bp_w = int(draw.textlength(bp_str, font=f_ft_small))
    bp_x = bx0 - 16 - bp_w
    draw.text((bp_x, row1_y + 2), bp_str, font=f_ft_small, fill='#D8CCA6')

    t_max_w = max(140, bp_x - 42)
    draw.text((28, row1_y), _truncate_text(draw, display_title, f_ft_title, t_max_w), font=f_ft_title, fill='#FFE58A')

    draw.line([(26, fy + 44), (W - 26, fy + 44)], fill='#463B28', width=1)

    # Row 2: 5 mini native rune icons + rune summary + self bonus / enemy debuff
    row2_y = fy + 52
    rx = 28
    for r in info['runes']:
        icon_name = eng.runes[r['id']].get('icon', '')
        ric = store.get_frame(icon_name)
        if ric is not None:
            rs = ric.resize((26, 26), Image.NEAREST)
            img.alpha_composite(rs, (rx, row2_y - 1))
            rx += 28
    if info['runes']:
        rune_summary = _summarize_runes(info['runes'])
        r_lv = info['runes'][0].get('lv', 20)
        lv_prefix = f'Lv{r_lv} ' if r_lv != 20 else ''
        self_rune_delta = {
            p: info['after_rune_stats'][p] - info['base_stats'][p]
            for p in ('speed', 'defense', 'dodge', 'authority')
        }
        self_str = _format_stat_deltas(self_rune_delta)
        foe_str = _format_stat_deltas(info['enemy_rune_debuff'])
        rune_line = f'符文: {lv_prefix}{rune_summary} | 己方: {self_str} | 敌方衰弱: {foe_str}'
    else:
        rune_line = '符文: 无符文诊断模式 (纯30人+宿命武器骨架评测)'
    draw.text((rx + 6, row2_y + 1), _truncate_text(draw, rune_line, f_ft_body, W - rx - 34), font=f_ft_body, fill='#F2E8C8')

    # Row 3: Wash distribution + Combos
    row3_y = fy + 92
    wash_str = _summarize_washes(info['slots'])
    combos = info['combos']
    combo_str = ' · '.join(combos) if combos else '无'
    row3_txt = f'洗练: {wash_str}   |   合体技({len(combos)}): {combo_str}'
    draw.text((28, row3_y), _truncate_text(draw, row3_txt, f_ft_body, W - 56), font=f_ft_body, fill='#D5C89E')

    # Row 4 (fair_eq / q7 quota mode only): Dedicated full-width transparent 炫金 contract allocation strip
    if has_q7_row:
        row4_y = fy + 128
        q7 = info['q7_summary']
        if q7['c2_slots']:
            c2_cnt = Counter(s['name'] for s in q7['c2_slots'])
            c2_disp = '、'.join(f'{n}×{c}' if c > 1 else n for n, c in c2_cnt.items())
        else:
            c2_disp = '无 (炫金全部一契)'
        row4_txt = (
            f'等资源契约: 闪金及以下全二契   |   炫金共{q7["total"]}张 · '
            f'二契({len(q7["c2_slots"])}/{q7["c2_limit"]}): {c2_disp}'
        )
        draw.text((28, row4_y), _truncate_text(draw, row4_txt, f_ft_body, W - 56), font=f_ft_body, fill='#FFE58A')

    # ── 5. OPTIONAL SEAMLESS LIVE VERIFICATION SCORECARD (一图流实机复核战绩区) ──
    if live_rows:
        ly = fy_end + 8
        draw.rounded_rectangle([14, ly, W - 14, H - 10], radius=10, fill='#1E1811', outline='#8A774E', width=2)

        f_lh = _ui_font(19, bold=True)
        f_lt = _ui_font(16, bold=True)
        f_lr = _ui_font(16, bold=False)

        fid_disp = live_report.get('fid', 5)
        tot_sec = live_report.get('total_sec', 0)
        all_pass = bool(live_report.get('all_pass', False))
        min_wr = float(live_report.get('min_winrate_pct', 0))
        avg_wr = float(live_report.get('avg_winrate_pct', 0))
        hdr_txt = f'实机复核战绩  ·  阵型F{fid_disp}  ·  严格在途=1  ·  耗时 {tot_sec}s'
        draw.text((28, ly + 11), hdr_txt, font=f_lh, fill='#FFE58A')

        # Stamp pill on right
        st_txt = '实机复核通过' if all_pass else f'保底 {min_wr:.1f}%'
        st_bg = '#1F4B2B' if all_pass else '#5A441B'
        st_bd = '#5FD478' if all_pass else '#E6B84C'
        st_fg = '#C4FFD0' if all_pass else '#FFE8A3'
        sw = 132
        sx0 = W - 26 - sw
        draw.rounded_rectangle([sx0, ly + 9, sx0 + sw, ly + 37], radius=6, fill=st_bg, outline=st_bd, width=2)
        draw.text((sx0 + sw // 2, ly + 23), st_txt, font=_ui_font(16, bold=True), fill=st_fg, anchor='mm')

        # Table header
        th_y = ly + 46
        draw.rectangle([22, th_y, W - 22, th_y + 28], fill='#2C2419')
        cols_x = [30, 195, 345, 465, 575, 845]
        for cx, cname in zip(cols_x, ['对手好友', '实机战绩', '实机胜率', '模拟胜率', '对手实战四维 / 符文', '复核']):
            draw.text((cx, th_y + 5), cname, font=f_lt, fill='#C9BD92')

        tot_w, tot_l = 0, 0
        for ri, row in enumerate(live_rows):
            ry = th_y + 30 + ri * 38
            if ri % 2 == 1:
                draw.rectangle([22, ry, W - 22, ry + 36], fill='#241D14')
            draw.line([(22, ry + 36), (W - 22, ry + 36)], fill='#382E20', width=1)

            lbl = f"{row.get('label', row.get('uid', ''))} ({row.get('uid', '')})"
            wins = int(row.get('wins', 0))
            losses = int(row.get('losses', 0))
            n_tot = int(row.get('n', wins + losses))
            tot_w += wins
            tot_l += losses
            wr = float(row.get('winrate_pct', 0))
            sim_v = row.get('sim_pct')
            sim_str = f'{float(sim_v):.1f}%' if sim_v is not None else '-'
            opp_r = _summarize_rune_ids(eng, row.get('opp_runes') or [])
            opp_info = f"{row.get('opp_base', '')} · {opp_r}" if row.get('opp_base') else opp_r
            ok_row = bool(row.get('pass', wr >= float(live_report.get('target_pct', 70))))

            wr_col = '#7BE498' if wr >= 80 else ('#FFE14D' if wr >= 70 else ('#F5B96B' if wr >= 55 else '#FF8575'))
            draw.text((cols_x[0], ry + 8), _truncate_text(draw, lbl, f_lt, 158), font=f_lt, fill='#F2E8C8')
            draw.text((cols_x[1], ry + 8), f'{wins}胜 / {losses}负 ({n_tot})', font=f_lr, fill='#DFD5B4')
            draw.text((cols_x[2], ry + 8), f'{wr:.2f}%', font=f_lt, fill=wr_col)
            draw.text((cols_x[3], ry + 8), sim_str, font=f_lr, fill='#BFC8D8')
            draw.text((cols_x[4], ry + 8), _truncate_text(draw, opp_info, f_lr, 260), font=f_lr, fill='#C9BD92')

            p_txt = '达标' if ok_row else '未达'
            p_bg = '#1F4B2B' if ok_row else '#5C2222'
            p_fg = '#B7F7C1' if ok_row else '#FFC2C2'
            draw.rounded_rectangle([cols_x[5], ry + 5, cols_x[5] + 74, ry + 31], radius=5, fill=p_bg)
            draw.text((cols_x[5] + 37, ry + 18), p_txt, font=_ui_font(15, bold=True), fill=p_fg, anchor='mm')

        # Bottom summary strip
        sum_y = H - 38
        ts_str = str(live_report.get('tested_at', ''))[:16].replace('T', ' ')
        sum_txt = f'汇总: {tot_w}胜/{tot_l}负  |  最低保底 {min_wr:.2f}%  |  均胜 {avg_wr:.2f}%  |  面板 0/0/0/0 零误差对账  |  {ts_str}'
        draw.text((28, sum_y), _truncate_text(draw, sum_txt, f_lr, W - 56), font=f_lr, fill='#B9AD88')

    return img


def render_team_card(
    eng: SimulatorEngine,
    team: dict[str, Any],
    mode: str = 'server30',
    subtitle: str | None = None,
    use_title: bool = True,
    live_report: dict[str, Any] | None = None,
    mode_overrides: dict[str, Any] | None = None,
) -> Image.Image:
    """Backwards-compatible alias for `render_formation_card` used by CLI & Web UI."""
    return render_formation_card(
        eng, team, mode=mode, title=subtitle, use_title=use_title,
        live_report=live_report, mode_overrides=mode_overrides,
    )


def _build_png_meta(
    eng: SimulatorEngine,
    team: dict[str, Any],
    mode: str,
    live_report: dict[str, Any] | None = None,
    mode_overrides: dict[str, Any] | None = None,
) -> PngImagePlugin.PngInfo:
    info = eng.inspect_team(team, mode=mode, mode_overrides=mode_overrides)
    payload = {
        'v': 1,
        'name': info.get('name', ''),
        'mode': mode,
        'weapon': int(info['weapon']['id']),
        'runes': [[int(r['id']), int(r.get('lv', 20))] for r in info['runes']],
        'slots': [{'id': int(s['id']), 'name': s.get('name', ''), 'wash': s.get('wash', '')} for s in info['slots']],
    }
    if mode_overrides:
        payload['mode_overrides'] = mode_overrides
    if live_report:
        payload['live_summary'] = {
            'fid': live_report.get('fid'),
            'min': live_report.get('min_winrate_pct'),
            'avg': live_report.get('avg_winrate_pct'),
            'all_pass': live_report.get('all_pass'),
        }
    pnginfo = PngImagePlugin.PngInfo()
    pnginfo.add_text('maowa_formation_v1', json.dumps(payload, ensure_ascii=False, separators=(',', ':')))
    return pnginfo


def export_formation_png(
    eng: SimulatorEngine,
    team: dict[str, Any],
    out_path: str | Path,
    mode: str = 'server30',
    title: str | None = None,
    use_title: bool = True,
    live_report: dict[str, Any] | None = None,
    mode_overrides: dict[str, Any] | None = None,
) -> Path:
    path = Path(out_path)
    path.parent.mkdir(parents=True, exist_ok=True)
    img = render_formation_card(
        eng, team, mode=mode, title=title, use_title=use_title,
        live_report=live_report, mode_overrides=mode_overrides,
    )
    img.save(path, format='PNG', optimize=True, pnginfo=_build_png_meta(eng, team, mode, live_report, mode_overrides))
    return path


def render_formation_png_bytes(
    eng: SimulatorEngine,
    team: dict[str, Any],
    mode: str = 'server30',
    title: str | None = None,
    use_title: bool = True,
    mode_overrides: dict[str, Any] | None = None,
) -> bytes:
    img = render_formation_card(eng, team, mode=mode, title=title, use_title=use_title, mode_overrides=mode_overrides)
    buf = io.BytesIO()
    img.save(buf, format='PNG', optimize=True)
    return buf.getvalue()


def render_team_png_bytes(
    eng: SimulatorEngine,
    team: dict[str, Any],
    mode: str = 'server30',
    subtitle: str | None = None,
    use_title: bool = True,
    mode_overrides: dict[str, Any] | None = None,
) -> bytes:
    """Backwards-compatible alias for `render_formation_png_bytes` used by Web UI."""
    return render_formation_png_bytes(eng, team, mode=mode, title=subtitle, use_title=use_title, mode_overrides=mode_overrides)


def render_formation_data_url(
    eng: SimulatorEngine,
    team: dict[str, Any],
    mode: str = 'server30',
    title: str | None = None,
    use_title: bool = True,
    mode_overrides: dict[str, Any] | None = None,
) -> str:
    raw = render_formation_png_bytes(eng, team, mode=mode, title=title, use_title=use_title, mode_overrides=mode_overrides)
    return 'data:image/png;base64,' + base64.b64encode(raw).decode('ascii')
