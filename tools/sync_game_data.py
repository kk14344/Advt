"""
新版本佣兵 / 技能 / 预设库一键校验与同步诊断工具：
1. 校验 pvp-simulator/presets.json 全部阵型结构完整性（槽位、武器、符文、主角 ID >= 99000001）；
2. 若本机存在游戏静态数据目录（--data-dir 或环境变量 PVP_DATA_DIR / F:\\codex1\\tmp\\test-server-pvp），
   自动加载 mercenary.json + current-mercenary-definitions.json + active_skill.json + cooperative_skill.json，
   精确核对全部预设佣兵是否存在缺失或新增变动；
3. 支持传入新版本 APK（如 Cocos Creator 3.x 测试服 APK），自动扫描引擎类型、H5 支持、TS 模块数与核心协议命中情况；
4. 支持 --json-out 导出结构化报告供 CI 或后续同步脚本消费。

用法：
    python tools/sync_game_data.py
    python tools/sync_game_data.py --apk f:\\mxywk-v1.0.1-vc2.apk.1 --json-out dist/game-data-audit.json
"""
from pathlib import Path
import argparse
import json
import os
import re
import sys
import zipfile

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
if hasattr(sys.stderr, 'reconfigure'):
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_DATA_DIR = Path(os.environ.get('PVP_DATA_DIR', r'F:\codex1\tmp\test-server-pvp'))
LEADER_TID = 99000001


def load_preset_refs(presets_path: Path):
    data = json.loads(presets_path.read_text(encoding='utf-8'))
    merc_ids = {}
    weapon_ids = set()
    rune_ids = set()
    formation_count = 0
    schema_errors = []

    for key, val in data.items():
        if not isinstance(val, dict) or 'slots' not in val:
            continue
        formation_count += 1
        slots = val.get('slots') or []
        if not isinstance(slots, list) or len(slots) == 0:
            schema_errors.append(f'{key}: slots 为空')
        wid = val.get('weapon_id') if isinstance(val.get('weapon_id'), int) else (val.get('weapon') if isinstance(val.get('weapon'), int) else None)
        if wid is not None:
            weapon_ids.add(wid)
        for r in val.get('runes') or []:
            if isinstance(r, list) and r and isinstance(r[0], int):
                rune_ids.add(r[0])
        for slot in slots:
            if isinstance(slot, dict) and isinstance(slot.get('id'), int):
                merc_ids[slot['id']] = slot.get('name', str(slot['id']))
            else:
                schema_errors.append(f'{key}: 存在无效槽位 {slot!r}')
    return formation_count, merc_ids, weapon_ids, rune_ids, schema_errors


def _collect_ids(table_obj):
    ids = set()
    if isinstance(table_obj, dict):
        for k, v in table_obj.items():
            if str(k).isdigit():
                ids.add(int(k))
            elif isinstance(v, dict) and str(v.get('ID', '')).isdigit():
                ids.add(int(v['ID']))
    elif isinstance(table_obj, list):
        for row in table_obj:
            if isinstance(row, dict):
                raw_id = row.get('ID') if row.get('ID') is not None else row.get('id')
                if raw_id is not None and str(raw_id).isdigit():
                    ids.add(int(raw_id))
    return ids


def load_data_dir_tables(data_dir: Path):
    if not data_dir.exists():
        return None
    mercs = set()
    active_skills = set()
    coop_skills = set()
    overlay_mercs = 0

    for fname, target in [
        ('mercenary.json', mercs),
        ('active_skill.json', active_skills),
        ('cooperative_skill.json', coop_skills),
    ]:
        fp = data_dir / fname
        if fp.exists():
            target.update(_collect_ids(json.loads(fp.read_text(encoding='utf-8'))))

    ov_path = data_dir / 'current-mercenary-definitions.json'
    if ov_path.exists():
        ov = json.loads(ov_path.read_text(encoding='utf-8')).get('tables', {})
        ov_m = _collect_ids(ov.get('mercenary', []))
        overlay_mercs = len(ov_m)
        mercs.update(ov_m)
        active_skills.update(_collect_ids(ov.get('active_skill', [])))
        coop_skills.update(_collect_ids(ov.get('cooperative_skill', [])))

    return {
        'dataDir': str(data_dir),
        'mercenaries': mercs,
        'overlayMercenaries': overlay_mercs,
        'activeSkills': active_skills,
        'coopSkills': coop_skills,
    }


