"""Convenient CLI for the 冒险与挖矿 real-server-rule PvP simulator."""

from __future__ import annotations

import argparse
import collections
import concurrent.futures
import json
import math
import os
import subprocess
import sys
import time
from pathlib import Path
from typing import Any

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from engine import SimulatorEngine
from export_card import export_formation_png, render_team_card

DEFAULT_MATRIX_TEAMS = [
    'v14_meiling_all4_single',
    'v13_all4_60plus_king',
    'v10_recovery_02_15_16',
    'v15_anti_t15_max',
    'friend_73ID6',
    'friend_3YPN0',
    'friend_NZVHY',
]

# Worker-local state for parallel matrix / sweep evaluation
_M_ENG: SimulatorEngine | None = None
_M_MODE: str = 'fair_eq'
_M_OVERRIDES: dict[str, Any] | None = None


def _matrix_worker_init(mode: str, mode_overrides: dict[str, Any] | None) -> None:
    global _M_ENG, _M_MODE, _M_OVERRIDES
    _M_ENG = SimulatorEngine()
    _M_MODE = mode
    _M_OVERRIDES = mode_overrides


def _matrix_worker_pair(task: tuple[int, int, dict[str, Any], dict[str, Any], int, int]) -> tuple[int, int, float]:
    i, j, ta, tb, runs, seed = task
    assert _M_ENG is not None
    res = _M_ENG.simulate_series(ta, tb, runs_each=runs, seed=seed, mode=_M_MODE, mode_overrides=_M_OVERRIDES)
    return i, j, float(res['winrate'])


def _sweep_worker_eval(
    task: tuple[int, int, dict[str, Any], list[tuple[str, dict[str, Any]]], int, int, dict[str, Any] | None],
) -> tuple[int, int, dict[str, float], float, float]:
    ti, vi, team, opps, runs, seed, ov = task
    assert _M_ENG is not None
    res = {}
    for oi, (ok, opp) in enumerate(opps):
        res[ok] = float(_M_ENG.simulate_series(team, opp, runs_each=runs, seed=seed + oi * 313, mode=_M_MODE, mode_overrides=ov)['winrate'])
    vals = list(res.values())
    return ti, vi, res, min(vals), sum(vals) / len(vals)


def load_presets() -> dict[str, Any]:
    return json.loads((HERE / 'presets.json').read_text(encoding='utf8'))


def load_team(ref: str, presets: dict[str, Any]) -> dict[str, Any]:
    if ref in presets:
        return presets[ref]
    p = Path(ref)
    if p.exists():
        data = json.loads(p.read_text(encoding='utf8'))
        if 'roster' in data:
            return {
                'name': p.stem,
                'weapon_id': data['roster'].get('weapon_id', data['roster'].get('weapon', 9)),
                'runes': [r['id'] if isinstance(r, dict) else r for r in data.get('runes', [])],
                'slots': data['roster']['slots'],
            }
        return data
    raise FileNotFoundError(f'Unknown preset or JSON file: {ref}')


def _add_mode_args(p: argparse.ArgumentParser, default_mode: str | None = None) -> None:
    p.add_argument('--mode', default=default_mode, help='规则档位: server30 | max | fair_eq')
    p.add_argument('--q7-c2', type=int, default=None, help='覆盖二契炫金配额上限 K (如 0/1/2/4/99)')
    p.add_argument('--artifact-lv', type=int, default=None, help='覆盖宝具等级 (0~15)')
    p.add_argument('--rune-lv', type=int, default=None, help='覆盖统一符文等级 (1~20)')
    p.add_argument('--merc-lv', type=int, default=None, help='覆盖佣兵等级 (如 30/100)')
    p.add_argument('--merc-wake', type=int, default=None, help='覆盖佣兵觉醒上限 (如 5/99)')
    p.add_argument('--leader-contract', type=int, default=None, help='覆盖主角契约等级 (0~22)')
    p.add_argument('--equalize-bp', action='store_true', help='诊断开关: 对战时将双方总战力拉齐到均值')
    p.add_argument('--no-runes', action='store_true', help='诊断开关: 双方零符文(纯测30人+宿命武器骨架)')
    p.add_argument('--mirror-runes', default=None, help='诊断开关: 双方强制同配指定5符文(如 5x防衰 或 59,59,59,59,59)')


