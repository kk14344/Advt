#!/usr/bin/env python3
"""Multi-core parallel PvP formation optimizer for 冒险与挖矿 (test-server).

Features:
  - ProcessPoolExecutor (up to 10 persistent workers, each loading slim-cached SimulatorEngine ONCE)
  - 2-stage pruning (n=40 rapid screen -> Top-6 confirm at n=300 -> final n=1000 symmetric = 2000 battles)
  - Flexible constraints:
      --opps 02,03,15,16 (supports short aliases 02/03/04/08/15/16/18, raw UIDs, or friend_* keys)
      --objective floor | avg (maximize min winrate or mean winrate)
      --runes bag | keep | all (0-draw bag runes / keep current equipped runes / allow theoretical runes)
      --max-changes N (optional micro-tuning cap on --base preset for instant deploy)
      --lock 红美铃,芙兰朵露  /  --ban ...
"""

from __future__ import annotations

import argparse
import collections
import concurrent.futures
import copy
import json
import os
import sys
import time
from pathlib import Path
from typing import Any

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from engine import SimulatorEngine

PRESETS_PATH = HERE / 'presets.json'
BAG_PATH = HERE / 'live_bag_runes.json'

OPP_ALIASES = {
    '02': 'friend_73ID6', '测试02': 'friend_73ID6', '73ID6': 'friend_73ID6',
    '03': 'friend_GE0Z2', '测试03': 'friend_GE0Z2', 'GE0Z2': 'friend_GE0Z2',
    '04': 'friend_DAFKJ', '测试04': 'friend_DAFKJ', 'DAFKJ': 'friend_DAFKJ',
    '08': 'friend_70SXF', '测试08': 'friend_70SXF', '70SXF': 'friend_70SXF',
    '15': 'friend_3YPN0', '测试15': 'friend_3YPN0', '3YPN0': 'friend_3YPN0',
    '16': 'friend_NZVHY', '测试16': 'friend_NZVHY', 'NZVHY': 'friend_NZVHY',
    '18': 'friend_P0PLM', '测试18': 'friend_P0PLM', 'P0PLM': 'friend_P0PLM',
}

THEO_EXTRA_RUNES = [
    ('5x66', [[66, 20]]*5),
    ('5x69', [[69, 20]]*5),
    ('5x64', [[64, 20]]*5),
    ('5x67', [[67, 20]]*5),
    ('4x67+1x69', [[67, 20]]*4 + [[69, 20]]),
    ('3x67+2x69', [[67, 20]]*3 + [[69, 20]]*2),
    ('3x64+2x59', [[64, 20]]*3 + [[59, 20]]*2),
    ('3x64+2x69', [[64, 20]]*3 + [[69, 20]]*2),
]

# Worker-local globals (initialized once per worker process!)
_W_ENG: SimulatorEngine | None = None
_W_OPPS: list[tuple[str, dict[str, Any]]] = []
_W_MODE: str = 'server30'
_W_OVERRIDES: dict[str, Any] | None = None


def _worker_init(opps: list[tuple[str, dict[str, Any]]], mode: str, mode_overrides: dict[str, Any] | None = None) -> None:
    global _W_ENG, _W_OPPS, _W_MODE, _W_OVERRIDES
    _W_ENG = SimulatorEngine()
    _W_OPPS = opps
    _W_MODE = mode
    _W_OVERRIDES = mode_overrides


def _worker_eval(task: tuple[Any, dict[str, Any], int, int]) -> tuple[Any, dict[str, float], float, float]:
    tag, team, runs, seed = task
    assert _W_ENG is not None
    res = {
        k: _W_ENG.simulate_series(team, o, runs_each=runs, seed=seed, mode=_W_MODE, mode_overrides=_W_OVERRIDES)['winrate']
        for k, o in _W_OPPS
    }
    vals = list(res.values())
    return tag, res, min(vals), sum(vals) / len(vals)


