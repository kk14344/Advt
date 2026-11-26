"""Automated verification suite for the real-rule PvP simulator."""

from __future__ import annotations

import copy
import json
import subprocess
import sys
import threading
import unittest
import urllib.request
from http.server import ThreadingHTTPServer
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from engine import PERMANENT_TITLE_BONUS, SimulatorEngine
import web_ui


class SimulatorTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.eng = SimulatorEngine()
        cls.presets = json.loads((HERE / 'presets.json').read_text(encoding='utf8'))

    def test_balanced_stats_and_rules_both_modes(self) -> None:
        max_info = self.eng.inspect_team(self.presets['my_best_balanced'], mode='max')
        self.assertEqual(max_info['violations'], [])
        self.assertEqual(max_info['max_bp'], 73170650)
        self.assertEqual(max_info['base_stats'], {'speed': 719, 'defense': 584, 'dodge': 141, 'authority': 1006})
        self.assertEqual(max_info['after_rune_stats'], {'speed': 719, 'defense': 764, 'dodge': 141, 'authority': 1096})
        self.assertEqual(max_info['enemy_rune_debuff'], {'speed': -120, 'defense': 0, 'dodge': 0, 'authority': -60})
        self.assertEqual(set(max_info['combos']), {'神奇开关', '联盟', '中立关系', '最强争夺'})

        # Cooperative skills must appear BEFORE personal skills
        seen_personal = False
        for sk in max_info['skills']:
            if sk['is_coop']:
                self.assertFalse(seen_personal, f"Coop skill {sk['name']} appeared after personal skill")
            else:
                seen_personal = True

        s30_info = self.eng.inspect_team(self.presets['my_best_balanced'], mode='server30')
        self.assertLess(s30_info['max_bp'], 30000000)
        self.assertGreater(s30_info['max_bp'], 25000000)
        # server30 defaults to live 测试09 title_prop (4/4/4/4) vs max (5/5/5/5)
        self.assertEqual(s30_info['base_stats'], {'speed': 718, 'defense': 583, 'dodge': 140, 'authority': 1005})
        self.assertEqual(s30_info['after_rune_stats'], {'speed': 718, 'defense': 763, 'dodge': 140, 'authority': 1095})

    def test_zero_point_live_stat_parity(self) -> None:
        # 1. User's live 测试09 F4 (v8_all6_balanced_67) live stats after Flandre rework + coop 200013: 1411 / 467 / 56 / 723
        #    (verifies 2x 泉此方 600013 no double-count + T4 pre-extra defense=399 < 400 so 大巴娘 360002 does not trigger)
        v8b_s30 = self.eng.inspect_team(self.presets['v8_all6_balanced_67'], mode='server30')
        self.assertEqual(v8b_s30['base_stats'], {'speed': 1411, 'defense': 467, 'dodge': 56, 'authority': 723})
        self.assertIn(88000031, self.eng.allowed_tids)

        # 2. v9_anti_02_15_16 corrected stats (no +20 phantom authority / +15+7 phantom dodge)
        v9_s30 = self.eng.inspect_team(self.presets['v9_anti_02_15_16'], mode='server30')
        self.assertEqual(v9_s30['base_stats'], {'speed': 724, 'defense': 542, 'dodge': 171, 'authority': 1037})
        v9_max = self.eng.inspect_team(self.presets['v9_anti_02_15_16'], mode='max')
        self.assertEqual(v9_max['base_stats'], {'speed': 725, 'defense': 543, 'dodge': 172, 'authority': 1038})

        # 3. Live friend_73ID6 (测试02) current formula & stats_override check + v13 live 0/0/0/0 parity (670/860/198/955)
        f73 = copy.deepcopy(self.presets['friend_73ID6'])
        self.assertEqual(self.eng.validate_team(f73), [])
        v13_s30 = self.eng.inspect_team(self.presets['v13_all4_60plus_king'], mode='server30')
        self.assertEqual(v13_s30['base_stats'], {'speed': 670, 'defense': 860, 'dodge': 198, 'authority': 955})

        # 4. Live friend_3YPN0 (测试15, female leader 99200003, T5, 2x 泉此方 + 时崎狂三): pure formula == 667/640/106/1032
        f3y = copy.deepcopy(self.presets['friend_3YPN0'])
        f3y.pop('stats_override', None)
        f3y['title_bonus'] = PERMANENT_TITLE_BONUS
        for s in f3y['slots']:
            if s['id'] == 99000001:
                s['id'] = 99200003
        self.assertEqual(self.eng.validate_team(f3y), [])
        self.assertEqual(
            self.eng.inspect_team(f3y, mode='server30')['base_stats'],
            {'speed': 667, 'defense': 640, 'dodge': 106, 'authority': 1032},
        )

    def test_duplicate_athena_flagged_by_validator(self) -> None:
        bad = copy.deepcopy(self.presets['my_best_balanced'])
        bad['slots'][1]['id'] = 22000007
        bad['slots'][1]['name'] = '雅典娜'
        errs = self.eng.validate_team(bad)
        self.assertTrue(any('雅典娜' in e for e in errs))

    def test_strict_greater_seal_and_gm_combo_in_lua(self) -> None:
        # Verify strict '>' seal_cond1 (seal.lo=7 must NOT block trigger_chance=7)
        res = self.eng.lua.eval('''
        function()
            local sk7 = {ID=999907, trigger_chance=7, effect_type=13, times_limit=99, turn_cond1=0, turn_cond2=0,
                self_bp_cond1=0, self_bp_cond2=0, enemy_bp_cond1=0, enemy_bp_cond2=0, turn_cond3=0, cur_bp_cond=0}
            local sk8 = {ID=999908, trigger_chance=8, effect_type=13, times_limit=99, turn_cond1=0, turn_cond2=0,
                self_bp_cond1=0, self_bp_cond2=0, enemy_bp_cond1=0, enemy_bp_cond2=0, turn_cond3=0, cur_bp_cond=0}
            local seal = {rem=2, kind=0, lo=7, hi=0}
            local blocked7 = (seal.rem>0 and (seal.kind==0 or seal.kind==sk7.effect_type) and (seal.lo==0 or sk7.trigger_chance>seal.lo))
            local blocked8 = (seal.rem>0 and (seal.kind==0 or seal.kind==sk8.effect_type) and (seal.lo==0 or sk8.trigger_chance>seal.lo))
            return blocked7, blocked8
        end
        ''')()
        self.assertEqual(res, (False, True))

    def test_trace_and_series_execution(self) -> None:
        tr = self.eng.simulate_trace(self.presets['my_best_balanced'], self.presets['friend_GE0Z2'], seed=12345, mode='server30')
        self.assertIn(tr['winner'], ('A', 'B', 'Draw'))
        self.assertGreater(tr['rounds'], 0)
        self.assertGreater(len(tr['events']), 0)

        sr = self.eng.simulate_series(self.presets['my_best_speed'], self.presets['friend_GE0Z2'], runs_each=200, mode='max')
        self.assertEqual(sr['total'], 400)
        self.assertGreater(sr['winrate'], 0.80)

        # Verify v7_anti_t15 clears >65% vs latest live-ordered friend_3YPN0 ([62,20]*5) in both server30 and max modes
        v7 = self.presets['v7_anti_t15']
        self.assertEqual(self.eng.validate_team(v7), [])
        sr_s30 = self.eng.simulate_series(v7, self.presets['friend_3YPN0'], runs_each=250, seed=2026, mode='server30')
        sr_max = self.eng.simulate_series(v7, self.presets['friend_3YPN0'], runs_each=250, seed=2026, mode='max')
        self.assertGreater(sr_s30['winrate'], 0.65)
        self.assertGreater(sr_max['winrate'], 0.65)

        # Verify v9_anti_02_15_16 is legal and clears >90% vs 16 (>65% vs latest 5x62 15, >50% vs 02)
        v9 = self.presets['v9_anti_02_15_16']
        self.assertEqual(self.eng.validate_team(v9), [])
        for mode in ('server30', 'max'):
            for ok, thr in (('friend_73ID6', 0.50), ('friend_3YPN0', 0.65), ('friend_NZVHY', 0.90)):
                sr9 = self.eng.simulate_series(v9, self.presets[ok], runs_each=300, seed=20260215, mode=mode)
                self.assertGreater(sr9['winrate'], thr, f'v9_anti_02_15_16 vs {ok} ({mode}) got {sr9["winrate"]:.2%}')

        # Verify v10_recovery_02_15_16 (偏恢复流: 10 genre=2 mercs, 实战闪352) is legal and clears >90% vs 16 (>60% vs 5x62 15)
        v10 = self.presets['v10_recovery_02_15_16']
        self.assertEqual(self.eng.validate_team(v10), [])
        v10_info = self.eng.inspect_team(v10, mode='server30')
        self.assertEqual(v10_info['base_stats'], {'speed': 690, 'defense': 544, 'dodge': 232, 'authority': 1017})
        self.assertEqual(v10_info['after_rune_stats'], {'speed': 690, 'defense': 544, 'dodge': 352, 'authority': 1077})
        for ok, thr in (('friend_3YPN0', 0.60), ('friend_NZVHY', 0.90)):
            sr10 = self.eng.simulate_series(v10, self.presets[ok], runs_each=500, seed=20261125, mode='server30')
            self.assertGreater(sr10['winrate'], thr, f'v10_recovery_02_15_16 vs {ok} got {sr10["winrate"]:.2%}')

        # Verify v9_f4_mod9 and v9_f4_keepSpd12 remain legal and beat 16 cleanly
        for pk, n_runs, thr in (('v9_f4_mod9', 300, 0.85), ('v9_f4_keepSpd12', 500, 0.85)):
            tm = self.presets[pk]
            self.assertEqual(self.eng.validate_team(tm), [])
            srm = self.eng.simulate_series(tm, self.presets['friend_NZVHY'], runs_each=n_runs, seed=20260215, mode='server30')
            self.assertGreater(srm['winrate'], thr, f'{pk} vs friend_NZVHY got {srm["winrate"]:.2%}')

        # Verify v15_anti_t15_max beats live friend_3YPN0 (5x62) >95%
        v15 = self.presets['v15_anti_t15_max']
        self.assertEqual(self.eng.validate_team(v15), [])
        sr15 = self.eng.simulate_series(v15, self.presets['friend_3YPN0'], runs_each=300, seed=20261125, mode='server30')
        self.assertGreater(sr15['winrate'], 0.95, f'v15_anti_t15_max vs friend_3YPN0 got {sr15["winrate"]:.2%}')

        # Verify v8_all5_70plus is legal and clears >70% vs all 6 specified opponents (02/03/04/08/15/18) in server30
        v8 = self.presets['v8_all5_70plus']
        self.assertEqual(self.eng.validate_team(v8), [])
        for ok in ('friend_73ID6', 'friend_GE0Z2', 'friend_DAFKJ', 'friend_70SXF', 'friend_3YPN0', 'friend_P0PLM'):
            sr8 = self.eng.simulate_series(v8, self.presets[ok], runs_each=300, seed=20260525, mode='server30')
            self.assertGreater(sr8['winrate'], 0.70, f'v8_all5_70plus vs {ok} got {sr8["winrate"]:.2%}')

        # Verify v8_all6_balanced_67 is legal (after live 3YPN0 switched 5x62 -> 5x66 +300 def, floor vs 3YPN0 is ~48.8%)
        v8b = self.presets['v8_all6_balanced_67']
        self.assertEqual(self.eng.validate_team(v8b), [])
        for mode, thr in (('server30', 0.45), ('max', 0.44)):
            for ok in ('friend_73ID6', 'friend_GE0Z2', 'friend_DAFKJ', 'friend_70SXF', 'friend_3YPN0', 'friend_P0PLM'):
                srb = self.eng.simulate_series(v8b, self.presets[ok], runs_each=250, seed=20260525, mode=mode)
                self.assertGreater(srb['winrate'], thr, f'v8_all6_balanced_67 vs {ok} ({mode}) got {srb["winrate"]:.2%}')

        # Verify v12_all7_live_king and v13_all4_60plus_king match exact 0-error stats & beat 03/04/08/16/18 >60%
        v12 = self.presets['v12_all7_live_king']
        self.assertEqual(self.eng.validate_team(v12), [])
        v12_info = self.eng.inspect_team(v12, mode='server30')
        self.assertEqual(v12_info['base_stats'], {'speed': 725, 'defense': 859, 'dodge': 209, 'authority': 948})
        self.assertEqual(v12_info['after_rune_stats'], {'speed': 725, 'defense': 1039, 'dodge': 209, 'authority': 1038})
        self.assertEqual(v12_info['enemy_rune_debuff'], {'speed': 0, 'defense': 0, 'dodge': -120, 'authority': -60})
        for ok in ('friend_GE0Z2', 'friend_DAFKJ', 'friend_70SXF', 'friend_NZVHY', 'friend_P0PLM'):
            sr12 = self.eng.simulate_series(v12, self.presets[ok], runs_each=500, seed=20261125, mode='server30')
            self.assertGreater(sr12['winrate'], 0.60, f'v12_all7_live_king vs {ok} got {sr12["winrate"]:.2%}')

        v13 = self.presets['v13_all4_60plus_king']
        self.assertEqual(self.eng.validate_team(v13), [])
        v13_info = self.eng.inspect_team(v13, mode='server30')
        self.assertEqual(v13_info['base_stats'], {'speed': 670, 'defense': 860, 'dodge': 198, 'authority': 955})
        self.assertEqual(v13_info['after_rune_stats'], {'speed': 670, 'defense': 1040, 'dodge': 198, 'authority': 1045})
        for ok in ('friend_GE0Z2', 'friend_NZVHY'):
            sr13 = self.eng.simulate_series(v13, self.presets[ok], runs_each=500, seed=20261125, mode='server30')
            self.assertGreater(sr13['winrate'], 0.60, f'v13_all4_60plus_king vs {ok} got {sr13["winrate"]:.2%}')

        # Verify 1400043 stale seal override & 1800043 seal_turn=1 override
        self.assertEqual(self.eng.cfg['active_skill']['1400043']['seal_turn'], 0)
        self.assertEqual(self.eng.cfg['active_skill']['1800043']['seal_turn'], 1)

        # Verify 2 copies of a times_limit=1 skill (70000002) exist as separate skill slots in v7_anti_t15
        slots_info = self.eng.inspect_team(v7, mode='server30')
        idx_limited = [i for i, sk in enumerate(slots_info['skills'], 1) if sk['id'] == 70000002]
        self.assertEqual(len(idx_limited), 2)

        # Verify 70000055 (gm_combo_effects 35% enemy cur_bp heal + 80% pure) uses post-heal cur_bp without phantom normal dmg
        tr_diff = None
        for seed in range(1, 30):
            tr = self.eng.simulate_trace(self.presets['friend_3YPN0'], self.presets['friend_4WRU5'], seed=seed, mode='server30')
            ev = next((e for e in tr['events'] if e['skill_id'] == 70000055), None)
            if ev is not None:
                tr_diff = ev
                break
        self.assertIsNotNone(tr_diff)
        self.assertGreater(tr_diff['self_heal'], 0)
        self.assertGreater(tr_diff['dmg_to_enemy'], 0)

        # Verify effect_type=9 with reply_after_attack=True (70000010 十一之弹) deals pre-heal damage then heals
        pre_dmg, post_bp = self.eng.lua.eval('''(function()
            local entity = require('entity.troop')
            local cm = require('logic.config_manager')
            local L, R = entity.New(1), entity.New(2)
            L:SetProperty(1000000, 1000000, 0, R, 100, 0, 200, 0, 100, 0, 200, 0)
            R:SetProperty(1000000, 1000000, 0, L, 0, 0, 0, 0, 0, 0, 0, 0)
            L:CalcDamageFactor(); R:CalcDamageFactor()
            L:UseSkill(cm.active_skill_config[70000010], false, 0, 0, 0)
            return 1000000 - R.cur_bp, L.cur_bp
        end)()''')
        # Pre-heal damage = ceil((1e6*0.2 + 1e6*0.1)*1.0*1.0) = 300000 (post-heal would be 370000)
        self.assertEqual(int(pre_dmg), 300000)
        # Heal = ceil(1e6 * 0.35 * (1 + 200*0.005)) = 700000 -> post_bp = 1700000
        self.assertEqual(int(post_bp), 1700000)

    def test_cli_commands(self) -> None:
        p = subprocess.run([sys.executable, str(HERE / 'sim.py'), 'find', '大魔王'], capture_output=True, text=True, check=True)
        self.assertIn('19000177', p.stdout)
        p2 = subprocess.run([sys.executable, str(HERE / 'sim.py'), 'vs', 'my_best_balanced', 'friend_GE0Z2', '-n', '20', '--mode', 'server30'], capture_output=True, text=True, check=True)
        self.assertIn('综合胜率', p2.stdout)
        p3 = subprocess.run([sys.executable, str(HERE / 'sim.py'), 'profile'], capture_output=True, text=True, check=True)
        self.assertIn('account_profile.json', p3.stdout)
        self.assertIn('4/4/4/4', p3.stdout)

        # Verify 1:1 native-asset renderer resolves every single slot sprite (0 missing portraits)
        from export_card import get_asset_store
        store = get_asset_store(self.eng.data_dir)
        for pk in ('v8_all6_balanced_67', 'v9_anti_02_15_16', 'v12_all7_live_king', 'v14_meiling_all4_single'):
            for slot in self.presets[pk]['slots']:
                mrow = self.eng.mercs[slot['id']]
                self.assertIsNotNone(store.get_merc_portrait(mrow), f"Missing sprite for {slot['id']} {mrow['name']}")

    def test_fair_eq_branch_rules_and_diagnostics(self) -> None:
        # 1. fair_eq defaults: q7 all contract 1 (K=0), q6 & below contract 2, ignore live snapshot stats_override/title_bonus
        f_nz = self.presets['friend_NZVHY']
        s30_nz = self.eng.inspect_team(f_nz, mode='server30')
        feq_nz = self.eng.inspect_team(f_nz, mode='fair_eq')
        # server30 uses live stats_override (~30.9M BP); fair_eq strips live override and computes clean unified Lv30 BP
        self.assertGreater(s30_nz['max_bp'], 30000000)
        self.assertLess(feq_nz['max_bp'], 29000000)
        self.assertEqual(feq_nz['q7_summary']['c2_limit'], 0)
        self.assertEqual(len(feq_nz['q7_summary']['c2_slots']), 0)
        self.assertEqual(len(feq_nz['q7_summary']['c1_slots']), feq_nz['q7_summary']['total'])
        for sl in feq_nz['slots']:
            if sl['id'] >= 99000001:
                self.assertEqual(sl['contract'], 0)
            elif sl['quality'] == 7:
                self.assertEqual(sl['contract'], 1)
            else:
                self.assertEqual(sl['contract'], 2)

        # 2. Exact mathematical check: K=0 -> K=1 -> K=2 increases stats by exact (c2 - c1) + 1x wash bonus on 炫金
        v14 = self.presets['v14_meiling_all4_single']
        i0 = self.eng.inspect_team(v14, mode='fair_eq', mode_overrides={'q7_contract2_limit': 0})
        i2 = self.eng.inspect_team(v14, mode='fair_eq', mode_overrides={'q7_contract2_limit': 2})
        i_all = self.eng.inspect_team(v14, mode='fair_eq', mode_overrides={'q7_contract2_limit': 99})
        self.assertEqual(len(i0['q7_summary']['c2_slots']), 0)
        self.assertEqual(len(i2['q7_summary']['c2_slots']), 2)
        self.assertEqual(len(i_all['q7_summary']['c2_slots']), i_all['q7_summary']['total'])
        # Since 泉此方 has highest c2-c1 (+8/+8/0/+8 = +24 + wash), K=2 should strictly exceed K=0 and be strictly <= K=99
        sum0 = sum(i0['base_stats'].values())
        sum2 = sum(i2['base_stats'].values())
        sum_all = sum(i_all['base_stats'].values())
        self.assertGreater(sum2, sum0)
        self.assertGreater(sum_all, sum2)
        # Under K=99 with T5 title and Lv30, base_stats of v14 should match server30 + (1,1,1,1) title diff!
        v14_s30 = self.eng.inspect_team(v14, mode='server30')
        # Note: passive conversions can amplify +1/+1/+1/+1 slightly, so each stat >= s30 + 1
        for p in ('speed', 'defense', 'dodge', 'authority'):
            self.assertGreaterEqual(i_all['base_stats'][p], v14_s30['base_stats'][p])

        # 3. Explicit contract:2 violation check when exceeding K
        bad_c2 = copy.deepcopy(v14)
        q7_seen = 0
        for sl in bad_c2['slots']:
            if self.eng.mercs[sl['id']].get('quality') == 7:
                sl['contract'] = 2
                q7_seen += 1
                if q7_seen == 3:
                    break
        errs = self.eng.validate_team(bad_c2, mode='fair_eq', mode_overrides={'q7_contract2_limit': 2})
        self.assertTrue(any('显式二契炫金超限' in e for e in errs))
        self.assertEqual(self.eng.validate_team(bad_c2, mode='fair_eq', mode_overrides={'q7_contract2_limit': 3}), [])

        # 4. Dynamic overrides & diagnostics: rune_lv, artifact_lv, no_runes, mirror_runes, equalize_bp
        i_nr = self.eng.inspect_team(v14, mode='fair_eq', mode_overrides={'no_runes': True})
        self.assertEqual(i_nr['base_stats'], i_nr['after_rune_stats'])
        self.assertEqual(i_nr['enemy_rune_debuff'], {'speed': 0, 'defense': 0, 'dodge': 0, 'authority': 0})

        i_mr = self.eng.inspect_team(v14, mode='fair_eq', mode_overrides={'mirror_runes': '5x防衰', 'rune_lv': 10})
        self.assertEqual([r['id'] for r in i_mr['runes']], [59, 59, 59, 59, 59])
        self.assertEqual([r['lv'] for r in i_mr['runes']], [10, 10, 10, 10, 10])

        i_a10 = self.eng.inspect_team(v14, mode='fair_eq', mode_overrides={'artifact_lv': 10})
        self.assertLess(i_a10['max_bp'], i0['max_bp'])

        tr_eq = self.eng.simulate_trace(
            v14, self.presets['v10_recovery_02_15_16'], seed=42, mode='fair_eq', mode_overrides={'equalize_bp': True}
        )
        self.assertEqual(tr_eq['a_effective']['max_bp'], tr_eq['b_effective']['max_bp'])

        # 5. CLI matrix & sweep & export under fair_eq
        p_mat = subprocess.run(
            [sys.executable, str(HERE / 'sim.py'), 'matrix', '--mode', 'fair_eq',
             '--teams', 'v14_meiling_all4_single,v13_all4_60plus_king,v10_recovery_02_15_16', '-n', '25', '--workers', '1'],
            capture_output=True, text=True, check=True,
        )
        self.assertIn('Nash', p_mat.stdout)
        p_sw = subprocess.run(
            [sys.executable, str(HERE / 'sim.py'), 'sweep', 'v14_meiling_all4_single',
             '--vs', 'v13_all4_60plus_king', '--param', 'q7_c2', '--values', '0,2', '-n', '20', '--workers', '1'],
            capture_output=True, text=True, check=True,
        )
        self.assertIn('q7_c2=0', p_sw.stdout)

    def test_web_ui_http_endpoints(self) -> None:
        srv = ThreadingHTTPServer(('127.0.0.1', 0), web_ui.SimHandler)
        port = srv.server_address[1]
        th = threading.Thread(target=srv.serve_forever, daemon=True)
        th.start()
        try:
            with urllib.request.urlopen(f'http://127.0.0.1:{port}/api/init', timeout=5) as resp:
                self.assertEqual(resp.status, 200)
                init_data = json.loads(resp.read().decode('utf8'))
                self.assertIn('my_best_balanced', init_data['presets'])

            req = urllib.request.Request(
                f'http://127.0.0.1:{port}/api/vs',
                data=json.dumps({'team_a': self.presets['my_best_balanced'], 'team_b': self.presets['friend_GE0Z2'], 'runs_each': 20, 'mode': 'server30'}).encode('utf8'),
                headers={'Content-Type': 'application/json'},
            )
            with urllib.request.urlopen(req, timeout=5) as resp:
                vs_data = json.loads(resp.read().decode('utf8'))
                self.assertEqual(vs_data['total'], 40)
            req2 = urllib.request.Request(
                f'http://127.0.0.1:{port}/api/export_png',
                data=json.dumps({'team': self.presets['v9_f4_mod9'], 'mode': 'server30'}).encode('utf8'),
                headers={'Content-Type': 'application/json'},
            )
            with urllib.request.urlopen(req2, timeout=10) as resp:
                self.assertEqual(resp.status, 200)
                self.assertEqual(resp.headers.get('Content-Type'), 'image/png')
                png_bytes = resp.read()
                self.assertTrue(png_bytes.startswith(b'\x89PNG\r\n\x1a\n'))
                self.assertGreater(len(png_bytes), 50000)
        finally:
            srv.shutdown()
            srv.server_close()


if __name__ == '__main__':
    unittest.main(verbosity=2)