def _extract_overrides(args: argparse.Namespace) -> dict[str, Any] | None:
    ov: dict[str, Any] = {}
    mapping = [
        ('q7_c2', 'q7_contract2_limit'),
        ('artifact_lv', 'artifact_lv'),
        ('rune_lv', 'rune_lv'),
        ('merc_lv', 'merc_level'),
        ('merc_wake', 'merc_wakeup_cap'),
        ('leader_contract', 'leader_contract_id'),
        ('mirror_runes', 'mirror_runes'),
    ]
    for attr, key in mapping:
        val = getattr(args, attr, None)
        if val is not None:
            ov[key] = val
    if getattr(args, 'equalize_bp', False):
        ov['equalize_bp'] = True
    if getattr(args, 'no_runes', False):
        ov['no_runes'] = True
    return ov or None


def _format_mode_tag(mode: str, ov: dict[str, Any] | None) -> str:
    if not ov:
        return mode
    parts = [mode]
    if 'q7_contract2_limit' in ov:
        parts.append(f'q7c2={ov["q7_contract2_limit"]}')
    if 'artifact_lv' in ov:
        parts.append(f'art={ov["artifact_lv"]}')
    if 'rune_lv' in ov:
        parts.append(f'runeLv={ov["rune_lv"]}')
    if 'merc_level' in ov:
        parts.append(f'lv={ov["merc_level"]}')
    if 'merc_wakeup_cap' in ov:
        parts.append(f'wake<={ov["merc_wakeup_cap"]}')
    if 'leader_contract_id' in ov:
        parts.append(f'lc={ov["leader_contract_id"]}')
    if ov.get('equalize_bp'):
        parts.append('equalBP')
    if ov.get('no_runes'):
        parts.append('noRunes')
    if ov.get('mirror_runes'):
        parts.append(f'mirror={ov["mirror_runes"]}')
    return ' | '.join(parts)


def _solve_symmetric_nash(wr_matrix: list[list[float]], iters: int = 3000) -> list[float]:
    """Computes approximate Nash equilibrium meta weights for an antisymmetric zero-sum payoff matrix."""
    n = len(wr_matrix)
    if n == 0:
        return []
    if n == 1:
        return [1.0]
    payoff = [[wr_matrix[i][j] - 0.5 for j in range(n)] for i in range(n)]
    w = [1.0 / n] * n
    acc = [0.0] * n
    for t in range(1, iters + 1):
        # Expected payoff of each pure strategy against current mixed strategy
        vals = [sum(payoff[i][j] * w[j] for j in range(n)) for i in range(n)]
        eta = 1.5 / math.sqrt(t)
        mx = max(vals)
        nw = [w[i] * math.exp(eta * (vals[i] - mx)) for i in range(n)]
        s = sum(nw)
        w = [x / s for x in nw]
        for i in range(n):
            acc[i] += w[i]
    tot = sum(acc)
    return [x / tot for x in acc]


def cmd_presets(args: argparse.Namespace) -> None:
    presets = load_presets()
    print('可用预设阵容:')
    for k, v in presets.items():
        print(f'  {k:24s} -> {v["name"]} (武器:{v["weapon"]}, 符文:{v["runes"]})')


def cmd_find(args: argparse.Namespace, eng: SimulatorEngine) -> None:
    q = args.keyword.strip()
    cat = eng.get_catalog()
    print(f'=== 搜索 "{q}" ===')
    for m in cat['mercenaries']:
        if q in m['name'] or q == str(m['id']):
            qtag = '炫金' if m['quality'] == 7 else ('闪金' if m['quality'] == 6 else f'Q{m["quality"]}')
            print(f"  [佣兵] id={m['id']}  name={m['name']}  品质={qtag}  上限={m['cap']}  可洗练={m['can_wash']}")
    for r in cat['runes']:
        if q in r['name'] or q == str(r['id']):
            print(f"  [符文] id={r['id']}  name={r['name']}  Lv20效果(己先/防/闪/王, 敌先/防/闪/王)={r['delta']}")
    for w in cat['weapons']:
        if q in w['name'] or q == str(w['id']):
            print(f"  [武器] id={w['id']}  name={w['name']}")