def resolve_opp_key(token: str, presets: dict[str, Any]) -> str:
    t = token.strip()
    if t in presets:
        return t
    if t in OPP_ALIASES and OPP_ALIASES[t] in presets:
        return OPP_ALIASES[t]
    if f'friend_{t}' in presets:
        return f'friend_{t}'
    raise KeyError(f'Unknown opponent: {token}')


def resolve_merc_tids(eng: SimulatorEngine, csv_str: str | None) -> set[int]:
    if not csv_str:
        return set()
    out: set[int] = set()
    for tok in csv_str.split(','):
        tok = tok.strip()
        if not tok:
            continue
        if tok.isdigit():
            out.add(int(tok))
        else:
            for tid, m in eng.mercs.items():
                if m.get('name') == tok and tid in eng.allowed_tids:
                    out.add(tid)
    return out


def build_bag_rune_sets(bag_counts: dict[str, int]) -> list[tuple[str, list[list[int]]]]:
    avail = {int(k): int(v) for k, v in bag_counts.items() if int(v) > 0}
    cands: list[tuple[str, list[list[int]]]] = []

    def can_make(rids: list[int]) -> bool:
        need = collections.Counter(rids)
        return all(avail.get(r, 0) >= n for r, n in need.items())

    templates = [
        ('5x59', [59]*5),
        ('4x59+1x63', [59]*4 + [63]),
        ('3x59+2x63', [59]*3 + [63]*2),
        ('2x59+3x63', [59]*2 + [63]*3),
        ('1x59+4x63', [59] + [63]*4),
        ('5x63', [63]*5),
        ('4x63+1x69', [63]*4 + [69]),
        ('3x63+2x69', [63]*3 + [69]*2),
        ('2x59+1x63+2x69', [59]*2 + [63] + [69]*2),
        ('1x59+2x63+2x69', [59] + [63]*2 + [69]*2),
        ('3x59+2x69', [59]*3 + [69]*2),
        ('4x66+1x69', [66]*4 + [69]),
        ('3x66+2x69', [66]*3 + [69]*2),
        ('4x66+1x63', [66]*4 + [63]),
        ('3x66+2x63', [66]*3 + [63]*2),
        ('2x66+3x63', [66]*2 + [63]*3),
        ('2x66+1x63+2x69', [66]*2 + [63] + [69]*2),
        ('3x59+2x66', [59]*3 + [66]*2),
        ('3x66+2x75', [66]*3 + [75]*2),
        ('3x63+2x75', [63]*3 + [75]*2),
        ('3x59+2x75', [59]*3 + [75]*2),
    ]
    for name, rids in templates:
        if not avail or can_make(rids):
            cands.append((name, [[r, 20] for r in rids]))
    return cands or [('5x59', [[59, 20]]*5), ('5x63', [[63, 20]]*5)]


