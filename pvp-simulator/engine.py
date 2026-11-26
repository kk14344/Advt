"""Verified real-server PvP simulator engine for 冒险与挖矿 (test-server rules).

Implements the live-verified combat & team-building mechanics:
1. Identical level-20 runes allowed and stacked linearly (including 5x duplicates).
2. Cooperative skills registered BEFORE personal active skills (never truncated by >100% personal active pools).
3. seal_turn decrements once per enemy action, and seal_cond1 uses strict '>' threshold.
4. Executes GM combo effects (gm_combo_effects), post-attack total heals (gm_after_attack_total_heal_percent),
   and passive stat conversion BEFORE rune additions.
5. Supports both full maxed mode ('max': Lv100 + max_wakeup) and live test-server parity mode
   ('server30': protagonist Lv100/Wake8, mercenaries Lv30/Wake<=5).
"""

from __future__ import annotations

import collections
import functools
import json
import math
import os
import re
from pathlib import Path
from typing import Any

from lupa import LuaRuntime

DEFAULT_DATA_DIR = Path(os.environ.get('PVP_DATA_DIR', r'F:\codex1\tmp\test-server-pvp'))
DEFAULT_PROFILE_PATH = Path(__file__).resolve().parent / 'account_profile.json'
LEADER_TID = 99000001
PROPS = ('speed', 'defense', 'dodge', 'authority')
WASH_ALIASES = {
    'speed': 'speed', '先攻': 'speed', 'spd': 'speed',
    'defense': 'defense', '防御': 'defense', 'def': 'defense',
    'dodge': 'dodge', '闪避': 'dodge', 'dge': 'dodge',
    'authority': 'authority', '王者': 'authority', 'auth': 'authority',
    '': '', '无': '', 'none': '',
}

# 17 permanent account titles (maxed account, e.g. 测试02/测试15) vs live 测试09 (16 titles, missing title #21 +1 all)
PERMANENT_TITLE_BONUS = {'bp': 10660, 'speed': 5, 'defense': 5, 'dodge': 5, 'authority': 5}
SERVER30_TITLE_BONUS = {'bp': 6860, 'speed': 4, 'defense': 4, 'dodge': 4, 'authority': 4}
DEFAULT_CULTIVATION = [[50, -50]] * 6


def is_leader_tid(tid: int) -> bool:
    return tid >= LEADER_TID


def _round_rune_val(val: float) -> int:
    if val > 0:
        return math.ceil(val)
    if val < 0:
        return math.floor(val)
    return 0


def _read_json(data_dir: Path, name: str) -> Any:
    return json.loads((data_dir / f'{name}.json').read_text(encoding='utf8'))