def cmd_info(args: argparse.Namespace, eng: SimulatorEngine) -> None:
    presets = load_presets()
    team = load_team(args.team, presets)
    ov = _extract_overrides(args)
    info = eng.inspect_team(team, mode=args.mode, mode_overrides=ov)
    pe = info['profile_effective']
    print(f"=== {info['name']} [{_format_mode_tag(info['mode'], ov)}] ===")
    print(f"宿命武器: {info['weapon']['name']} ({info['weapon']['id']})")
    if info['runes']:
        print(f"符文配置: {', '.join(r['name'] + '(' + str(r['id']) + ',Lv' + str(r.get('lv', 20)) + ')' for r in info['runes'])}")
    else:
        print('符文配置: 无符文 (纯骨架诊断模式)')
    print(f"合体技({len(info['combos'])}): {', '.join(info['combos']) or '无'}")
    print(f"总战力: {info['max_bp']:,}  开场护盾: {info['shield']:,}")
    q7 = info['q7_summary']
    if q7['c2_limit'] is not None:
        c2_names = ', '.join(f"#{s['pos']}{s['name']}" for s in q7['c2_slots']) or '无'
        c1_names = ', '.join(f"#{s['pos']}{s['name']}" for s in q7['c1_slots']) or '无'
        print(
            f"等资源基准: 佣兵Lv{pe['merc_level']}/≤{pe['merc_wakeup_cap']}觉 · 宝具Lv{pe['artifact_lv']} · "
            f"主角契约#{pe['leader_contract_id']} · 符文Lv{pe['rune_lv']} · 屏蔽实服覆盖={pe['ignore_team_overrides']}"
        )
        print(f"炫金契约(总{q7['total']}张): 二契({len(q7['c2_slots'])}/{q7['c2_limit']})=[{c2_names}] | 一契({len(q7['c1_slots'])})=[{c1_names}]")
    bs = info['base_stats']
    ar = info['after_rune_stats']
    db = info['enemy_rune_debuff']
    print(f"含称号裸四维: 先{bs['speed']} / 防{bs['defense']} / 闪{bs['dodge']} / 王{bs['authority']}")
    print(f"含符文有效值: 先{ar['speed']} / 防{ar['defense']} / 闪{ar['dodge']} / 王{ar['authority']}  (敌方减益: 先{db['speed']} / 防{db['defense']} / 闪{db['dodge']} / 王{db['authority']})")
    if info['violations']:
        print('规则警告:', '; '.join(info['violations']))
    else:
        print('规则合规检查: 通过 (符合实服图鉴/上限及当前分支契约限制)')
    print('\n--- 技能概率池 (按实服顺序: 合体技优先 -> 个人技) ---')
    for sk in info['skills']:
        tag = '[合体]' if sk['is_coop'] else '[个人]'
        masked = ' (首回合已被前100%挤占)' if sk['turn1_masked'] else ''
        print(f"  #{sk['order']:2d} {tag} {sk['name']} ({sk['id']})  {sk['prob']:3d}%  累计={sk['cum_total']:3d}%{masked}")


def cmd_vs(args: argparse.Namespace, eng: SimulatorEngine) -> None:
    presets = load_presets()
    a = load_team(args.team_a, presets)
    b = load_team(args.team_b, presets)
    ov = _extract_overrides(args)
    res = eng.simulate_series(a, b, runs_each=args.n, seed=args.seed, mode=args.mode, mode_overrides=ov)
    ia = eng.inspect_team(a, mode=args.mode, mode_overrides=ov)
    ib = eng.inspect_team(b, mode=args.mode, mode_overrides=ov)
    print(f"=== {a['name']}  VS  {b['name']}  [{_format_mode_tag(args.mode, ov)}] ===")
    print(f"  A 战力={ia['max_bp']:,}  裸四维={ia['base_stats']}  含符={ia['after_rune_stats']}")
    print(f"  B 战力={ib['max_bp']:,}  裸四维={ib['base_stats']}  含符={ib['after_rune_stats']}")
    print(f"对称 {res['total']} 场 ({args.n}先手侧 + {args.n}后手侧):")
    print(f"  A 胜: {res['wins']}  负: {res['losses']}  平: {res['draws']}  ->  综合胜率: {res['winrate']:.2%}")
    print(f"  A 在左侧: {res['a_as_left']['wins']}W/{res['a_as_left']['losses']}L/{res['a_as_left']['draws']}D")
    print(f"  A 在右侧: {res['a_as_right']['wins']}W/{res['a_as_right']['losses']}L/{res['a_as_right']['draws']}D")


def cmd_bench(args: argparse.Namespace, eng: SimulatorEngine) -> None:
    presets = load_presets()
    a = load_team(args.team, presets)
    ov = _extract_overrides(args)
    wrs = []
    print(f"=== {a['name']} 全预设基准测试 (每对手 {args.n*2} 场, mode={_format_mode_tag(args.mode, ov)}) ===")
    for i, (k, b) in enumerate(presets.items()):
        res = eng.simulate_series(a, b, runs_each=args.n, seed=args.seed + i * 313, mode=args.mode, mode_overrides=ov)
        wrs.append((k, b['name'], res['winrate']))
        print(f"  vs {k:24s} ({b['name']}): {res['winrate']:.2%}")
    mean_wr = sum(x[2] for x in wrs) / len(wrs)
    min_item = min(wrs, key=lambda x: x[2])
    print(f"\n总均胜: {mean_wr:.2%} | 最低胜率: {min_item[2]:.2%} (vs {min_item[0]})")