def main() -> None:
    ap = argparse.ArgumentParser(description='Multi-core fast PvP formation optimizer')
    ap.add_argument('--opps', required=True, help='Comma-separated opponents (e.g. 02,03,15,16 or friend_3YPN0)')
    ap.add_argument('--mode', choices=('server30', 'max', 'fair_eq'), default='server30')
    ap.add_argument('--objective', choices=('floor', 'avg'), default='floor')
    ap.add_argument('--runes', choices=('bag', 'keep', 'all'), default=None)
    ap.add_argument('--q7-c2', type=int, default=None, help='Override q7_contract2_limit (e.g. 0/2/4)')
    ap.add_argument('--artifact-lv', type=int, default=None, help='Override artifact_lv')
    ap.add_argument('--rune-lv', type=int, default=None, help='Override unified rune_lv')
    ap.add_argument('--merc-lv', type=int, default=None, help='Override merc_level')
    ap.add_argument('--merc-wake', type=int, default=None, help='Override merc_wakeup_cap')
    ap.add_argument('--leader-contract', type=int, default=None, help='Override leader_contract_id')
    ap.add_argument('--equalize-bp', action='store_true', help='Diagnostic: equalize both sides max_bp to mean')
    ap.add_argument('--no-runes', action='store_true', help='Diagnostic: disable runes on both sides')
    ap.add_argument('--mirror-runes', default=None, help='Diagnostic: mirror exact 5 runes on both sides')
    ap.add_argument('--base', default=None, help='Optional starting preset key')
    ap.add_argument('--max-changes', type=int, default=0, help='Max slot edits from --base (0 = unlimited)')
    ap.add_argument('--lock', default=None, help='Comma-separated merc names/IDs that must stay in team')
    ap.add_argument('--ban', default=None, help='Comma-separated merc names/IDs excluded from search')
    ap.add_argument('--target', type=float, default=70.0, help='Target minimum winrate percentage')
    ap.add_argument('--iters', type=int, default=5)
    ap.add_argument('--workers', type=int, default=min(10, max(2, (os.cpu_count() or 4) - 2)))
    ap.add_argument('--save', default=None, help='Preset key to save into presets.json')
    ap.add_argument('--title', default=None, help='Display name for saved preset')
    ap.add_argument('--out', default=None, help='Optional JSON output path')
    args = ap.parse_args()

    mode_overrides: dict[str, Any] = {}
    for attr, key in (
        ('q7_c2', 'q7_contract2_limit'),
        ('artifact_lv', 'artifact_lv'),
        ('rune_lv', 'rune_lv'),
        ('merc_lv', 'merc_level'),
        ('merc_wake', 'merc_wakeup_cap'),
        ('leader_contract', 'leader_contract_id'),
        ('mirror_runes', 'mirror_runes'),
    ):
        val = getattr(args, attr, None)
        if val is not None:
            mode_overrides[key] = val
    if args.equalize_bp:
        mode_overrides['equalize_bp'] = True
    if args.no_runes:
        mode_overrides['no_runes'] = True
    ov = mode_overrides or None

    if args.runes is None:
        args.runes = 'all' if args.mode == 'fair_eq' else 'bag'

    t0 = time.time()
    eng = SimulatorEngine()
    mp = eng.get_mode_profile(args.mode, ov)
    eff_rune_lv = int(mp.get('rune_lv', 20))
    presets = json.loads(PRESETS_PATH.read_text(encoding='utf8'))
    opp_keys = [resolve_opp_key(tok, presets) for tok in args.opps.split(',') if tok.strip()]
    opps = [(k, presets[k]) for k in opp_keys]

    lock_tids = resolve_merc_tids(eng, args.lock)
    ban_tids = resolve_merc_tids(eng, args.ban)

    bag_data = json.loads(BAG_PATH.read_text(encoding='utf8')) if BAG_PATH.exists() else {}
    bag_counts: dict[str, int] = bag_data.get('counts', {})
    if args.no_runes or args.mirror_runes:
        rune_pool = [('fixed_diag', [[59, eff_rune_lv]] * 5)]
    elif args.runes == 'keep':
        eq = bag_data.get('equipped') or []
        eq_list = list(eq.values()) if isinstance(eq, dict) else list(eq)
        if len(eq_list) == 5:
            rune_pool = [('equipped', [[int(r['tid']), int(r.get('lv', eff_rune_lv))] for r in eq_list])]
        else:
            rune_pool = build_bag_rune_sets(bag_counts)
    elif args.runes == 'all':
        rune_pool = build_bag_rune_sets(bag_counts) + THEO_EXTRA_RUNES
    else:
        rune_pool = build_bag_rune_sets(bag_counts)
    if eff_rune_lv != 20 or args.mode == 'fair_eq':
        rune_pool = [(rn, [[r[0], eff_rune_lv] for r in rs]) for rn, rs in rune_pool]

    def score_of(mn: float, av: float) -> tuple[float, float]:
        return (av, mn) if args.objective == 'avg' else (mn, av)

    n_workers = max(1, min(args.workers, os.cpu_count() or 4))
    ov_str = f' ov={ov}' if ov else ''
    print(f'Mode={args.mode}{ov_str} | Opps={opp_keys} | obj={args.objective} | runes={args.runes}({len(rune_pool)}) | workers={n_workers}', flush=True)

    cache: dict[tuple[str, int], tuple[dict[str, float], float, float]] = {}

    def team_sig(tm: dict[str, Any]) -> str:
        return json.dumps([tm.get('weapon'), tm.get('runes'), [(s['id'], s.get('wash', '')) for s in tm['slots']]], separators=(',', ':'))

    with concurrent.futures.ProcessPoolExecutor(
        max_workers=n_workers,
        initializer=_worker_init,
        initargs=(opps, args.mode, ov),
    ) as pool:
        def batch_eval(items: list[tuple[Any, dict[str, Any]]], runs: int, seed: int = 20261125) -> list[tuple[Any, dict[str, float], float, float]]:
            out_map: dict[int, tuple[Any, dict[str, float], float, float]] = {}
            to_run: list[tuple[int, dict[str, Any], int, int]] = []
            sigs: dict[int, str] = {}
            for i, (tag, tm) in enumerate(items):
                sg = team_sig(tm)
                sigs[i] = sg
                ck = (sg, runs)
                if ck in cache:
                    res, mn, av = cache[ck]
                    out_map[i] = (tag, res, mn, av)
                else:
                    to_run.append((i, tm, runs, seed))
            if to_run:
                for i, res, mn, av in pool.map(_worker_eval, to_run, chunksize=max(1, len(to_run) // (n_workers * 2))):
                    cache[(sigs[i], runs)] = (res, mn, av)
                    out_map[i] = (items[i][0], res, mn, av)
            return [out_map[i] for i in range(len(items))]

        # 1. Parallel seed x rune screening
        if args.base:
            seed_keys = [args.base]
        else:
            seed_keys = [k for k in presets if not k.startswith('friend_')]

        seed_jobs: list[tuple[tuple[str, str], dict[str, Any]]] = []
        for pk in seed_keys:
            base = presets[pk]
            if eng.validate_team(base):
                continue
            for rname, runes in rune_pool:
                cand = copy.deepcopy(base)
                cand['runes'] = runes
                seed_jobs.append(((pk, rname), cand))

        screened = batch_eval(seed_jobs, runs=60)
        best_per_seed: dict[str, tuple[tuple[float, float], str, dict[str, Any]]] = {}
        for idx_j, ((pk, rname), res, mn, av) in enumerate(screened):
            sc = score_of(mn, av)
            if pk not in best_per_seed or sc > best_per_seed[pk][0]:
                best_per_seed[pk] = (sc, rname, seed_jobs[idx_j][1])

        top_seeds = sorted(best_per_seed.items(), key=lambda kv: kv[1][0], reverse=True)[:4]
        conf_jobs = [((pk, rname), tm) for pk, (_, rname, tm) in top_seeds]
        conf_res = batch_eval(conf_jobs, runs=250)
        ranked = sorted(
            [(score_of(mn, av), pk, rname, conf_jobs[i][1], res, mn, av) for i, ((pk, rname), res, mn, av) in enumerate(conf_res)],
            reverse=True,
            key=lambda x: x[0],
        )
        for _, pk, rname, _, _, mn, av in ranked:
            print(f'  TOP SEED {pk:26s} [{rname:14s}] min={mn*100:5.1f}% avg={av*100:5.1f}%', flush=True)

        cur = copy.deepcopy(ranked[0][3])
        cur_res, cur_mn, cur_av = ranked[0][4], ranked[0][5], ranked[0][6]
        cur_sc = score_of(cur_mn, cur_av)
        base_orig_slots = [(s['id'], s.get('wash', '')) for s in cur['slots']]
        changes_used = 0

        # Focused meta candidate pool
        cand_tids = sorted({
            t for t in eng.allowed_tids
            if t not in ban_tids and (
                t >= 88000001 or any(
                    s['id'] == t for pk in presets for s in presets[pk].get('slots', []) if s.get('id', 0) < 99000001
                )
            )
        })

        # 2. Parallel weapon sweep W1..W12
        w_jobs = [(w, {**cur, 'weapon': w}) for w in range(1, 13) if w != cur.get('weapon')]
        if w_jobs:
            w_res = batch_eval(w_jobs, runs=200)
            w_best = max(w_res, key=lambda x: score_of(x[2], x[3]))
            if score_of(w_best[2], w_best[3]) > (cur_sc[0] + 0.004, cur_sc[1]):
                cand_w = {**cur, 'weapon': w_best[0]}
                _, r2, mn2, av2 = batch_eval([('w', cand_w)], runs=300)[0]
                if score_of(mn2, av2) > (cur_sc[0] + 0.003, cur_sc[1]):
                    cur, cur_res, cur_mn, cur_av = cand_w, r2, mn2, av2
                    cur_sc = score_of(cur_mn, cur_av)
                    print(f'  weapon->W{w_best[0]}: min={cur_mn*100:.1f}% avg={cur_av*100:.1f}%', flush=True)

        # 3. Parallel multi-iteration coordinate hill-climb (2-stage pruned!)
        for it in range(args.iters):
            improved = False
            # 3a. Parallel rune sweep
            if len(rune_pool) > 1:
                r_jobs = [(rn, {**cur, 'runes': runes}) for rn, runes in rune_pool if runes != cur['runes']]
                if r_jobs:
                    r_evals = batch_eval(r_jobs, runs=200)
                    r_best = max(r_evals, key=lambda x: score_of(x[2], x[3]))
                    if score_of(r_best[2], r_best[3]) > (cur_sc[0] + 0.004, cur_sc[1]):
                        cand_r = next(tm for rn, tm in r_jobs if rn == r_best[0])
                        _, r2, mn2, av2 = batch_eval([('r', cand_r)], runs=300)[0]
                        if score_of(mn2, av2) > (cur_sc[0] + 0.003, cur_sc[1]):
                            cur, cur_res, cur_mn, cur_av = cand_r, r2, mn2, av2
                            cur_sc = score_of(cur_mn, cur_av)
                            print(f'  it{it} rune->{r_best[0]}: min={cur_mn*100:.1f}% avg={cur_av*100:.1f}%', flush=True)
                            improved = True

            # 3b. Parallel slot replacement (respecting --max-changes and --lock)
            if args.max_changes <= 0 or changes_used < args.max_changes:
                seen_slots, seen_keys = [], set()
                for idx in range(1, len(cur['slots'])):
                    sl = cur['slots'][idx]
                    if sl['id'] in lock_tids:
                        continue
                    k = (sl['id'], sl.get('wash', ''))
                    if k not in seen_keys:
                        seen_keys.add(k)
                        seen_slots.append(idx)

                stage1_jobs: list[tuple[tuple[int, str, str, int], dict[str, Any]]] = []
                for idx in seen_slots:
                    old_sl = cur['slots'][idx]
                    w = old_sl.get('wash', 'speed') or 'speed'
                    for ntid in cand_tids:
                        if ntid == old_sl['id']:
                            continue
                        if sum(1 for s in cur['slots'] if s['id'] == ntid) >= eng.caps[ntid]:
                            continue
                        cand = copy.deepcopy(cur)
                        cand['slots'][idx] = {'id': ntid, 'name': eng.mercs[ntid]['name'], 'wash': w}
                        if eng.validate_team(cand):
                            continue
                        stage1_jobs.append(((idx, old_sl['name'], eng.mercs[ntid]['name'], ntid), cand))

                s1_res = batch_eval(stage1_jobs, runs=40)
                s1_res.sort(key=lambda x: score_of(x[2], x[3]), reverse=True)
                # Stage 2: test top 6 candidates across speed/authority/defense washes at runs=300
                s2_jobs: list[tuple[tuple[int, str, str, str], dict[str, Any]]] = []
                for (idx, oname, nname, ntid), _, _, _ in s1_res[:6]:
                    for w in ('speed', 'authority', 'defense'):
                        cand = copy.deepcopy(cur)
                        cand['slots'][idx] = {'id': ntid, 'name': nname, 'wash': w}
                        s2_jobs.append(((idx, oname, nname, w), cand))
                if s2_jobs:
                    s2_res = batch_eval(s2_jobs, runs=300)
                    best_i = max(range(len(s2_res)), key=lambda i: score_of(s2_res[i][2], s2_res[i][3]))
                    (idx, oname, nname, w), r2, mn2, av2 = s2_res[best_i]
                    if score_of(mn2, av2) > (cur_sc[0] + 0.003, cur_sc[1]):
                        cur = s2_jobs[best_i][1]
                        cur_res, cur_mn, cur_av = r2, mn2, av2
                        cur_sc = score_of(cur_mn, cur_av)
                        changes_used = sum(
                            1 for a, b in zip(base_orig_slots, [(s['id'], s.get('wash', '')) for s in cur['slots']]) if a != b
                        )
                        print(f'  it{it} -{oname}+{nname}({w[0]}): min={cur_mn*100:.1f}% avg={cur_av*100:.1f}%', flush=True)
                        improved = True

            if not improved or cur_mn >= 0.996:
                break

        # 4. Advisory check for non-bag runes ONLY if bag runes missed --target
        if args.runes == 'bag' and cur_mn * 100 < args.target:
            th_jobs = [(rn, {**cur, 'runes': runes}) for rn, runes in THEO_EXTRA_RUNES]
            th_res = batch_eval(th_jobs, runs=400)
            th_best = max(th_res, key=lambda x: score_of(x[2], x[3]))
            if th_best[2] > cur_mn + 0.02:
                print(
                    f'\n[RUNE ADVISORY] 背包现有符文保底 {cur_mn*100:.1f}% < 目标 {args.target:.1f}%；'
                    f'若抽新符文 {th_best[0]} 可升至保底 {th_best[2]*100:.1f}% (均胜 {th_best[3]*100:.1f}%)',
                    flush=True,
                )

        # 5. Final 2000-battle symmetric verification (runs_each=1000)
        _, final_res, final_mn, final_av = batch_eval([('final', cur)], runs=1000)[0]

    inf = eng.inspect_team(cur, mode=args.mode, mode_overrides=ov)
    summary = ' '.join(f'{k}:{final_res[k]*100:.1f}%' for k in opp_keys)
    print(
        f'\nFINAL(2000): {summary} | min={final_mn*100:.2f}% avg={final_av*100:.2f}% | '
        f'W{cur["weapon"]} base={inf["base_stats"]} runes={[r[0] for r in cur["runes"]]} ({time.time()-t0:.1f}s)',
        flush=True,
    )

    if args.save:
        cur['name'] = args.title or f'{args.save} ({final_mn*100:.1f}%)'
        cur['note'] = f'fast_opt({n_workers}c) {args.mode}: {summary} (2000场对称, min={final_mn*100:.2f}%, avg={final_av*100:.2f}%)'
        presets[args.save] = cur
        PRESETS_PATH.write_text(json.dumps(presets, ensure_ascii=False, indent=2), encoding='utf8')
        print(f'Saved preset: {args.save}')
    if args.out:
        Path(args.out).write_text(
            json.dumps({'team': cur, 'sim': final_res, 'min': final_mn, 'avg': final_av, 'elapsed_s': round(time.time() - t0, 2)}, ensure_ascii=False, indent=2),
            encoding='utf8',
        )


if __name__ == '__main__':
    main()