class SimulatorEngine:
    """Thread-local / single-process Lua-backed PvP simulator."""

    def __init__(
        self,
        data_dir: str | Path = DEFAULT_DATA_DIR,
        profile_path: str | Path | None = DEFAULT_PROFILE_PATH,
    ) -> None:
        self.data_dir = Path(data_dir)
        self.profile_path = Path(profile_path) if profile_path else None
        self.profile = self._load_profile()
        self._load_configs()
        self._init_lua()

    def _load_profile(self) -> dict[str, Any]:
        default_profile: dict[str, Any] = {
            'profile_name': 'default',
            'default_mode': 'server30',
            'leader_tid': LEADER_TID,
            'server30': {
                'leader_level': 100,
                'leader_wakeup': 8,
                'leader_forge_lv': 10,
                'merc_level': 30,
                'merc_wakeup_cap': 5,
                'merc_forge_lv': 30,
                'artifact_lv': 15,
                'contract_lv': 2,
                'weapon_total_star_id': 13,
                'leader_contract_id': 22,
                'title_bonus': dict(SERVER30_TITLE_BONUS),
                'cultivation': [list(x) for x in DEFAULT_CULTIVATION],
            },
            'max': {
                'leader_level': 100,
                'leader_wakeup': 8,
                'leader_forge_lv': 10,
                'merc_level': 100,
                'merc_wakeup_cap': 99,
                'merc_forge_lv': 30,
                'artifact_lv': 15,
                'contract_lv': 2,
                'weapon_total_star_id': 13,
                'leader_contract_id': 22,
                'title_bonus': dict(PERMANENT_TITLE_BONUS),
                'cultivation': [list(x) for x in DEFAULT_CULTIVATION],
            },
            'fair_eq': {
                'description': '等资源公平评测分支：闪金及以下全二契，炫金默认一契(二契炫金配额默认0)，统一宝具/主角契约/符文/养成/称号/修炼，屏蔽实服快照污染。',
                'leader_level': 100,
                'leader_wakeup': 8,
                'leader_forge_lv': 10,
                'merc_level': 30,
                'merc_wakeup_cap': 5,
                'merc_forge_lv': 30,
                'artifact_lv': 15,
                'contract_lv': 2,
                'q7_contract_lv': 1,
                'q7_contract2_limit': 0,
                'rune_lv': 20,
                'weapon_total_star_id': 13,
                'leader_contract_id': 22,
                'ignore_team_overrides': True,
                'title_bonus': dict(PERMANENT_TITLE_BONUS),
                'cultivation': [list(x) for x in DEFAULT_CULTIVATION],
            },
            'opponent_groups': {},
        }
        if self.profile_path and self.profile_path.exists():
            raw = json.loads(self.profile_path.read_text(encoding='utf8'))
            for k, v in raw.items():
                if k in default_profile and isinstance(default_profile[k], dict) and isinstance(v, dict) and k != 'opponent_groups':
                    default_profile[k] = {**default_profile[k], **v}
                else:
                    default_profile[k] = v
        return default_profile

    def list_modes(self) -> list[str]:
        return [
            k for k, v in self.profile.items()
            if isinstance(v, dict) and 'merc_level' in v and 'leader_level' in v
        ]

    def get_mode_profile(self, mode: str, mode_overrides: dict[str, Any] | None = None) -> dict[str, Any]:
        if mode in self.profile and isinstance(self.profile[mode], dict) and 'merc_level' in self.profile[mode]:
            base = dict(self.profile[mode])
        else:
            key = 'server30' if mode == 'server30' else 'max'
            base = dict(self.profile[key])
        if mode_overrides:
            for k, v in mode_overrides.items():
                if v is not None:
                    base[k] = v
        return base

    def _load_slim_boot(self) -> dict[str, Any]:
        """Loads only the 3 tiny slices needed by SimulatorEngine (~120KB vs 20MB current-boot-metadata.json)."""
        boot_path = self.data_dir / 'current-boot-metadata.json'
        cache_path = self.data_dir / 'sim-runtime-cache.json'
        st = boot_path.stat()
        sig = f'{st.st_size}:{int(st.st_mtime)}'
        if cache_path.exists():
            try:
                cached = json.loads(cache_path.read_text(encoding='utf8'))
                if cached.get('_sig') == sig:
                    return cached['files']
            except Exception:
                pass
        full = json.loads(boot_path.read_text(encoding='utf8'))['files']
        slim = {
            'lua-sources.json': {
                'logic.skill_manager': full['lua-sources.json']['logic.skill_manager'],
                'entity.troop': full['lua-sources.json']['entity.troop'],
            },
            'data-tables.json': {
                'destiny_forge': full['data-tables.json']['destiny_forge'],
            },
        }
        try:
            cache_path.write_text(json.dumps({'_sig': sig, 'files': slim}, ensure_ascii=False), encoding='utf8')
        except Exception:
            pass
        return slim

    def _load_configs(self) -> None:
        boot = self._load_slim_boot()
        const = _read_json(self.data_dir, 'pvp_constants')
        overlay = _read_json(self.data_dir, 'current-mercenary-definitions')

        cfg_names = (
            'mercenary', 'active_skill', 'passive_skill', 'cooperative_skill',
            'skill_buffer', 'mercenary_artifact', 'mercenary_exp', 'mercenary_contract',
            'leader_contract', 'weapon_star_upgrade', 'weapon_total_star',
            'wakeup_info', 'weapon_forge', 'destiny_skill',
        )
        cfg = {name: _read_json(self.data_dir, name) for name in cfg_names}
        for name in ('mercenary', 'active_skill', 'passive_skill', 'cooperative_skill', 'skill_buffer'):
            for raw in overlay['tables'][name]:
                row = raw.copy()
                key = str(row['ID'])
                if name == 'active_skill':
                    for field in ('self_property', 'enemy_property'):
                        row[field + '_map'] = [int(n) for n in row[field].split('|')] if row.get(field) else []
                elif name == 'passive_skill':
                    row['is_special'] = row['effect_type'] in (5, 10) or any(row[x] for x in ('job', 'sex', 'race'))
                elif name == 'cooperative_skill':
                    row['mercenary_ids'] = {int(k): v for k, v in collections.Counter(re.findall(r'\d+', row['condition'])).items()}
                elif name == 'skill_buffer':
                    row['factor'] /= 100
                cfg[name][key] = row
        for lv in ('1', '2'):
            cfg['mercenary_contract'][lv].update({str(q['ID']): q for q in overlay['tables']['mercenary_contract' + lv]})
        for row in sorted(overlay['tables']['mercenary_artifact'], key=lambda q: (q['template_id'], q['artifact_level'])):
            levels = cfg['mercenary_artifact'].setdefault(str(row['template_id']), {})
            prev = levels.get(str(row['artifact_level'] - 1), {})
            fields = {f'sum_{p}': row[p] + prev.get(f'sum_{p}', 0) for p in ('bp',) + PROPS}
            levels[str(row['artifact_level'])] = row | fields
        for k, v in overlay['skill_metadata'].items():
            row = cfg['active_skill'].get(k) or cfg['passive_skill'].get(k)
            if row is not None:
                row.update(v)
        # Live-verified override: 1400043 (魔法驳回) never seals effect_type=10 on the server;
        # seal_turn/seal_turneffect in active_skill.json comes from its conditional 13% passive clause.
        if '1400043' in cfg['active_skill']:
            cfg['active_skill']['1400043'].update({
                'seal_turn': 0,
                'seal_turneffect': 0,
                'seal_cond1': 0,
                'seal_cond2': 0,
            })
        # Live-verified override: 1800043 (无明) seals effect_type=13 for 1 turn when cast (desc + 0/11 post-cast pure skills)
        if '1800043' in cfg['active_skill']:
            cfg['active_skill']['1800043']['seal_turn'] = 1
        for row in cfg['active_skill'].values():
            if 'ignore_dodge' in row:
                row['ignore_dodge'] = bool(row['ignore_dodge'])
        cfg['custom_growth'] = {
            k: {str(row['wakeup']): row for row in v['wakeup_rows']}
            for k, v in overlay['custom_growth'].items()
        }
        cfg['destiny_forge'] = {str(q['ID']): q for q in boot['data-tables.json']['destiny_forge']['rows']}

        self.cfg = cfg
        self.const = const
        self.boot = boot
        self.mercs: dict[int, dict[str, Any]] = {int(k): v for k, v in cfg['mercenary'].items()}
        self.allowed_tids = {
            tid for tid, row in self.mercs.items()
            if row.get('library_mark') and row.get('max_wakeup', 0) > 0
        }
        # True acquisition limit: explicit num_limit > 0, else 1 if is_unique, else 29
        self.caps: dict[int, int] = {
            tid: (row['num_limit'] or (1 if row.get('is_unique') else 29))
            for tid, row in self.mercs.items()
        }
        self.weapons: dict[int, str] = {
            int(k): v['name'] for k, v in cfg['destiny_skill'].items() if int(k) in range(1, 13)
        }
        self.weapon_by_name: dict[str, int] = {name: wid for wid, name in self.weapons.items()}

        # Runes (all levels/qualities, plus helper map for level-20 & arbitrary-level effect lookup)
        rune_raw = _read_json(self.data_dir, 'rune')
        rune_prop_raw = _read_json(self.data_dir, 'rune_property')
        self.runes: dict[int, dict[str, Any]] = {int(k): v for k, v in rune_raw.items()}
        self.rune_props = rune_prop_raw
        self.rune_lv20_delta: dict[int, tuple[int, ...]] = {}
        self.rune_by_name: dict[str, list[int]] = collections.defaultdict(list)
        for rid, rinfo in self.runes.items():
            self.rune_by_name[rinfo['name']].append(rid)
            rprop = rune_prop_raw.get(str(rid))
            if not rprop:
                continue
            self.rune_lv20_delta[rid] = self._rune_delta(rid, int(rinfo.get('level_limit', 20)))

    def _rune_delta(self, rid: int, lv: int = 20) -> tuple[int, ...]:
        rinfo = self.runes[rid]
        rprop = self.rune_props.get(str(rid), {})
        lv_key = str(min(max(1, int(lv)), int(rinfo.get('level_limit', 20))))
        mine = rprop.get('mine_property', {}).get(lv_key, {})
        foe = rprop.get('enemy_property', {}).get(lv_key, {})
        return (
            _round_rune_val(float(mine.get('speed', 0))),
            _round_rune_val(float(mine.get('defense', 0))),
            _round_rune_val(float(mine.get('dodge', 0))),
            _round_rune_val(float(mine.get('authority', 0))),
            _round_rune_val(float(foe.get('speed', 0))),
            _round_rune_val(float(foe.get('defense', 0))),
            _round_rune_val(float(foe.get('dodge', 0))),
            _round_rune_val(float(foe.get('authority', 0))),
        )

        # Mercenary name index (remember e.g. 小叔叔 has both 19000286 and 88000030)
        self.merc_by_name: dict[str, list[int]] = collections.defaultdict(list)
        for tid, row in self.mercs.items():
            self.merc_by_name[row['name']].append(tid)
        for tids in self.merc_by_name.values():
            tids.sort()

    def _lua_convert(self, value: Any) -> Any:
        if isinstance(value, dict):
            return self.lua.table_from({
                int(k) if isinstance(k, str) and k.isdigit() else k: self._lua_convert(v)
                for k, v in value.items()
            })
        if isinstance(value, (list, tuple)):
            return self.lua.table_from([self._lua_convert(v) for v in value])
        return value

    def _init_lua(self) -> None:
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        modules = self.lua.eval('package.loaded')
        modules['logic.config_manager'] = self._lua_convert({f'{name}_config': t for name, t in self.cfg.items()})
        modules['util.constants'] = self._lua_convert({
            'ACTIVE_SKILL_EFFECT_TYPE': self.const['active_types'],
            'PASSIVE_SKILL_EFFECT_TYPE': self.const['passive_types'],
            'PROPERTY_TYPE_NAME': list(PROPS),
            'SKILL_CULTIVATION': self.const['cultivation_map'],
            'MAX_WEAPON_LV': self.const['weapon'],
            'MAX_ARTIFACT_LEVEL': self.const['artifact'],
            'MAX_DESTINY_WEAPON_LV': self.const['destiny_max'],
            'FORCE_LEVEL_PROPERTY': self.const['force_property'],
            'ACTIVE_SKILL_ID_OFFSET': 1000001,
        })
        modules['util.client_constants'] = self._lua_convert({'BATTLE': {'left_troop_id': 1, 'right_troop_id': 2}})
        self.lua.execute('bit={}; CCLOG=function() end')
        modules['logic.skill_manager'] = self.lua.execute(self.boot['lua-sources.json']['logic.skill_manager'])
        modules['entity.troop'] = self.lua.execute(self.boot['lua-sources.json']['entity.troop'])

        # Live-verified patch: skill_manager:AddPassiveSkill must ONLY add flat skill_info stats on pass 1
        # (not take_special). Otherwise is_special=True passives with flat stats (600013 泉此方 +10 auth,
        # 600012 时崎狂三 +15 dodge, 600050 宿大爷 +7 dodge) get double-counted on pass 2.
        self.lua.execute('''
        local sm = require('logic.skill_manager')
        local cm = require('logic.config_manager')
        local c = require('util.constants')
        local PASSIVE = c.PASSIVE_SKILL_EFFECT_TYPE
        local PROP = c.PROPERTY_TYPE_NAME

        function sm:AddPassiveSkill(troop, skill_id, take_special)
            local skill_info = self:GetSkill(skill_id)
            if not take_special then
                troop.speed = troop.speed + (skill_info.speed or 0)
                troop.defense = troop.defense + (skill_info.defense or 0)
                troop.dodge = troop.dodge + (skill_info.dodge or 0)
                troop.authority = troop.authority + (skill_info.authority or 0)
            end

            if skill_id >= c.ACTIVE_SKILL_ID_OFFSET then
                return
            end

            local skill = cm.passive_skill_config[skill_id]
            if not skill then
                return
            end

            if skill.is_special and not take_special then
                if skill.effect_type == PASSIVE['convert_property'] then
                    table.insert(troop.special_skill_list, skill_id)
                elseif skill.effect_type == PASSIVE['convert_property2'] then
                    if not troop.special_skill_list2 then
                        troop.special_skill_list2 = {}
                    end
                    table.insert(troop.special_skill_list2, skill_id)
                else
                    table.insert(troop.special_skill_list, 1, skill_id)
                end
                return
            end

            local num = 0
            if skill.job ~= 0 or skill.sex ~= 0 or skill.race ~= 0 then
                for template_id, mercenary_num in pairs(troop.all_mercenary_template_ids) do
                    local m = cm.mercenary_config[template_id]
                    local ok_job = skill.job == 0 or skill.job == m.job
                    local ok_sex = skill.sex == 0 or skill.sex == m.sex
                    local ok_race = skill.race == 0 or skill.race == m.race
                    if ok_job and ok_sex and ok_race then
                        num = num + mercenary_num
                    end
                end
            else
                num = 1
            end

            local add_num = skill.param1 * num
            if skill.effect_type == PASSIVE['increase_speed'] then
                troop.speed = troop.speed + self:FixPropertyNum(troop, skill, add_num)
            elseif skill.effect_type == PASSIVE['increase_defense'] then
                troop.defense = troop.defense + self:FixPropertyNum(troop, skill, add_num)
            elseif skill.effect_type == PASSIVE['increase_dodge'] then
                troop.dodge = troop.dodge + self:FixPropertyNum(troop, skill, add_num)
            elseif skill.effect_type == PASSIVE['increase_authority'] then
                troop.authority = troop.authority + self:FixPropertyNum(troop, skill, add_num)
            elseif skill.effect_type == PASSIVE['convert_property'] then
                if skill.param1 == 0 or skill.param2 == 0 or skill.param3 == 0 then
                    return
                end
                local src_property = PROP[skill.param1]
                local dst_property = 'extra_' .. PROP[skill.param2]
                local src_num = troop[src_property] >= 0 and troop[src_property] or 0
                troop[dst_property] = troop[dst_property] + self:FixPropertyNum(troop, skill, math.floor(src_num / skill.param3 * num))
            elseif skill.effect_type == PASSIVE['convert_property2'] then
                if skill.param1 == 0 or skill.param2 == 0 or skill.param3 == 0 then
                    return
                end
                local src_property = PROP[skill.param1]
                local dst_property = 'extra_' .. PROP[skill.param2]
                local src_num = troop[src_property] >= 0 and troop[src_property] or 0
                local add2 = src_num >= skill.param3 and skill.stack_limit or 0
                troop[dst_property] = troop[dst_property] + add2
            elseif skill.effect_type == PASSIVE['resist_lethal'] then
                troop.resist_lethal_num = skill.param1 or 0
            end
        end
        ''')

        # Live-verified patch: active_effects[9] (increase_by_self_bp) honors skill.reply_after_attack
        # on the live server/client (e.g. 70000010 十一之弹 deals damage using pre-heal cur_bp, then heals).
        self.lua.execute('''
        local sm = require('logic.skill_manager')
        local c = require('util.constants')
        local orig_eff9 = sm.active_effects[c.ACTIVE_SKILL_EFFECT_TYPE['increase_by_self_bp']]
        sm.active_effects[c.ACTIVE_SKILL_EFFECT_TYPE['increase_by_self_bp']] = function(caster, receiver, skill, is_dodge)
            if not skill.reply_after_attack then
                return orig_eff9(caster, receiver, skill, is_dodge)
            end
            local cur_type = c.SKILL_CULTIVATION['increase_by_self_bp']
            local reply = math.ceil(caster.cur_bp * skill.param / 100 * caster.bp_increase_factor)
            local c1 = caster.cultivation_property[cur_type] and caster.cultivation_property[cur_type].coefficient1 or 0
            local c2 = receiver.cultivation_property[cur_type] and receiver.cultivation_property[cur_type].coefficient2 or 0
            reply = caster:BuffSeriouslyInjured(math.ceil(reply * (1 + c1 / 100 + c2 / 100)))
            if is_dodge then
                caster.cur_bp = caster.cur_bp + reply
                return
            end
            local damage = (caster.max_bp * 0.2 + caster.cur_bp * 0.1) * receiver.damage_reduction_factor * caster.final_damage_modifier
            local ratio = (skill.param2 and skill.param2 ~= 0) and skill.param2 or 100
            damage = caster:BuffWeak(math.ceil(damage * ratio / 100))
            if caster.exhaust_turn > 0 then damage = 1 end
            receiver:CalcDamageBp(damage)
            caster.cur_bp = caster.cur_bp + reply
        end
        ''')

        self.lua.execute('''
        local entity = require('entity.troop')
        local sm = require('logic.skill_manager')
        local cm = require('logic.config_manager')
        local c = require('util.constants')
        local properties = c.PROPERTY_TYPE_NAME

        local function add_skill(team, sid, is_coop)
            if not sid or sid==0 or not sm:GetSkill(sid) then return end
            team.special_skill_index=0
            sm:AddPassiveSkill(team,sid)
            local skill=cm.active_skill_config[sid]
            if skill and skill.trigger_chance>0 then
                team.skills[#team.skills+1]=skill
                if is_coop then team.coop_skill_ids[skill.ID]=true end
            end
            local passive=cm.passive_skill_config[sid]
            if passive and passive.effect_type==7 then
                for _,v in ipairs({passive.param1,passive.param2,passive.param3}) do
                    if v>0 then team.permanent_seals[v]=true end
                end
            elseif passive and passive.effect_type==8 then
                team.shield=team.shield+passive.param1+math.ceil(team.max_bp*passive.param2/100)
            end
        end

        function sim_build_roster(tids, weapon, washes, title, mode, prof, slot_contracts)
            local t={max_bp=0,speed=0,defense=0,dodge=0,authority=0,shield=0,resist_lethal_num=0,
                extra_speed=0,extra_defense=0,extra_dodge=0,extra_authority=0,all_mercenary_template_ids={},
                special_skill_list={},special_skill_list2={},stack_list={},skills={},permanent_seals={},combos={},coop_skill_ids={}}
            local lc_id = (prof and prof.leader_contract_id ~= nil) and prof.leader_contract_id or 22
            local wts_id = (prof and prof.weapon_total_star_id ~= nil) and prof.weapon_total_star_id or 13
            local leader = (lc_id > 0 and cm.leader_contract_config[lc_id]) or {bp=0,speed=0,defense=0,dodge=0,authority=0}
            local total_stars = (wts_id > 0 and cm.weapon_total_star_config[wts_id]) or {speed=0,defense=0,dodge=0,authority=0}
            for _,p in ipairs(properties) do t[p]=(total_stars[p] or 0)+(title and title[p] or 0) end
            if title then t.max_bp=title.bp end
            for _,weapon_stars in ipairs(cm.weapon_star_upgrade_config) do
                t.max_bp=t.max_bp+(weapon_stars[12] and weapon_stars[12].add_bp or 0)
            end
            local merc_forge_lv = (prof and prof.merc_forge_lv ~= nil) and prof.merc_forge_lv or c.MAX_WEAPON_LV
            local leader_forge_lv = (prof and prof.leader_forge_lv ~= nil) and prof.leader_forge_lv or c.MAX_DESTINY_WEAPON_LV
            local art_lv_cap = (prof and prof.artifact_lv ~= nil) and prof.artifact_lv or c.MAX_ARTIFACT_LEVEL
            local forge=cm.weapon_forge_config[merc_forge_lv+1].bp_factor
            local members,coops={},{}
            for i,tid in ipairs(tids) do
                local row=cm.mercenary_config[tid]
                local is_leader=tid>=99000001
                local artifact=row.have_artifact and cm.mercenary_artifact_config[tid] or nil
                local growth_row=cm.custom_growth_config[tid] or cm.wakeup_info_config
                local wake = row.max_wakeup
                local lv = 100
                if is_leader then
                    wake = math.min(row.max_wakeup, (prof and prof.leader_wakeup) or row.max_wakeup)
                    lv = (prof and prof.leader_level) or 100
                elseif prof then
                    wake = math.min(row.max_wakeup, prof.merc_wakeup_cap or row.max_wakeup)
                    lv = prof.merc_level or 100
                elseif mode == 'server30' then
                    wake = math.min(row.max_wakeup, 5)
                    lv = 30
                end
                local aw=growth_row[wake]
                local stat={}
                local art=(artifact and art_lv_cap > 0) and (artifact[art_lv_cap] or {}) or {}
                local want_clv = (slot_contracts and slot_contracts[i]) or (prof and prof.contract_lv) or 2
                local contract={}
                if want_clv >= 2 then
                    contract=cm.mercenary_contract_config[2][tid] or cm.mercenary_contract_config[1][tid] or {}
                elseif want_clv == 1 then
                    contract=cm.mercenary_contract_config[1][tid] or {}
                end
                local growth=(math.min(lv,aw.damping_level1)*aw.damping_factor1 + math.min(lv,aw.damping_level2)*aw.damping_factor2
                    + math.min(lv,aw.damping_level3)*aw.damping_factor3 + lv*aw.damping_factor4)*aw.accumulated_value+aw.basic_value
                local bp=growth*row.bp_factor+row.init_bp
                if is_leader then bp=(bp+(leader.bp or 0)+1000)*(1+cm.destiny_forge_config[leader_forge_lv+1].bp_factor/100) end
                if not is_leader then bp=bp*(1+(forge+(art.sum_bp or 0))/100)*(row.can_upgrade_force and 1.35 or 1) end
                t.max_bp=t.max_bp+math.ceil(bp)
                for _,p in ipairs(properties) do
                    local value=(row.have_artifact and row['artifact_'..p] or 0)+(art['sum_'..p] or 0)+(contract[p] or 0)
                    if is_leader then value=value+(leader[p] or 0) end
                    if row.can_upgrade_force and not is_leader and washes[i]==p then
                        value=value+c.FORCE_LEVEL_PROPERTY[p]*((want_clv>=2 and contract.contract_level==2) and 2 or 1)
                    end
                    stat[p]=value
                end
                members[i]=stat
                t.all_mercenary_template_ids[tid]=(t.all_mercenary_template_ids[tid] or 0)+1
            end
            for i,tid in ipairs(tids) do
                local row=cm.mercenary_config[tid]
                for _,p in ipairs(properties) do t[p]=t[p]+members[i][p] end
                if tid<99000001 then
                    for _,ex in ipairs({row.ex_skill1,row.ex_skill2}) do if ex>0 then coops[ex]=true end end
                end
            end
            -- Cooperative skills BEFORE personal active skills (verified on live server)
            local ids={}
            for id in pairs(coops) do ids[#ids+1]=id end
            table.sort(ids)
            for _,id in ipairs(ids) do
                local row=cm.cooperative_skill_config[id]
                local unlocked=row~=nil
                for tid,n in pairs(row and row.mercenary_ids or {}) do
                    if (t.all_mercenary_template_ids[tid] or 0)<n then unlocked=false; break end
                end
                if unlocked then
                    t.combos[#t.combos+1]=row.name
                    add_skill(t,row.real_skill1,true); add_skill(t,row.real_skill2,true); add_skill(t,row.real_skill3,true)
                end
            end
            for i,tid in ipairs(tids) do
                local row=cm.mercenary_config[tid]
                if tid>=99000001 then
                    local upgrade=cm.weapon_star_upgrade_config[weapon]
                    add_skill(t,upgrade[12].skill_id,false)
                else
                    add_skill(t,row.skill1,false); add_skill(t,row.skill2,false); add_skill(t,row.skill3,false)
                end
            end
            -- Match logic.troop:GetTroopProperty: append special_skill_list2 onto special_skill_list before pass 2
            for _,sid in ipairs(t.special_skill_list2) do
                table.insert(t.special_skill_list, sid)
            end
            t.special_skill_list2 = {}
            for i,sid in ipairs(t.special_skill_list) do
                t.special_skill_index=i
                sm:AddPassiveSkill(t,sid,true)
            end
            for _,p in ipairs(properties) do t[p]=t[p]+t['extra_'..p] end
            return t
        end

        local function usable_real(sk, slot_idx, turn, mine, enemy, my_bp, foe_bp)
            if sk.times_limit>0 and sk.times_limit<999 and (mine.used[slot_idx] or 0)>=sk.times_limit then return false end
            if sk.turn_cond1~=0 and turn<=sk.turn_cond1 or sk.turn_cond2~=0 and turn>=sk.turn_cond2 then return false end
            if sk.self_bp_cond1~=0 and my_bp<=sk.self_bp_cond1 or sk.self_bp_cond2~=0 and my_bp>=sk.self_bp_cond2 then return false end
            if sk.enemy_bp_cond1~=0 and foe_bp<=sk.enemy_bp_cond1 or sk.enemy_bp_cond2~=0 and foe_bp>=sk.enemy_bp_cond2 then return false end
            local tc=sk.turn_cond3
            if tc==1 and not mine.last_hit or tc==2 and mine.last_hit or tc==3 and not enemy.last_hit or tc==4 and enemy.last_hit then return false end
            local bp=sk.cur_bp_cond
            if bp==1 and mine.cur_bp<=enemy.cur_bp or bp==2 and mine.cur_bp>=enemy.cur_bp or bp==3 and mine.cur_bp~=enemy.cur_bp then return false end
            if sk.trigger_chance<101 then
                if mine.permanent_seals[sk.effect_type] then return false end
                for _,seal in ipairs(mine.seals) do
                    if seal.rem>0 and (seal.kind==0 or seal.kind==sk.effect_type)
                        and (seal.lo==0 or sk.trigger_chance>seal.lo) and (seal.hi==0 or sk.trigger_chance<seal.hi) then return false end
                end
            end
            return true
        end

        local function choose_real(turn, mine, enemy)
            local roll=math.random(1,100)
            local my_bp=100*mine.cur_bp/mine.max_bp
            local foe_bp=100*enemy.cur_bp/enemy.max_bp
            for slot_idx,skill in ipairs(mine.skills) do
                if usable_real(skill,slot_idx,turn,mine,enemy,my_bp,foe_bp) then
                    roll=roll-skill.trigger_chance
                    if roll<=0 then
                        mine.used[slot_idx]=(mine.used[slot_idx] or 0)+1
                        return skill
                    end
                end
            end
            return sm:GetMeleeSkill()
        end

        local function initialize(team, foe, cultiv)
            local unit=entity.New(1)
            unit:SetProperty(team.max_bp,team.max_bp,team.shield,foe,team.speed,team.defense,team.dodge,team.authority,
                team.speed,team.defense,team.dodge,team.authority)
            if cultiv then
                local cp = {}
                for i=1,6 do
                    cp[i] = {coefficient1 = cultiv[i][1], coefficient2 = cultiv[i][2]}
                end
                unit:SetCultivation(cp)
            end
            unit.skills=team.skills; unit.permanent_seals={}; unit.seals={}; unit.used={}; unit.resist_lethal_num=team.resist_lethal_num
            return unit
        end

        local function buffer_roll(id)
            local b=id and cm.skill_buffer_config[id]
            return b and math.random(1,100)<=b.chance and id or 0
        end

        local function mount_recorded(mine, bid, before_attack)
            local cfg = cm.skill_buffer_config[tonumber(bid) or 0]
            if cfg and ((cfg.start == 'before_attack') == before_attack) then
                mine:BuffTrigger(bid)
            end
        end

        local function apply_stat_mods(mine, enemy, resolved, is_dodge)
            if resolved.need_hit == 1 and is_dodge then return end
            if #resolved.enemy_property_map > 0 then
                local m = resolved.enemy_property_map
                enemy.speed = enemy.speed + m[1]
                enemy.defense = enemy.defense + m[2]
                enemy.dodge = enemy.dodge + m[3]
                enemy.authority = enemy.authority + m[4]
            end
            if #resolved.self_property_map > 0 then
                local m = resolved.self_property_map
                mine.speed = mine.speed + m[1]
                mine.defense = mine.defense + m[2]
                mine.dodge = mine.dodge + m[3]
                mine.authority = mine.authority + m[4]
            end
            enemy:CalcDamageFactor()
            mine:CalcDamageFactor()
        end

        local function duel_real(a, b, culta, cultb, trace_out, forced_first)
            local left=initialize(a,nil,culta); local right=initialize(b,left,cultb); left.enemy=right
            left.permanent_seals=b.permanent_seals; right.permanent_seals=a.permanent_seals
            left:CalcDamageFactor(); right:CalcDamageFactor()
            local mine,enemy=left,right
            if forced_first == 'right' then
                mine,enemy=right,left
            elseif forced_first ~= 'left' then
                if left.speed<right.speed or left.speed==right.speed and math.random(2)==2 then mine,enemy=right,left end
            end
            local turn=1
            for round=1,160 do
                local skill=choose_real(turn,mine,enemy)
                local is_pure_combo = skill.gm_combo_effects and skill.gm_combo_effects[2] and skill.gm_combo_effects[2].effect_type == 13
                -- Live-verified: critical_damage (effect_type=2), true_damage (effect_type=13), and pure-damage combos
                -- (70000055/70000082: 0/202 dodged live) never roll dodge.
                local is_dodge=not (skill.ignore_dodge or skill.effect_type==2 or skill.effect_type==13 or is_pure_combo) and math.random(1,1000)<=enemy.dodge_chance
                mine:BuffProcessPre()
                local pre_exhaust = (mine.exhaust_turn > 0)
                local pre_enemy_bp=enemy.cur_bp
                local pre_self_bp=mine.cur_bp
                local pre_fdm=mine.final_damage_modifier
                local b1, b2, b3 = buffer_roll(skill.buffer_id), buffer_roll(skill.buffer_id2), buffer_roll(skill.buffer_id3)
                local mod = 0
                local resolved = skill
                -- Live-verified (270/270): gm_combo_effects with seg2 effect_type==13 (e.g. 70000055/70000082)
                -- run seg1 heal ONLY (no phantom 100% normal attack), then seg2 pure dmg using post-heal cur_bp
                -- & pre-addon fdm (honoring pre_exhaust), then apply self/enemy property shifts afterwards.
                if skill.gm_combo_effects and skill.gm_combo_effects[2] and skill.gm_combo_effects[2].effect_type == 13 then
                    local seg1 = skill.gm_combo_effects[1]
                    local seg2 = skill.gm_combo_effects[2]
                    mount_recorded(mine, b1, true); mount_recorded(mine, b2, true); mount_recorded(mine, b3, true)
                    if seg1.effect_type == 11 then
                        local reply = math.ceil(enemy.cur_bp * seg1.param / 100 * mine.bp_increase_factor)
                        reply = mine:BuffSeriouslyInjured(reply)
                        local ct = c.SKILL_CULTIVATION['increase_by_enemy_bp']
                        if ct then
                            local c1 = mine.cultivation_property[ct] and mine.cultivation_property[ct].coefficient1 or 0
                            local c2 = enemy.cultivation_property[ct] and enemy.cultivation_property[ct].coefficient2 or 0
                            reply = math.ceil(reply * (1 + c1/100 + c2/100))
                        end
                        mine.cur_bp = mine.cur_bp + reply
                    elseif seg1.effect_type == 9 then
                        local reply = math.ceil(mine.cur_bp * seg1.param / 100 * mine.bp_increase_factor)
                        reply = mine:BuffSeriouslyInjured(reply)
                        local ct = c.SKILL_CULTIVATION['increase_by_self_bp']
                        if ct then
                            local c1 = mine.cultivation_property[ct] and mine.cultivation_property[ct].coefficient1 or 0
                            local c2 = enemy.cultivation_property[ct] and enemy.cultivation_property[ct].coefficient2 or 0
                            reply = math.ceil(reply * (1 + c1/100 + c2/100))
                        end
                        mine.cur_bp = mine.cur_bp + reply
                    end
                    local dmg = math.ceil((mine.max_bp/5 + mine.cur_bp/10) * seg2.param / 100 * pre_fdm)
                    local c1 = mine.cultivation_property[5] and mine.cultivation_property[5].coefficient1 or 0
                    local c2 = enemy.cultivation_property[5] and enemy.cultivation_property[5].coefficient2 or 0
                    dmg = math.ceil(dmg * (1 + c1/100 + c2/100))
                    dmg = mine:BuffWeak(dmg)
                    if pre_exhaust then dmg = 1 end
                    enemy:CalcDamageBp(dmg)
                    apply_stat_mods(mine, enemy, skill, is_dodge)
                    if not (skill.need_hit == 1 and is_dodge) and skill.exhaust_turn > 0 then
                        enemy.exhaust_turn = math.max(enemy.exhaust_turn, skill.exhaust_turn)
                    end
                    mine.exhaust_turn = math.max(0, mine.exhaust_turn - 1)
                    mine.last_turn_skill = skill
                    mount_recorded(mine, b1, false); mount_recorded(mine, b2, false); mount_recorded(mine, b3, false)
                    mod = (not (skill.need_hit == 1 and is_dodge) and skill.turn_fixed) or 0
                else
                    mod=mine:UseSkill(skill,is_dodge,b1,b2,b3)
                    resolved=mine.last_turn_skill or skill
                    local prev_dmg=math.max(0, pre_enemy_bp - enemy.cur_bp)
                    if resolved.gm_combo_effects then
                        local seg2 = resolved.gm_combo_effects[2]
                        if seg2 then
                            if seg2.lifesteal_basis=='previous_damage' and not is_dodge then
                                mine.cur_bp = mine.cur_bp + math.ceil(prev_dmg * seg2.param / 100 * mine.bp_increase_factor)
                            end
                        end
                    end
                    if resolved.gm_critical_lifesteal_percent and resolved.gm_critical_lifesteal_percent>0 and not is_dodge then
                        mine.cur_bp = mine.cur_bp + math.ceil(prev_dmg * resolved.gm_critical_lifesteal_percent / 100 * mine.bp_increase_factor)
                    end
                    if resolved.gm_after_attack_total_heal_percent and resolved.gm_after_attack_total_heal_percent>0 then
                        mine.cur_bp = mine.cur_bp + math.ceil(mine.max_bp * resolved.gm_after_attack_total_heal_percent / 100 * mine.bp_increase_factor)
                    end
                end
                local post_combo_enemy_bp = enemy.cur_bp
                local post_combo_self_bp = mine.cur_bp
                local killed_during_attack = (enemy.cur_bp <= 0)
                if not killed_during_attack then
                    mine:BuffProcessLast()
                    mine:BuffTimesProcess()
                    for _,seal in ipairs(mine.seals) do seal.rem=math.max(0,seal.rem-1) end
                    local sealed_added = false
                    if resolved.seal_turn>0 and not (resolved.need_hit==1 and is_dodge) then
                        enemy.seals[#enemy.seals+1]={rem=resolved.seal_turn,kind=resolved.seal_turneffect,lo=resolved.seal_cond1,hi=resolved.seal_cond2}
                        sealed_added = true
                    end
                    mine.last_hit=not is_dodge
                    if trace_out then
                        local actor = (mine == left) and 'A' or 'B'
                        local act_tbl = (mine == left) and a or b
                        trace_out[#trace_out+1] = {
                            round = round,
                            turn = turn,
                            actor = actor,
                            skill_id = resolved.ID or 0,
                            skill_name = resolved.name or '普通攻击',
                            effect_type = resolved.effect_type or 0,
                            is_coop = act_tbl.coop_skill_ids[resolved.ID] == true,
                            is_dodge = is_dodge,
                            dmg_to_enemy = pre_enemy_bp - post_combo_enemy_bp,
                            self_heal = post_combo_self_bp - pre_self_bp,
                            sealed = sealed_added,
                            seal_turn = resolved.seal_turn or 0,
                            seal_cond1 = resolved.seal_cond1 or 0,
                            a_bp = left.cur_bp,
                            b_bp = right.cur_bp,
                            a_shield = left.cur_shield_bp or 0,
                            b_shield = right.cur_shield_bp or 0,
                        }
                    end
                else
                    if trace_out then
                        local actor = (mine == left) and 'A' or 'B'
                        local act_tbl = (mine == left) and a or b
                        trace_out[#trace_out+1] = {
                            round = round,
                            turn = turn,
                            actor = actor,
                            skill_id = resolved.ID or 0,
                            skill_name = resolved.name or '普通攻击',
                            effect_type = resolved.effect_type or 0,
                            is_coop = act_tbl.coop_skill_ids[resolved.ID] == true,
                            is_dodge = is_dodge,
                            dmg_to_enemy = pre_enemy_bp - post_combo_enemy_bp,
                            self_heal = post_combo_self_bp - pre_self_bp,
                            sealed = false,
                            seal_turn = resolved.seal_turn or 0,
                            seal_cond1 = resolved.seal_cond1 or 0,
                            a_bp = left.cur_bp,
                            b_bp = right.cur_bp,
                            a_shield = left.cur_shield_bp or 0,
                            b_shield = right.cur_shield_bp or 0,
                        }
                    end
                    return (mine == left) and 1 or -1
                end
                if left.cur_bp<=0 and right.cur_bp<=0 then return 0 end
                if right.cur_bp<=0 then return 1 end
                if left.cur_bp<=0 then return -1 end
                turn=math.max(1,turn+mod)+1
                mine,enemy=enemy,mine
            end
            return 0
        end

        function sim_apply_runes(a, b, runa, runb, ova, ovb)
            local aa, bb = {}, {}
            for k, v in pairs(a) do aa[k] = v end
            for k, v in pairs(b) do bb[k] = v end
            if ova then
                if ova._scale_shield and ova.max_bp and a.max_bp and a.max_bp > 0 and aa.shield and aa.shield > 0 then
                    aa.shield = math.ceil(aa.shield * ova.max_bp / a.max_bp)
                end
                for k, v in pairs(ova) do if k ~= '_scale_shield' then aa[k] = v end end
            end
            if ovb then
                if ovb._scale_shield and ovb.max_bp and b.max_bp and b.max_bp > 0 and bb.shield and bb.shield > 0 then
                    bb.shield = math.ceil(bb.shield * ovb.max_bp / b.max_bp)
                end
                for k, v in pairs(ovb) do if k ~= '_scale_shield' then bb[k] = v end end
            end
            for i, p in ipairs(properties) do
                aa[p] = aa[p] + runa[i] + runb[i+4]
                bb[p] = bb[p] + runb[i] + runa[i+4]
            end
            return aa, bb
        end

        function sim_series(a, b, runa, runb, runs, seed, culta, cultb, ova, ovb)
            math.randomseed(seed)
            local aa, bb = sim_apply_runes(a, b, runa, runb, ova, ovb)
            local w, l = 0, 0
            for _ = 1, runs do
                local r = duel_real(aa, bb, culta, cultb, nil, nil)
                if r == 1 then w = w + 1 elseif r == -1 then l = l + 1 end
            end
            return w, l, runs - w - l
        end

        function sim_trace_one(a, b, runa, runb, seed, forced_first, culta, cultb, ova, ovb)
            math.randomseed(seed)
            local aa, bb = sim_apply_runes(a, b, runa, runb, ova, ovb)
            local trace = {}
            local res = duel_real(aa, bb, culta, cultb, trace, forced_first)
            return res, aa, bb, trace
        end
        ''')
        self._build_cached = functools.lru_cache(maxsize=4096)(self._build_uncached)

    def _resolve_title_tuple(
        self,
        mode: str,
        use_title: bool,
        title_override: dict[str, int] | None,
        mode_overrides: dict[str, Any] | None = None,
    ) -> tuple[int, int, int, int, int] | None:
        if not use_title:
            return None
        mp = self.get_mode_profile(mode, mode_overrides)
        base = mp.get('title_bonus') or (
            SERVER30_TITLE_BONUS if mode == 'server30' else PERMANENT_TITLE_BONUS
        )
        if isinstance(title_override, dict) and not mp.get('ignore_team_overrides'):
            return (
                int(title_override.get('bp', base['bp'])),
                int(title_override.get('speed', base['speed'])),
                int(title_override.get('defense', base['defense'])),
                int(title_override.get('dodge', base['dodge'])),
                int(title_override.get('authority', base['authority'])),
            )
        return (int(base['bp']), int(base['speed']), int(base['defense']), int(base['dodge']), int(base['authority']))

    def _resolve_slot_contracts(
        self,
        tids: tuple[int, ...],
        washes: tuple[str, ...],
        explicit_contracts: tuple[int, ...],
        mp: dict[str, Any],
    ) -> tuple[int, ...]:
        base_clv = int(mp.get('contract_lv', 2))
        q7_default = mp.get('q7_contract_lv', None)
        q7_c2_limit = mp.get('q7_contract2_limit', None)
        if q7_default is None and q7_c2_limit is None:
            return tuple(0 if is_leader_tid(tid) else base_clv for tid in tids)

        q7_def_lv = int(q7_default if q7_default is not None else 1)
        limit = max(0, int(q7_c2_limit if q7_c2_limit is not None else 0))

        res = [0 if is_leader_tid(tid) else base_clv for tid in tids]
        q7_indices: list[int] = []
        for i, tid in enumerate(tids):
            if is_leader_tid(tid):
                continue
            if int(self.mercs[tid].get('quality', 0)) == 7:
                res[i] = q7_def_lv
                q7_indices.append(i)

        if limit <= 0 or not q7_indices:
            return tuple(res)

        assigned: list[int] = []
        # 1. Honor explicit contract==2 annotations up to limit
        for i in q7_indices:
            if explicit_contracts[i] == 2 and len(assigned) < limit:
                res[i] = 2
                assigned.append(i)

        rem = limit - len(assigned)
        if rem > 0:
            # 2. Deterministically pick the remaining top-gain quality-7 slots for contract 2
            wash_counts = collections.Counter(w for w in washes if w in PROPS)
            total_w = max(1, sum(wash_counts.values()))
            prop_weight = {p: 1.0 + 0.35 * (wash_counts[p] / total_w) for p in PROPS}
            force_prop = self.const['force_property']
            mc1 = self.cfg['mercenary_contract']['1']
            mc2 = self.cfg['mercenary_contract']['2']

            scored: list[tuple[float, int, int]] = []
            for i in q7_indices:
                if i in assigned:
                    continue
                tid = tids[i]
                k = str(tid)
                c1 = mc1.get(k, {})
                c2 = mc2.get(k, {})
                w_gain = 0.0
                raw_gain = 0
                for p in PROPS:
                    d = int(c2.get(p, 0)) - int(c1.get(p, 0))
                    raw_gain += d
                    w_gain += d * prop_weight[p]
                w_prop = washes[i]
                if self.mercs[tid].get('can_upgrade_force') and w_prop in force_prop and c2.get('contract_level') == 2:
                    fd = int(force_prop[w_prop])
                    raw_gain += fd
                    w_gain += fd * prop_weight[w_prop]
                scored.append((w_gain, raw_gain, -i))
            scored.sort(reverse=True)
            for _, _, neg_i in scored[:rem]:
                res[-neg_i] = 2

        return tuple(res)

    def _profile_cache_key(self, mp: dict[str, Any]) -> tuple[Any, ...]:
        return (
            int(mp.get('leader_level', 100)),
            int(mp.get('leader_wakeup', 8)),
            int(mp.get('leader_forge_lv', 10)),
            int(mp.get('merc_level', 100)),
            int(mp.get('merc_wakeup_cap', 99)),
            int(mp.get('merc_forge_lv', 30)),
            int(mp.get('artifact_lv', 15)),
            int(mp.get('contract_lv', 2)),
            int(mp.get('weapon_total_star_id', 13)),
            int(mp.get('leader_contract_id', 22)),
        )

    def _build_uncached(
        self,
        tids: tuple[int, ...],
        weapon_id: int,
        washes: tuple[str, ...],
        mode: str,
        title_tuple: tuple[int, int, int, int, int] | None,
        prof_key: tuple[Any, ...],
        slot_contracts: tuple[int, ...],
    ) -> Any:
        title = None
        if title_tuple is not None:
            title = self._lua_convert({
                'bp': title_tuple[0],
                'speed': title_tuple[1],
                'defense': title_tuple[2],
                'dodge': title_tuple[3],
                'authority': title_tuple[4],
            })
        prof_lua = self._lua_convert({
            'leader_level': prof_key[0],
            'leader_wakeup': prof_key[1],
            'leader_forge_lv': prof_key[2],
            'merc_level': prof_key[3],
            'merc_wakeup_cap': prof_key[4],
            'merc_forge_lv': prof_key[5],
            'artifact_lv': prof_key[6],
            'contract_lv': prof_key[7],
            'weapon_total_star_id': prof_key[8],
            'leader_contract_id': prof_key[9],
        })
        return self.lua.globals().sim_build_roster(
            self._lua_convert(list(tids)),
            weapon_id,
            self._lua_convert(list(washes)),
            title,
            mode,
            prof_lua,
            self._lua_convert(list(slot_contracts)),
        )

    def resolve_merc_id(self, ref: int | str) -> int:
        if isinstance(ref, int):
            if ref not in self.mercs:
                raise KeyError(f'Unknown mercenary ID: {ref}')
            return ref
        s = str(ref).strip()
        if s.isdigit():
            tid = int(s)
            if tid not in self.mercs:
                raise KeyError(f'Unknown mercenary ID: {tid}')
            return tid
        if s in self.merc_by_name:
            # Default to first allowed ID when unambiguous; user can pass exact ID for e.g. 小叔叔 (19000286 vs 88000030)
            cands = [t for t in self.merc_by_name[s] if t in self.allowed_tids or t == LEADER_TID]
            return cands[0] if cands else self.merc_by_name[s][0]
        raise KeyError(f'Unknown mercenary name/ID: {ref}')

    def resolve_rune_id(self, ref: int | str) -> int:
        if isinstance(ref, int):
            if ref not in self.rune_lv20_delta:
                raise KeyError(f'Unknown property rune ID: {ref}')
            return ref
        s = str(ref).strip()
        if s.isdigit():
            rid = int(s)
            if rid not in self.rune_lv20_delta:
                raise KeyError(f'Unknown property rune ID: {rid}')
            return rid
        if s in self.rune_by_name:
            # Pick highest quality (quality=6 IDs 56..75) by default
            cands = [r for r in self.rune_by_name[s] if r in self.rune_lv20_delta]
            if cands:
                return max(cands, key=lambda r: (self.runes[r]['quality'], r))
        raise KeyError(f'Unknown rune name/ID: {ref}')

    def resolve_weapon_id(self, ref: int | str) -> int:
        if isinstance(ref, int):
            if ref not in self.weapons:
                raise KeyError(f'Unknown weapon ID: {ref}')
            return ref
        s = str(ref).strip()
        if s.isdigit():
            wid = int(s)
            if wid not in self.weapons:
                raise KeyError(f'Unknown weapon ID: {wid}')
            return wid
        if s in self.weapon_by_name:
            return self.weapon_by_name[s]
        for wid, name in self.weapons.items():
            if s in name:
                return wid
        raise KeyError(f'Unknown weapon name/ID: {ref}')

    def parse_rune_spec(self, spec: str, default_lv: int = 20) -> list[tuple[int, int]]:
        """Parses shorthand rune specs like '5x防衰', '3x王衰+2x闪王', '59,59,59,59,59', or '66*3+69*2'."""
        alias_map = {
            '先衰': 57, '防衰': 59, '闪衰': 61, '王衰': 63,
            '防王': 66, '闪王': 69, '先王': 64, '先防': 58, '先闪': 60,
            '双防': 67, '双闪': 68, '双王': 75, '双先': 56, '防闪': 62,
            '防先': 65,
        }
        s = spec.strip()
        parts = [p.strip() for p in re.split(r'[+,，\s]+', s) if p.strip()]
        out: list[tuple[int, int]] = []
        for part in parts:
            m = re.match(r'^(\d+)[xX*×](.+)$', part)
            m_rev = re.match(r'^(.+)[xX*×](\d+)$', part)
            if m:
                cnt, tok = int(m.group(1)), m.group(2).strip()
            elif m_rev:
                tok, cnt = m_rev.group(1).strip(), int(m_rev.group(2))
            else:
                cnt, tok = 1, part
            rid = alias_map[tok] if tok in alias_map else self.resolve_rune_id(tok)
            for _ in range(cnt):
                out.append((rid, default_lv))
        if len(out) != 5:
            raise ValueError(f'Rune spec must resolve to exactly 5 runes, got {len(out)} from {spec!r}')
        return out

    def normalize_team(self, team: dict[str, Any]) -> dict[str, Any]:
        """Normalizes a team dictionary (names or IDs) into canonical IDs + washes + contracts + runes."""
        raw_slots = team.get('slots', [])
        if len(raw_slots) != 30:
            raise ValueError(f'Team must have exactly 30 slots, got {len(raw_slots)}')

        tids: list[int] = []
        washes: list[str] = []
        contracts: list[int] = []
        for idx, slot in enumerate(raw_slots):
            if isinstance(slot, dict):
                ref = slot.get('id', slot.get('name'))
                tid = self.resolve_merc_id(ref)
                w_raw = str(slot.get('wash', '') or '').strip().lower()
                c_raw = int(slot.get('contract', 0) or 0)
            else:
                tid = self.resolve_merc_id(slot)
                w_raw = ''
                c_raw = 0
            if w_raw not in WASH_ALIASES:
                raise ValueError(f'Slot {idx + 1} invalid wash: {w_raw}')
            w = '' if is_leader_tid(tid) else (WASH_ALIASES[w_raw] or 'authority')
            tids.append(tid)
            washes.append(w)
            contracts.append(0 if is_leader_tid(tid) else c_raw)

        weapon_ref = team.get('weapon_id', team.get('weapon', 9))
        weapon_id = self.resolve_weapon_id(weapon_ref)

        raw_runes = team.get('runes', [65, 65, 66, 66, 66])
        if len(raw_runes) != 5:
            raise ValueError(f'Must provide exactly 5 runes, got {len(raw_runes)}')
        runes_norm: list[tuple[int, int]] = []
        for r in raw_runes:
            if isinstance(r, dict):
                rid = self.resolve_rune_id(r.get('id', r.get('name')))
                lv = int(r.get('lv', r.get('level', 20)))
            elif isinstance(r, (list, tuple)) and len(r) == 2:
                rid = self.resolve_rune_id(r[0])
                lv = int(r[1])
            else:
                rid = self.resolve_rune_id(r)
                lv = 20
            runes_norm.append((rid, lv))

        cult = team.get('cultivation', DEFAULT_CULTIVATION)
        stats_ov = team.get('stats_override')
        title_bonus = team.get('title_bonus')

        return {
            'name': team.get('name', 'Custom'),
            'tids': tuple(tids),
            'washes': tuple(washes),
            'contracts': tuple(contracts),
            'weapon_id': weapon_id,
            'weapon_name': self.weapons[weapon_id],
            'runes': tuple(runes_norm),
            'rune_ids': tuple(rid for rid, _ in runes_norm),
            'cultivation': [[int(c[0]), int(c[1])] for c in cult],
            'stats_override': stats_ov,
            'title_bonus': title_bonus,
        }

    def validate_team(
        self,
        team: dict[str, Any],
        mode: str | None = None,
        mode_overrides: dict[str, Any] | None = None,
    ) -> list[str]:
        """Returns list of rule violations under verified server acquisition/formation caps (and optional mode limits)."""
        norm = self.normalize_team(team)
        errors: list[str] = []
        cnt = collections.Counter(norm['tids'])
        leader_cnt = sum(n for tid, n in cnt.items() if is_leader_tid(tid))
        if leader_cnt != 1:
            errors.append(f'主角(99000001+)必须恰好上阵 1 名，当前 {leader_cnt} 名')
        for tid, n in cnt.items():
            if is_leader_tid(tid):
                continue
            cap = self.caps.get(tid, 1)
            name = self.mercs[tid]['name']
            if n > cap:
                errors.append(f'{name}({tid}) 最多上阵 {cap} 名，当前 {n} 名')
            if tid not in self.allowed_tids:
                errors.append(f'{name}({tid}) 不在图鉴可招募/常规池中')
        if mode is not None:
            mp = self.get_mode_profile(mode, mode_overrides)
            q7_lim = mp.get('q7_contract2_limit', None)
            if q7_lim is not None:
                explicit_q7_c2 = sum(
                    1
                    for tid, clv in zip(norm['tids'], norm['contracts'])
                    if (not is_leader_tid(tid)) and int(self.mercs[tid].get('quality', 0)) == 7 and clv == 2
                )
                if explicit_q7_c2 > int(q7_lim):
                    errors.append(f'显式二契炫金超限: 最多允许 {int(q7_lim)} 名，当前标注 {explicit_q7_c2} 名')
        return errors

    def _effective_runes(
        self,
        norm: dict[str, Any],
        mp: dict[str, Any],
        mode_overrides: dict[str, Any] | None = None,
    ) -> tuple[tuple[int, int], ...]:
        if mp.get('no_runes'):
            return ()
        mirror = mp.get('mirror_runes')
        forced_lv = mp.get('rune_lv')
        if mirror:
            if isinstance(mirror, str):
                base_runes = self.parse_rune_spec(mirror, default_lv=int(forced_lv or 20))
            else:
                base_runes = [
                    (self.resolve_rune_id(r[0]), int(r[1])) if isinstance(r, (list, tuple)) else (self.resolve_rune_id(r), int(forced_lv or 20))
                    for r in mirror
                ]
            if forced_lv is not None:
                return tuple((rid, int(forced_lv)) for rid, _ in base_runes)
            return tuple(base_runes)
        if forced_lv is not None and (mp.get('ignore_team_overrides') or (mode_overrides and mode_overrides.get('rune_lv') is not None)):
            return tuple((rid, int(forced_lv)) for rid, _ in norm['runes'])
        return norm['runes']

    def _effective_cultivation(self, norm: dict[str, Any], mp: dict[str, Any]) -> list[list[int]]:
        if mp.get('ignore_team_overrides'):
            return [[int(c[0]), int(c[1])] for c in mp.get('cultivation', DEFAULT_CULTIVATION)]
        return norm['cultivation']

    def _rune_vec(self, runes_norm: tuple[tuple[int, int], ...]) -> tuple[int, ...]:
        acc = [0] * 8
        for rid, lv in runes_norm:
            d = self._rune_delta(rid, lv)
            for i in range(8):
                acc[i] += d[i]
        return tuple(acc)

    def _effective_override(
        self,
        norm: dict[str, Any],
        mode: str,
        mp: dict[str, Any] | None = None,
    ) -> dict[str, Any] | None:
        if mp and mp.get('ignore_team_overrides'):
            return None
        ov = norm.get('stats_override')
        if mode == 'server30' and isinstance(ov, dict):
            return {k: int(v) for k, v in ov.items() if k in ('max_bp',) + PROPS}
        return None

    def inspect_team(
        self,
        team: dict[str, Any],
        mode: str = 'max',
        use_title: bool = True,
        mode_overrides: dict[str, Any] | None = None,
    ) -> dict[str, Any]:
        norm = self.normalize_team(team)
        mp = self.get_mode_profile(mode, mode_overrides)
        tt = self._resolve_title_tuple(mode, use_title, norm.get('title_bonus'), mode_overrides)
        slot_contracts = self._resolve_slot_contracts(norm['tids'], norm['washes'], norm['contracts'], mp)
        t = self._build_cached(norm['tids'], norm['weapon_id'], norm['washes'], mode, tt, self._profile_cache_key(mp), slot_contracts)
        eff_runes = self._effective_runes(norm, mp, mode_overrides)
        rvec = self._rune_vec(eff_runes)
        ov = None if mp.get('ignore_team_overrides') else (norm.get('stats_override') if mode == 'server30' else None)
        base_stats = {p: int(ov[p]) if ov and p in ov else int(t[p]) for p in PROPS}
        max_bp = int(ov['max_bp']) if ov and 'max_bp' in ov else int(t.max_bp)
        after_runes = {p: base_stats[p] + rvec[i] for i, p in enumerate(PROPS)}
        enemy_debuff = {p: rvec[i + 4] for i, p in enumerate(PROPS)}

        skills_out = []
        cum_personal = 0
        cum_coop = 0
        for idx in range(1, len(t.skills) + 1):
            sk = t.skills[idx]
            sid = int(sk.ID)
            is_coop = bool(t.coop_skill_ids[sid])
            prob = int(sk.trigger_chance)
            if is_coop:
                cum_coop += prob
            else:
                cum_personal += prob
            skills_out.append({
                'order': idx,
                'id': sid,
                'name': sk.name,
                'prob': prob,
                'is_coop': is_coop,
                'effect_type': int(sk.effect_type),
                'cum_total': cum_coop + cum_personal,
                'turn1_masked': (not is_coop) and ((cum_coop + cum_personal - prob) >= 100),
            })

        slots_out = []
        q7_c2_slots = []
        q7_c1_slots = []
        for i, tid in enumerate(norm['tids']):
            clv = int(slot_contracts[i])
            qual = int(self.mercs[tid].get('quality', 0))
            s_entry = {
                'pos': i + 1,
                'id': tid,
                'name': self.mercs[tid]['name'],
                'quality': qual,
                'wash': norm['washes'][i],
                'contract': clv,
            }
            slots_out.append(s_entry)
            if (not is_leader_tid(tid)) and qual == 7:
                if clv >= 2:
                    q7_c2_slots.append({'pos': i + 1, 'id': tid, 'name': self.mercs[tid]['name']})
                else:
                    q7_c1_slots.append({'pos': i + 1, 'id': tid, 'name': self.mercs[tid]['name']})

        return {
            'name': norm['name'],
            'mode': mode,
            'weapon': {'id': norm['weapon_id'], 'name': norm['weapon_name']},
            'runes': [{'id': rid, 'lv': lv, 'name': self.runes[rid]['name']} for rid, lv in eff_runes],
            'max_bp': max_bp,
            'shield': int(t.shield),
            'base_stats': base_stats,
            'after_rune_stats': after_runes,
            'enemy_rune_debuff': enemy_debuff,
            'combos': list(t.combos.values()),
            'skills': skills_out,
            'violations': self.validate_team(team, mode=mode, mode_overrides=mode_overrides),
            'slots': slots_out,
            'q7_summary': {
                'total': len(q7_c2_slots) + len(q7_c1_slots),
                'c2_limit': mp.get('q7_contract2_limit', None),
                'c2_slots': q7_c2_slots,
                'c1_slots': q7_c1_slots,
            },
            'profile_effective': {
                'merc_level': int(mp.get('merc_level', 100)),
                'merc_wakeup_cap': int(mp.get('merc_wakeup_cap', 99)),
                'artifact_lv': int(mp.get('artifact_lv', 15)),
                'leader_contract_id': int(mp.get('leader_contract_id', 22)),
                'rune_lv': mp.get('rune_lv', 20),
                'q7_contract2_limit': mp.get('q7_contract2_limit', None),
                'ignore_team_overrides': bool(mp.get('ignore_team_overrides', False)),
                'equalize_bp': bool(mp.get('equalize_bp', False)),
                'no_runes': bool(mp.get('no_runes', False)),
                'mirror_runes': mp.get('mirror_runes', None),
            },
        }

    def _prepare_match(
        self,
        team_a: dict[str, Any],
        team_b: dict[str, Any],
        mode: str,
        use_title: bool,
        mode_overrides: dict[str, Any] | None = None,
    ) -> tuple[Any, Any, Any, Any, Any, Any, Any, Any]:
        na = self.normalize_team(team_a)
        nb = self.normalize_team(team_b)
        mp = self.get_mode_profile(mode, mode_overrides)
        pkey = self._profile_cache_key(mp)
        sca = self._resolve_slot_contracts(na['tids'], na['washes'], na['contracts'], mp)
        scb = self._resolve_slot_contracts(nb['tids'], nb['washes'], nb['contracts'], mp)
        ta = self._build_cached(na['tids'], na['weapon_id'], na['washes'], mode, self._resolve_title_tuple(mode, use_title, na.get('title_bonus'), mode_overrides), pkey, sca)
        tb = self._build_cached(nb['tids'], nb['weapon_id'], nb['washes'], mode, self._resolve_title_tuple(mode, use_title, nb.get('title_bonus'), mode_overrides), pkey, scb)
        ra = self._lua_convert(list(self._rune_vec(self._effective_runes(na, mp, mode_overrides))))
        rb = self._lua_convert(list(self._rune_vec(self._effective_runes(nb, mp, mode_overrides))))
        ca = self._lua_convert(self._effective_cultivation(na, mp))
        cb = self._lua_convert(self._effective_cultivation(nb, mp))
        ova_d = self._effective_override(na, mode, mp)
        ovb_d = self._effective_override(nb, mode, mp)
        if mp.get('equalize_bp'):
            bp_a = int(ova_d['max_bp']) if ova_d and 'max_bp' in ova_d else int(ta.max_bp)
            bp_b = int(ovb_d['max_bp']) if ovb_d and 'max_bp' in ovb_d else int(tb.max_bp)
            eq_bp = int(round((bp_a + bp_b) / 2))
            ova_d = dict(ova_d or {})
            ovb_d = dict(ovb_d or {})
            ova_d['max_bp'] = eq_bp
            ova_d['_scale_shield'] = True
            ovb_d['max_bp'] = eq_bp
            ovb_d['_scale_shield'] = True
        ova = self._lua_convert(ova_d) if ova_d else None
        ovb = self._lua_convert(ovb_d) if ovb_d else None
        return ta, tb, ra, rb, ca, cb, ova, ovb

    def simulate_series(
        self,
        team_a: dict[str, Any],
        team_b: dict[str, Any],
        runs_each: int = 500,
        seed: int = 20261125,
        mode: str = 'max',
        use_title: bool = True,
        symmetric: bool = True,
        mode_overrides: dict[str, Any] | None = None,
    ) -> dict[str, Any]:
        ta, tb, ra, rb, ca, cb, ova, ovb = self._prepare_match(team_a, team_b, mode, use_title, mode_overrides)

        w1, l1, d1 = self.lua.globals().sim_series(ta, tb, ra, rb, runs_each, seed, ca, cb, ova, ovb)
        if not symmetric:
            tot = runs_each
            return {
                'wins': int(w1), 'losses': int(l1), 'draws': int(d1), 'total': tot,
                'winrate': (w1 + 0.5 * d1) / tot,
            }
        w2, l2, d2 = self.lua.globals().sim_series(tb, ta, rb, ra, runs_each, seed + 1337, cb, ca, ovb, ova)
        wins = int(w1 + l2)
        losses = int(l1 + w2)
        draws = int(d1 + d2)
        tot = 2 * runs_each
        return {
            'wins': wins, 'losses': losses, 'draws': draws, 'total': tot,
            'winrate': (wins + 0.5 * draws) / tot,
            'a_as_left': {'wins': int(w1), 'losses': int(l1), 'draws': int(d1)},
            'a_as_right': {'wins': int(l2), 'losses': int(w2), 'draws': int(d2)},
        }

    def simulate_trace(
        self,
        team_a: dict[str, Any],
        team_b: dict[str, Any],
        seed: int = 20261125,
        mode: str = 'max',
        use_title: bool = True,
        forced_first: str | None = None,
        mode_overrides: dict[str, Any] | None = None,
    ) -> dict[str, Any]:
        ta, tb, ra, rb, ca, cb, ova, ovb = self._prepare_match(team_a, team_b, mode, use_title, mode_overrides)

        res, aa, bb, trace = self.lua.globals().sim_trace_one(ta, tb, ra, rb, seed, forced_first, ca, cb, ova, ovb)
        events = []
        for idx in range(1, len(trace) + 1):
            ev = trace[idx]
            events.append({
                'round': int(ev.round),
                'turn': int(ev.turn),
                'actor': ev.actor,
                'skill_id': int(ev.skill_id),
                'skill_name': ev.skill_name,
                'effect_type': int(ev.effect_type),
                'is_coop': bool(ev.is_coop),
                'is_dodge': bool(ev.is_dodge),
                'dmg_to_enemy': int(ev.dmg_to_enemy),
                'self_heal': int(ev.self_heal),
                'sealed': bool(ev.sealed),
                'seal_turn': int(ev.seal_turn),
                'seal_cond1': int(ev.seal_cond1),
                'a_bp': int(ev.a_bp),
                'b_bp': int(ev.b_bp),
            })

        return {
            'winner': 'A' if res == 1 else ('B' if res == -1 else 'Draw'),
            'rounds': len(events),
            'a_effective': {'max_bp': int(aa.max_bp), **{p: int(aa[p]) for p in PROPS}},
            'b_effective': {'max_bp': int(bb.max_bp), **{p: int(bb[p]) for p in PROPS}},
            'events': events,
        }

    def get_catalog(self) -> dict[str, Any]:
        mercs_list = [
            {
                'id': tid,
                'name': row['name'],
                'quality': row.get('quality', 0),
                'cap': 1 if tid == LEADER_TID else self.caps[tid],
                'can_wash': bool(row.get('can_upgrade_force')),
                'is_leader': tid == LEADER_TID,
                'note': f"{row['name']} ({tid})",
            }
            for tid, row in sorted(self.mercs.items())
            if tid in self.allowed_tids or tid == LEADER_TID
        ]
        runes_list = [
            {
                'id': rid,
                'name': self.runes[rid]['name'],
                'quality': self.runes[rid]['quality'],
                'delta': self.rune_lv20_delta[rid],
            }
            for rid in sorted(self.rune_lv20_delta)
            if self.runes[rid]['quality'] >= 5
        ]
        weapons_list = [{'id': wid, 'name': name} for wid, name in sorted(self.weapons.items())]
        return {'mercenaries': mercs_list, 'runes': runes_list, 'weapons': weapons_list}