def cmd_trace(args: argparse.Namespace, eng: SimulatorEngine) -> None:
    presets = load_presets()
    a = load_team(args.team_a, presets)
    b = load_team(args.team_b, presets)
    ov = _extract_overrides(args)
    tr = eng.simulate_trace(a, b, seed=args.seed, mode=args.mode, forced_first=args.first, mode_overrides=ov)
    print(f"=== 单局战报回放: A={a['name']} vs B={b['name']} (mode={_format_mode_tag(args.mode, ov)}, seed={args.seed}) ===")
    print(f"A 实战四维: {tr['a_effective']}")
    print(f"B 实战四维: {tr['b_effective']}")
    print(f"胜者: {tr['winner']}  总回合: {tr['rounds']}\n")
    for ev in tr['events']:
        coop_tag = '[合体] ' if ev['is_coop'] else ''
        hit_tag = '闪避!' if ev['is_dodge'] else f"造成伤害:{ev['dmg_to_enemy']:,}"
        heal_tag = f" 自回:{ev['self_heal']:,}" if ev['self_heal'] > 0 else ''
        seal_tag = f" 封印({ev['seal_turn']}敌回合,>{ev['seal_cond1']}%)" if ev['sealed'] else ''
        print(
            f"R{ev['round']:02d} T{ev['turn']:02d} [{ev['actor']}] {coop_tag}{ev['skill_name']} -> "
            f"{hit_tag}{heal_tag}{seal_tag} | A血:{ev['a_bp']:,} B血:{ev['b_bp']:,}"
        )


def cmd_export(args: argparse.Namespace, eng: SimulatorEngine) -> None:
    presets = load_presets()
    team = load_team(args.team, presets)
    mode = args.mode or eng.profile.get('default_mode', 'server30')
    ov = _extract_overrides(args)
    live_report = json.loads(Path(args.live_report).read_text(encoding='utf8')) if getattr(args, 'live_report', None) else None
    out_path = Path(args.output) if args.output else (HERE / 'exports' / f'{Path(args.team).stem}_{mode}.png')
    saved = export_formation_png(eng, team, out_path, mode=mode, title=args.subtitle, live_report=live_report, mode_overrides=ov)
    img_sz = render_team_card(eng, team, mode=mode, subtitle=args.subtitle, live_report=live_report, mode_overrides=ov).size
    tag = '一图流(含实机战绩)' if live_report else '1:1 阵容图'
    print(f'已导出 {tag}: {saved.resolve()} ({img_sz[0]}x{img_sz[1]})')


def cmd_profile(eng: SimulatorEngine) -> None:
    p = eng.profile
    print(f"=== 当前账号档案: {p.get('profile_name', 'default')} ({eng.profile_path}) ===")
    print(f"默认模式: {p.get('default_mode', 'server30')}  默认主角ID: {p.get('leader_tid', 99000001)}")
    for m in eng.list_modes():
        mp = eng.get_mode_profile(m)
        tb = mp.get('title_bonus', {})
        extra = ''
        if mp.get('q7_contract2_limit') is not None:
            extra = (
                f" | 炫金默认{mp.get('q7_contract_lv', 1)}契/二契配额K={mp.get('q7_contract2_limit')}"
                f" | 符文Lv{mp.get('rune_lv', 20)} | 隔离实服覆盖={bool(mp.get('ignore_team_overrides'))}"
            )
        print(
            f"  [{m:8s}] 主角 Lv{mp.get('leader_level')}/{mp.get('leader_wakeup')}觉/强化{mp.get('leader_forge_lv')}/契约#{mp.get('leader_contract_id', 22)} | "
            f"佣兵 Lv{mp.get('merc_level')}/≤{mp.get('merc_wakeup_cap')}觉/强化{mp.get('merc_forge_lv')}/宝具{mp.get('artifact_lv')} | "
            f"称号 {tb.get('speed')}/{tb.get('defense')}/{tb.get('dodge')}/{tb.get('authority')} (BP+{tb.get('bp')}){extra}"
        )
    groups = p.get('opponent_groups', {})
    if groups:
        print('内置对手组:')
        for gname, members in groups.items():
            print(f"  {gname:18s}: {', '.join(members)}")