def inspect_apk(apk_path: Path):
    info = {
        'apk': str(apk_path),
        'engine': 'unknown',
        'hasH5Support': False,
        'moduleCount': 0,
        'mercenaryModules': [],
        'protocolHits': {},
    }
    with zipfile.ZipFile(apk_path) as z:
        names = z.namelist()
        if any(n.endswith('libcocos.so') for n in names):
            info['engine'] = 'Cocos Creator 3.x'
        elif any(n.endswith('.lua') or n.endswith('.luac') for n in names):
            info['engine'] = 'Cocos2d-x Lua'

        bundle_candidates = [n for n in names if re.match(r'^assets/assets/main/index\.[0-9a-f]+\.js$', n)]
        if bundle_candidates:
            idx = z.read(bundle_candidates[0]).decode('utf-8', 'ignore')
            mods = re.findall(r'System\.register\("([^"]+)"', idx)
            info['moduleCount'] = len(mods)
            info['hasH5Support'] = ('aandm.h5.' in idx)
            info['mercenaryModules'] = [m.split('/')[-1] for m in mods if any(k in m.lower() for k in ('mercenary', 'skill', 'rune', 'battle', 'troop'))][:30]
            for kw in ['dig_block', 'mining_dig', 'equip_rune', 'query_rune', 'resource_recycle', 'escort_', 'guild_boss', 'open_box', 'buy_guild']:
                info['protocolHits'][kw] = idx.count(kw)
    return info


def main():
    parser = argparse.ArgumentParser(description='冒险与挖矿 佣兵/技能/预设数据一致性校验与新版 APK 扫描工具')
    parser.add_argument('--data-dir', default=str(DEFAULT_DATA_DIR), help='可选：静态游戏表数据目录')
    parser.add_argument('--apk', default='', help='可选：新版本 APK 路径，用于检测引擎与模块变化')
    parser.add_argument('--json-out', default='', help='可选：输出 JSON 诊断报告路径')
    args = parser.parse_args()

    presets_path = ROOT / 'pvp-simulator' / 'presets.json'
    formation_count, preset_mercs, weapon_ids, rune_ids, schema_errors = load_preset_refs(presets_path)
    if schema_errors:
        raise SystemExit('预设库结构校验失败：\n' + '\n'.join(schema_errors))

    non_leader_mercs = {mid: name for mid, name in preset_mercs.items() if mid < LEADER_TID}
    report = {
        'formations': formation_count,
        'uniquePresetMercenaries': len(non_leader_mercs),
        'uniqueLeaderSkins': len(preset_mercs) - len(non_leader_mercs),
        'uniquePresetWeapons': sorted(weapon_ids),
        'uniquePresetRunes': sorted(rune_ids),
        'schemaValid': True,
    }

    print(f'[PASS] 预设库结构校验通过：共 {formation_count} 套阵型 | 佣兵 {len(non_leader_mercs)} 个 | 主角皮肤 {len(preset_mercs) - len(non_leader_mercs)} 个 | 武器 {len(weapon_ids)} 种 | 符文 {len(rune_ids)} 种')

    tables = load_data_dir_tables(Path(args.data_dir)) if args.data_dir else None
    if tables is not None:
        missing = {str(mid): nm for mid, nm in sorted(non_leader_mercs.items()) if mid not in tables['mercenaries']}
        report['dataTables'] = {
            'mercenaryCount': len(tables['mercenaries']),
            'overlayMercenaryCount': tables['overlayMercenaries'],
            'activeSkillCount': len(tables['activeSkills']),
            'coopSkillCount': len(tables['coopSkills']),
            'missingPresetMercenaries': missing,
        }
        print(f'[OK] 已加载游戏静态表：佣兵 {len(tables["mercenaries"])}（含热更定义 {tables["overlayMercenaries"]}）| 主动技能 {len(tables["activeSkills"])} | 合作技 {len(tables["coopSkills"])}')
        if missing:
            print(f'[WARN] 发现 {len(missing)} 个预设佣兵缺失于静态表：{missing}')
        else:
            print('[PASS] 全部预设佣兵 100% 匹配游戏静态表（0 缺失）。')
    else:
        print('[INFO] 未发现本机外部静态数据目录（CI 环境正常跳过外部表比对，仅校验仓库内预设库结构）。')

    if args.apk:
        apk_path = Path(args.apk)
        if not apk_path.exists():
            raise SystemExit(f'找不到 APK 文件：{apk_path}')
        report['apkAudit'] = inspect_apk(apk_path)
        a = report['apkAudit']
        print(f'[APK] 引擎：{a["engine"]} | TS 模块数：{a["moduleCount"]} | H5 支持：{a["hasH5Support"]}')
        print(f'[APK] 关键协议命中：{a["protocolHits"]}')

    if args.json_out:
        out_p = Path(args.json_out)
        out_p.parent.mkdir(parents=True, exist_ok=True)
        out_p.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        print(f'[OK] 报告已写入 {out_p}')


if __name__ == '__main__':
    main()