def cmd_deliver(args: argparse.Namespace, eng: SimulatorEngine) -> None:
    """Standardized delivery gate: validate -> 0-error stats -> optional opponent group eval -> 1:1 PNG."""
    presets = load_presets()
    team = load_team(args.team, presets)
    mode = args.mode or eng.profile.get('default_mode', 'server30')
    ov = _extract_overrides(args)
    info = eng.inspect_team(team, mode=mode, mode_overrides=ov)

    print(f"=== 标准交付门禁: {info['name']} [{_format_mode_tag(mode, ov)}] ===")
    if info['violations']:
        print('[门禁失败] 合规校验未通过:')
        for v in info['violations']:
            print(f'  - {v}')
        sys.exit(2)
    print('[1/3 合规门禁] 通过 (图鉴/上限/主角/洗练/契约全部合法)')

    bs = info['base_stats']
    ar = info['after_rune_stats']
    db = info['enemy_rune_debuff']
    cov = min(100, sum(sk['prob'] for sk in info['skills']))
    print(
        f"[2/3 零误差面板] 战力={info['max_bp']:,} | 基础四维: 先{bs['speed']} / 防{bs['defense']} / 闪{bs['dodge']} / 王{bs['authority']} "
        f"| 技能覆盖率={cov}% | 合体技({len(info['combos'])}): {', '.join(info['combos']) or '无'}"
    )
    print(
        f"               含符文实战: 先{ar['speed']} / 防{ar['defense']} / 闪{ar['dodge']} / 王{ar['authority']} "
        f"| 敌方衰弱: 先{db['speed']} / 防{db['defense']} / 闪{db['dodge']} / 王{db['authority']}"
    )

    if args.group:
        groups = eng.profile.get('opponent_groups', {})
        if args.group not in groups:
            raise KeyError(f'Unknown opponent group: {args.group} (available: {list(groups.keys())})')
        opps = groups[args.group]
        print(f"\n--- 对手组评测 [{args.group}] (每对手 {args.n * 2} 局对称先手, mode={_format_mode_tag(mode, ov)}) ---")
        wrs = []
        for idx, ok in enumerate(opps):
            opp = load_team(ok, presets)
            res = eng.simulate_series(team, opp, runs_each=args.n, seed=args.seed + idx * 313, mode=mode, mode_overrides=ov)
            wrs.append((ok, opp['name'], res['winrate']))
            print(f"  vs {ok:16s} ({opp['name']}): {res['winrate']:.2%} ({res['wins']}W/{res['losses']}L/{res['draws']}D)")
        mean_wr = sum(x[2] for x in wrs) / len(wrs)
        min_item = min(wrs, key=lambda x: x[2])
        print(f"  汇总: 均胜 {mean_wr:.2%} | 最低 {min_item[2]:.2%} (vs {min_item[0]})\n")

    out_path = Path(args.output) if args.output else (HERE / 'exports' / f'{Path(args.team).stem}_{mode}.png')
    saved = export_formation_png(eng, team, out_path, mode=mode, title=args.subtitle, mode_overrides=ov)
    print(f'[3/3 1:1原生出图] 已导出: {saved.resolve()}')


def cmd_matrix(args: argparse.Namespace, eng: SimulatorEngine) -> None:
    """N x N symmetric round-robin evaluation + Nash equilibrium T0 meta weights."""
    t0 = time.time()
    presets = load_presets()
    mode = args.mode or 'fair_eq'
    ov = _extract_overrides(args)

    if args.teams:
        refs = [x.strip() for x in args.teams.split(',') if x.strip()]
    elif args.group:
        groups = eng.profile.get('opponent_groups', {})
        if args.group not in groups:
            raise KeyError(f'Unknown group: {args.group}')
        refs = list(groups[args.group])
    else:
        refs = [k for k in DEFAULT_MATRIX_TEAMS if k in presets]

    teams = [(ref, load_team(ref, presets)) for ref in refs]
    n_teams = len(teams)
    if n_teams < 2:
        raise ValueError('matrix requires at least 2 teams')

    infos = [eng.inspect_team(tm, mode=mode, mode_overrides=ov) for _, tm in teams]
    tasks = []
    pair_idx = 0
    for i in range(n_teams):
        for j in range(i + 1, n_teams):
            tasks.append((i, j, teams[i][1], teams[j][1], args.n, args.seed + pair_idx * 1009))
            pair_idx += 1

    mat = [[0.5] * n_teams for _ in range(n_teams)]
    n_workers = max(1, min(args.workers, len(tasks), os.cpu_count() or 4))
    if n_workers > 1 and len(tasks) > 1:
        with concurrent.futures.ProcessPoolExecutor(
            max_workers=n_workers,
            initializer=_matrix_worker_init,
            initargs=(mode, ov),
        ) as pool:
            for i, j, wr in pool.map(_matrix_worker_pair, tasks):
                mat[i][j] = wr
                mat[j][i] = 1.0 - wr
    else:
        for i, j, ta, tb, runs, seed in tasks:
            wr = eng.simulate_series(ta, tb, runs_each=runs, seed=seed, mode=mode, mode_overrides=ov)['winrate']
            mat[i][j] = wr
            mat[j][i] = 1.0 - wr

    nash = _solve_symmetric_nash(mat)
    elapsed = time.time() - t0

    print(f"=== N×N 循环赛矩阵 [{_format_mode_tag(mode, ov)}] ({n_teams}队, 每对 {args.n*2} 场对称, {elapsed:.2f}s) ===")
    codes = [f'T{i+1}' for i in range(n_teams)]
    header = '      ' + ' '.join(f'{c:>6s}' for c in codes) + ' |   均胜   保底  Nash'
    print(header)
    print('-' * len(header))

    summary_rows = []
    for i in range(n_teams):
        others = [(j, mat[i][j]) for j in range(n_teams) if j != i]
        avg_wr = sum(w for _, w in others) / len(others)
        min_j, min_wr = min(others, key=lambda x: x[1])
        max_j, max_wr = max(others, key=lambda x: x[1])
        row_cells = ' '.join(f'  --  ' if i == j else f'{mat[i][j]*100:5.1f}%' for j in range(n_teams))
        print(f'{codes[i]:4s}  {row_cells} | {avg_wr*100:5.1f}% {min_wr*100:5.1f}% {nash[i]*100:5.1f}%')
        summary_rows.append({
            'code': codes[i],
            'key': teams[i][0],
            'name': infos[i]['name'],
            'max_bp': infos[i]['max_bp'],
            'base_stats': infos[i]['base_stats'],
            'q7_total': infos[i]['q7_summary']['total'],
            'q7_c2': len(infos[i]['q7_summary']['c2_slots']),
            'avg_wr': avg_wr,
            'min_wr': min_wr,
            'worst_vs': teams[min_j][0],
            'best_vs': teams[max_j][0],
            'best_wr': max_wr,
            'nash_weight': nash[i],
        })

    print('\n--- 综合排行 (按 Nash 权重 -> 均胜排序) ---')
    for rank, r in enumerate(sorted(summary_rows, key=lambda x: (round(x['nash_weight'], 3), x['avg_wr']), reverse=True), 1):
        bs = r['base_stats']
        stat_s = f"{bs['speed']}/{bs['defense']}/{bs['dodge']}/{bs['authority']}"
        print(
            f"  #{rank} {r['code']} {r['key']:24s} | Nash={r['nash_weight']*100:5.1f}%  均胜={r['avg_wr']*100:5.1f}%  "
            f"保底={r['min_wr']*100:5.1f}%(vs {r['worst_vs']}) | BP={r['max_bp']/1e6:5.2f}M  四维={stat_s}  炫金二契={r['q7_c2']}/{r['q7_total']}"
        )

    if args.out:
        payload = {
            'mode': mode,
            'mode_overrides': ov,
            'runs_each': args.n,
            'elapsed_s': round(elapsed, 2),
            'teams': summary_rows,
            'matrix': [[round(v, 4) for v in row] for row in mat],
        }
        Path(args.out).write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding='utf8')
        print(f'\n已保存矩阵结果: {Path(args.out).resolve()}')


def cmd_sweep(args: argparse.Namespace, eng: SimulatorEngine) -> None:
    """Resource sensitivity ladder scan across q7_c2 / artifact_lv / rune_lv / merc_lv / bp_mode."""
    t0 = time.time()
    presets = load_presets()
    mode = args.mode or 'fair_eq'
    base_ov = dict(_extract_overrides(args) or {})

    team_refs = args.teams
    teams = [(ref, load_team(ref, presets)) for ref in team_refs]

    groups = eng.profile.get('opponent_groups', {})
    if args.vs in groups:
        opp_refs = list(groups[args.vs])
    else:
        opp_refs = [x.strip() for x in args.vs.split(',') if x.strip()]
    opps = [(r, load_team(r, presets)) for r in opp_refs]

    param_map = {
        'q7_c2': ('q7_contract2_limit', int),
        'artifact_lv': ('artifact_lv', int),
        'rune_lv': ('rune_lv', int),
        'merc_lv': ('merc_level', int),
        'bp_mode': ('equalize_bp', lambda s: s.strip().lower() in ('1', 'true', 'equal', 'eq', 'yes')),
    }
    if args.param not in param_map:
        raise ValueError(f'Unsupported --param: {args.param}')
    ov_key, caster = param_map[args.param]
    raw_vals = [x.strip() for x in args.values.split(',') if x.strip()]
    val_items = [(rv, caster(rv)) for rv in raw_vals]

    tasks = []
    for ti, (_, tm) in enumerate(teams):
        for vi, (_, val) in enumerate(val_items):
            ov = dict(base_ov)
            ov[ov_key] = val
            tasks.append((ti, vi, tm, opps, args.n, args.seed + ti * 997 + vi * 131, ov))

    grid: dict[tuple[int, int], tuple[float, float]] = {}
    n_workers = max(1, min(args.workers, len(tasks), os.cpu_count() or 4))
    if n_workers > 1 and len(tasks) > 1:
        with concurrent.futures.ProcessPoolExecutor(
            max_workers=n_workers,
            initializer=_matrix_worker_init,
            initargs=(mode, None),
        ) as pool:
            for ti, vi, _, mn, av in pool.map(_sweep_worker_eval, tasks):
                grid[(ti, vi)] = (av, mn)
    else:
        for ti, vi, tm, opp_list, runs, seed, ov in tasks:
            wrs = [eng.simulate_series(tm, o, runs_each=runs, seed=seed + oi * 313, mode=mode, mode_overrides=ov)['winrate'] for oi, (_, o) in enumerate(opp_list)]
            grid[(ti, vi)] = (sum(wrs) / len(wrs), min(wrs))

    print(
        f"=== 资源敏感度阶梯扫描 [{_format_mode_tag(mode, base_ov)}] "
        f"param={args.param} vs [{', '.join(opp_refs)}] (每对手{args.n*2}场, {time.time()-t0:.2f}s) ==="
    )
    col_hdr = ' | '.join(f'{f"{args.param}={rv}":^15s}' for rv, _ in val_items)
    print(f"{'阵容':26s} | {col_hdr}")
    print('-' * (29 + len(col_hdr)))
    for ti, (ref, _) in enumerate(teams):
        cells = []
        for vi in range(len(val_items)):
            av, mn = grid[(ti, vi)]
            cells.append(f'均{av*100:4.1f}%/底{mn*100:4.1f}%')
        print(f'{ref:26s} | ' + ' | '.join(f'{c:^15s}' for c in cells))


def cmd_check() -> None:
    p = subprocess.run([sys.executable, str(HERE / 'test_sim.py')], check=False)
    sys.exit(p.returncode)


def main() -> None:
    parser = argparse.ArgumentParser(description='冒险与挖矿 实服真规则 & 等资源公平评估 PvP 模拟器 CLI')
    sub = parser.add_subparsers(dest='cmd', required=True)

    sub.add_parser('presets', help='列出所有内置预设阵容')
    sub.add_parser('profile', help='查看当前账号养成与规则分支档案 (account_profile.json)')
    sub.add_parser('check', help='运行四维 0 误差与全套自动化回归测试')

    p_find = sub.add_parser('find', help='按名字/ID搜索佣兵、符文、宿命武器')
    p_find.add_argument('keyword')

    p_info = sub.add_parser('info', help='查看阵容面板、技能概率、炫金契约分配与合规性')
    p_info.add_argument('team', help='预设名或 JSON 路径')
    _add_mode_args(p_info)

    p_vs = sub.add_parser('vs', help='两套阵容对称胜率测试')
    p_vs.add_argument('team_a')
    p_vs.add_argument('team_b')
    p_vs.add_argument('-n', type=int, default=500, help='每侧模拟场数（总场数=2n）')
    _add_mode_args(p_vs)
    p_vs.add_argument('--seed', type=int, default=20261125)

    p_bench = sub.add_parser('bench', help='对抗全部内置预设基准')
    p_bench.add_argument('team')
    p_bench.add_argument('-n', type=int, default=250)
    _add_mode_args(p_bench)
    p_bench.add_argument('--seed', type=int, default=20261125)

    p_trace = sub.add_parser('trace', help='查看单局逐回合详细战报')
    p_trace.add_argument('team_a')
    p_trace.add_argument('team_b')
    _add_mode_args(p_trace)
    p_trace.add_argument('--seed', type=int, default=20261125)
    p_trace.add_argument('--first', choices=('left', 'right'), default=None)

    p_exp = sub.add_parser('export', help='导出 1:1 游戏原生素材 5x6 阵容图片 / 含实机战绩一图流 (PNG)')
    p_exp.add_argument('team', help='预设名或 JSON 路径')
    _add_mode_args(p_exp)
    p_exp.add_argument('-o', '--output', default=None, help='输出 PNG 路径（默认写入 exports/）')
    p_exp.add_argument('--subtitle', default=None, help='可选自定义底栏标题')
    p_exp.add_argument('--live-report', default=None, help='可选 live_battle.mjs 生成的实机战绩 JSON，无缝拼接为一图流')

    p_del = sub.add_parser('deliver', help='标准一键交付（合规门禁 -> 0误差面板 -> 对手组评测 -> 1:1出图）')
    p_del.add_argument('team', help='预设名或 JSON 路径')
    _add_mode_args(p_del)
    p_del.add_argument('--group', default=None, help='可选对手组名（如 top3_02_15_16 或 all6）')
    p_del.add_argument('-n', type=int, default=500, help='每对手单侧模拟场数（总场数=2n）')
    p_del.add_argument('--seed', type=int, default=20261125)
    p_del.add_argument('-o', '--output', default=None, help='输出 PNG 路径（默认写入 exports/）')
    p_del.add_argument('--subtitle', default=None, help='可选自定义底栏标题')

    p_mat = sub.add_parser('matrix', help='N×N 全交叉对称循环赛矩阵 + Nash 均衡 Meta 分析（默认 fair_eq）')
    p_mat.add_argument('--teams', default=None, help='逗号分隔预设名/JSON路径（默认代表流派+实服头部好友）')
    p_mat.add_argument('--group', default=None, help='或指定 account_profile.json 中的对手组名')
    p_mat.add_argument('-n', type=int, default=250, help='每对单侧场数（每对总场数=2n）')
    p_mat.add_argument('--workers', type=int, default=min(10, max(2, (os.cpu_count() or 4) - 2)))
    p_mat.add_argument('--seed', type=int, default=20261125)
    p_mat.add_argument('--out', default=None, help='可选导出 JSON 路径')
    _add_mode_args(p_mat, default_mode='fair_eq')

    p_sw = sub.add_parser('sweep', help='资源敏感度阶梯扫描（如 q7_c2=0,2,4,10 或 rune_lv=10,15,20）')
    p_sw.add_argument('teams', nargs='+', help='待对比阵容列表（预设名或 JSON 路径）')
    p_sw.add_argument('--vs', default='top3_02_15_16', help='对手组名或逗号分隔对手预设名')
    p_sw.add_argument('--param', choices=('q7_c2', 'artifact_lv', 'rune_lv', 'merc_lv', 'bp_mode'), default='q7_c2')
    p_sw.add_argument('--values', default='0,2,4,10', help='逗号分隔阶梯值')
    p_sw.add_argument('-n', type=int, default=200, help='每对手单侧模拟场数')
    p_sw.add_argument('--workers', type=int, default=min(10, max(2, (os.cpu_count() or 4) - 2)))
    p_sw.add_argument('--seed', type=int, default=20261125)
    _add_mode_args(p_sw, default_mode='fair_eq')

    args = parser.parse_args()
    if args.cmd == 'presets':
        cmd_presets(args)
        return
    if args.cmd == 'check':
        cmd_check()
        return

    eng = SimulatorEngine()
    default_mode = eng.profile.get('default_mode', 'server30')
    if hasattr(args, 'mode') and args.mode is None:
        args.mode = default_mode

    if args.cmd == 'profile':
        cmd_profile(eng)
    elif args.cmd == 'find':
        cmd_find(args, eng)
    elif args.cmd == 'info':
        cmd_info(args, eng)
    elif args.cmd == 'vs':
        cmd_vs(args, eng)
    elif args.cmd == 'bench':
        cmd_bench(args, eng)
    elif args.cmd == 'trace':
        cmd_trace(args, eng)
    elif args.cmd == 'export':
        cmd_export(args, eng)
    elif args.cmd == 'deliver':
        cmd_deliver(args, eng)
    elif args.cmd == 'matrix':
        cmd_matrix(args, eng)
    elif args.cmd == 'sweep':
        cmd_sweep(args, eng)


if __name__ == '__main__':
    main()
