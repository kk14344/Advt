-- In-client zero-sleep event-driven full-auto formation deployer for 冒险与挖矿 (test-server).
-- Driven by network return events & game update ticks (zero external CDP sleep loops).
-- Strictly obeys safety rails:
--   1. Never modifies wash/training/membership of any mercenary in protected formations (F1..F4 when fid=5).
--   2. Tier 1 (maxed exact-wash) > Tier 2 (idle non-protected) > Tier 3 (LibraryRecruit).
--   3. Full-auto Lv30 / Wake5 / Weapon30 (max extra_chance) / Artifact15 / Contract2 / Force35 / per-merc exact wash,
--      with 200x blood-diamond store auto-restock on resource shortage.
--   4. Safe rune setup: SwitchLoadout / EquipRune / DrawRune(auto_compose=false) + ExchangeRuneProperty ONLY.
--      NEVER calls UpgradeRune! NEVER destroys any rune!

local M = _G.__ANTIGRA_AUTO_DEPLOY
if M and M.cleanup then
    pcall(M.cleanup)
end

local network = require "util.network"
local tl = require "logic.troop"
local rl = require "logic.rune"
local dw = require "logic.destiny_weapon"
local res = require "logic.resource"
local cm = require "logic.config_manager"
local constants = require "util.constants"
local graphic_ok, graphic = pcall(require, "logic.graphic")

local store_ok, store_logic = pcall(require, "logic.store")

local DEPLOYER_VERSION = "3.0-serial-zero-stall"

local STATE = {
    running = false,
    done = false,
    ok = false,
    err = nil,
    phase = "init",
    logs = {},
    stats = {
        reused_tier1 = 0,
        reused_tier2 = 0,
        recruited_tier3 = 0,
        restocks = 0,
        forges = 0,
        washes = 0,
        rune_draws = 0,
        rune_exchanges = 0,
        packets = 0,
        watchdogs = 0,
    },
    phase_sec = { recruit = 0, formation = 0, train = 0, runes = 0 },
    result = nil,
    _cfg = nil,
    _plan = nil,
    _train_queue = {},
    _train_idx = 1,
    _merc_map = {},
    _merc_done = {},
    _inflight = nil,
    _inflight_ts = 0,
    _inflight_mid = nil,
    _inflight_op = nil,
    _phase_t0 = 0,
    _allow_bp_calc = false,
    _orig_update = nil,
    _hooks_installed = false,
}

local function log(msg)
    STATE.logs[#STATE.logs + 1] = string.format("[%.2fs] %s", os.clock() - (STATE._t0 or os.clock()), tostring(msg))
    if #STATE.logs > 300 then
        table.remove(STATE.logs, 1)
    end
end

local function fail(msg)
    STATE.running = false
    STATE.done = true
    STATE.ok = false
    STATE.err = tostring(msg)
    log("ERROR: " .. STATE.err)
end

local function finish(res_tbl)
    STATE.running = false
    STATE.done = true
    STATE.ok = true
    STATE.result = res_tbl
    log("ALL_DONE")
end

-- Wash name <-> ex_prop_type
local WASH_CODE = { speed = 1, defense = 2, dodge = 3, authority = 4 }
local function wash_min_val(wcode)
    return (wcode == 4) and 2 or 5
end

local function merc_max_wake(m)
    local mw = (m.template_info and m.template_info.max_wakeup) or 5
    return math.min(mw, 5)
end

local function merc_has_artifact(m)
    return m.template_info and m.template_info.have_artifact and true or false
end

local function merc_can_upgrade_art(m)
    return m.template_info and m.template_info.have_artifact_upgrade and true or false
end

local function merc_max_contract(m)
    local tid = m.template_id
    if tl.CanContractLv then
        if tl:CanContractLv(tid, 2) then return 2 end
        if tl:CanContractLv(tid, 1) then return 1 end
        return 0
    end
    return 2
end

local function is_fully_trained(m)
    if (m.level or 0) < 30 then return false end
    if (m.wakeup or 0) < merc_max_wake(m) then return false end
    if (m.weapon_lv or 0) < 30 then return false end
    if merc_has_artifact(m) then
        if not m.is_open_artifact then return false end
        if merc_can_upgrade_art(m) and (m.artifact_lv or 0) < 15 then return false end
    end
    if (m.contract_lv or 0) < merc_max_contract(m) then return false end
    if m.template_info and m.template_info.can_upgrade_force then
        if (m.force_lv or 0) < (constants["MAX_FORCE_LEVEL"] or 35) then return false end
    end
    return true
end

local function is_wash_satisfied(m, wcode)
    if not wcode or wcode == 0 then return true end
    if not (m.template_info and m.template_info.can_upgrade_force) then return true end
    return (m.ex_prop_type or 0) == wcode and (m.ex_prop_val or 0) >= wash_min_val(wcode)
end

local function is_temp_wash_satisfied(m, wcode)
    if not wcode or wcode == 0 then return false end
    return (m.ex_prop_type_temp or 0) == wcode and (m.ex_prop_val_temp or 0) >= wash_min_val(wcode)
end

local function is_tier1_ready(m, wcode)
    return is_fully_trained(m) and is_wash_satisfied(m, wcode)
end

local function best_extra_chance(m)
    local wlv = m.weapon_lv or 0
    local wf = cm.weapon_forge_config and cm.weapon_forge_config[wlv + 1]
    local base_ch = wf and (wf.success_chance or wf.chance or 100) or 100
    local lucky = 0
    if tl.forge_info and tl.forge_info.mercenary_id == m.instance_id then
        lucky = tl.forge_info.lucky_num or 0
    end
    local need = math.max(0, 100 - base_ch - lucky)
    if need <= 0 then return 0 end
    local ex_cfg = cm.weapon_forge_extra_config
    if not ex_cfg then return 0 end
    -- Find smallest key >= need, or largest available key
    local best_ge, max_k = nil, 0
    for k, _ in pairs(ex_cfg) do
        local kn = tonumber(k)
        if kn then
            if kn > max_k then max_k = kn end
            if kn >= need and (not best_ge or kn < best_ge) then best_ge = kn end
        end
    end
    return best_ge or max_k or 0
end

-- Find resource_type key that is currently lacking for an action, and buy 200x from blood-diamond store
local function try_restock(res_type_id)
    if not STATE._cfg.auto_restock then return false end
    if STATE.stats.restocks >= 30 then return false end
    if not store_ok or not store_logic then return false end

    local goods_list = store_logic.goods_list
    local has_goods = false
    if goods_list then
        for _ in pairs(goods_list) do has_goods = true; break end
    end
    if not has_goods then
        if not STATE._store_queried then
            STATE._store_queried = true
            STATE._inflight = "store_query"
            STATE._inflight_ts = os.clock()
            STATE.stats.packets = STATE.stats.packets + 1
            if store_logic.Query then
                store_logic:Query()
            else
                network:Send({ query_store_info = {} })
            end
            log("Querying blood-diamond store (store_logic:Query)...")
            return true
        end
        return false
    end

    local target_idx, target_goods = nil, nil
    -- 1. Preferred native lookup: store_logic:GetResourceGoodsIndex(res_type_id)
    if res_type_id and store_logic.GetResourceGoodsIndex then
        local ok_idx, idx = pcall(function() return store_logic:GetResourceGoodsIndex(res_type_id) end)
        if ok_idx and idx and idx > 0 then
            target_idx = idx
            target_goods = (store_logic.GetGoodsInfo and store_logic:GetGoodsInfo(idx)) or goods_list[idx]
        end
    end
    -- 2. Fallback scan across goods_list (field 'data' holds resource_type!)
    if not target_idx and res_type_id then
        for idx, g in ipairs(goods_list) do
            local ginfo = g.goods_info or g
            local rid = ginfo.data or ginfo.resource_type or ginfo.item_id or g.data or g.resource_type
            if rid and tonumber(rid) == tonumber(res_type_id) then
                target_idx = idx
                target_goods = g
                break
            end
        end
    end
    if not target_idx or not target_goods then
        log("Auto-restock: no store goods found for res_type_id=" .. tostring(res_type_id))
        return false
    end
    local max_buy = 500
    if target_goods.max_buy_count and target_goods.already_buy_count then
        local rem = target_goods.max_buy_count - target_goods.already_buy_count
        if rem > 0 and rem < max_buy then max_buy = rem end
    end
    if max_buy <= 0 then return false end
    local gid = target_goods.id or target_goods.goods_id
    STATE.stats.restocks = STATE.stats.restocks + 1
    STATE._inflight = "store_buy"
    STATE._inflight_ts = os.clock()
    STATE.stats.packets = STATE.stats.packets + 1
    log(string.format("Auto-restock #%d: buy_store_goods(id=%s, qty=%d, res=%s)", STATE.stats.restocks, tostring(gid), max_buy, tostring(res_type_id)))
    if gid then
        network:Send({ buy_store_goods = { goods_id = gid, num = max_buy } })
    else
        store_logic:BuyGoods(target_idx, max_buy)
    end
    return true
end

local function detect_missing_resource_for_merc(m)
    local RT = constants["RESOURCE_TYPE"] or {}
    -- Exp
    if (m.level or 1) < 30 then
        local exp_cfg = cm.mercenary_exp_config and cm.mercenary_exp_config[29]
        local need = exp_cfg and ((exp_cfg["wakeup_factor" .. (m.wakeup or 1)] or 0) - (m.exp or 0)) or 0
        if need > 0 and (res:GetResourceNum(RT["exp"]) or 0) < need then return RT["exp"] end
    end
    -- Wakeup
    if (m.level or 1) >= 30 and (m.wakeup or 1) < merc_max_wake(m) then
        local wc = cm.wakeup_info_config and cm.wakeup_info_config[m.wakeup]
        if wc then
            if (res:GetResourceNum(RT["gold_coin"]) or 0) < (wc.gold_coin or 0) then return RT["gold_coin"] end
            if wc.resource_id and (res:GetResourceNum(wc.resource_id) or 0) < (wc.resource_num or 0) then return wc.resource_id end
        end
    end
    -- Force / Wash
    if (m.wakeup or 1) >= 5 and m.template_info and m.template_info.can_upgrade_force then
        if (m.force_lv or 0) < (constants["MAX_FORCE_LEVEL"] or 35) then
            if (res:GetResourceNum(RT["golem"]) or 0) < 5 then return RT["golem"] end
            if (res:GetResourceNum(RT["soul_chip"]) or 0) < 500 then return RT["soul_chip"] end
        else
            local cc = constants.CHANGE_EX_PROPERTY_RESOURCE
            if cc then
                if (res:GetResourceNum(RT["gold_coin"]) or 0) < (cc.gold_coin or 0) then return RT["gold_coin"] end
                if (res:GetResourceNum(RT["golem"]) or 0) < (cc.golem or 0) then return RT["golem"] end
                if (res:GetResourceNum(RT["soul_chip"]) or 0) < ((m.template_info.soul_chip or 0) * (cc.scale or 1)) then return RT["soul_chip"] end
            end
        end
    end
    -- Forge weapon resources
    if (m.weapon_lv or 0) < 30 then
        local wf = cm.weapon_forge_config and cm.weapon_forge_config[(m.weapon_lv or 0) + 1]
        if wf then
            for _, rname in ipairs({"copper","tin","iron","silver","gold","diamond","titan_iron","ruby","purple_gem","emerald","topaz","inferno_brimstone","time_sand","forge_pt"}) do
                local need = wf[rname] or 0
                if need > 0 and RT[rname] and (res:GetResourceNum(RT[rname]) or 0) < need then
                    return RT[rname]
                end
            end
        end
    end
    -- Artifact open/upgrade
    if merc_has_artifact(m) and (m.weapon_lv or 0) >= 20 then
        if not m.is_open_artifact then
            if (res:GetResourceNum(RT["forge_ticket"]) or 0) < 1 then
                if (res:GetResourceNum(RT["gold_coin"]) or 0) < 95000000 then return RT["gold_coin"] end
                for _, rname in ipairs({"red_soul_crystal","green_soul_crystal","light_soul_crystal","dark_soul_crystal"}) do
                    if (res:GetResourceNum(RT[rname]) or 0) < 6 then return RT[rname] end
                end
            end
        elseif merc_can_upgrade_art(m) and (m.artifact_lv or 1) < 15 then
            local ac = cm.mercenary_artifact_config and cm.mercenary_artifact_config[m.template_id] and cm.mercenary_artifact_config[m.template_id][m.artifact_lv or 1]
            if ac and ac.cost_list then
                for rid, need in pairs(ac.cost_list) do
                    if (res:GetResourceNum(rid) or 0) < need then return rid end
                end
            end
        end
    end
    -- Contract
    if (m.contract_lv or 0) < merc_max_contract(m) then
        local cf = tl:GetContractConf(m.instance_id, (m.contract_lv or 0) + 1)
        if cf then
            if (res:GetResourceNum(RT["soul_chip"]) or 0) < (cf.soul_chip or 0) then return RT["soul_chip"] end
            if (res:GetResourceNum(RT["contract_stone"]) or 0) < (cf.contract_stone or 0) then return RT["contract_stone"] end
        end
    end
    return nil
end

-- Forward declaration
local step_machine

local function build_reuse_plan(cfg)
    local fid = cfg.fid
    local in_protected = {}
    local in_target = {}
    for f = 1, 5 do
        for _, m in ipairs(tl:GetFormationMercenaryList(f) or {}) do
            if f == fid then
                in_target[m.instance_id] = true
            else
                in_protected[m.instance_id] = true
            end
        end
    end

    local owned_by_tid = {}
    local leader_merc = nil
    for id, m in pairs(tl:GetMercenaryList() or {}) do
        if m.template_id >= 99000001 then
            leader_merc = m
        else
            owned_by_tid[m.template_id] = owned_by_tid[m.template_id] or {}
            table.insert(owned_by_tid[m.template_id], m)
        end
    end
    if not leader_merc then return nil, "Leader mercenary not found" end

    -- Compute slot-invariant wash budget
    local budget = { speed = 0, defense = 0, dodge = 0, authority = 0 }
    for _, s in ipairs(cfg.slots) do
        if s.id < 99000001 and s.wash and s.wash ~= "" then
            budget[s.wash] = (budget[s.wash] or 0) + 1
        end
    end

    local used = {}
    local plan = {}
    for idx, s in ipairs(cfg.slots) do
        local is_l = (s.id >= 99000001)
        plan[idx] = {
            pos = idx,
            tid = s.id,
            name = s.name,
            is_leader = is_l,
            wash = is_l and "" or nil,
            inst = is_l and leader_merc.instance_id or nil,
            tier = is_l and 0 or nil,
        }
        if is_l then used[leader_merc.instance_id] = true end
    end

    local function find_tier1(tid, wname)
        local wcode = WASH_CODE[wname]
        local best = nil
        for _, m in ipairs(owned_by_tid[tid] or {}) do
            if not used[m.instance_id] and is_tier1_ready(m, wcode) then
                if not best
                    or (in_target[m.instance_id] and not in_target[best.instance_id])
                    or ((m.wakeup or 0) > (best.wakeup or 0))
                    or (((m.wakeup or 0) == (best.wakeup or 0)) and m.instance_id < best.instance_id) then
                    best = m
                end
            end
        end
        return best
    end

    -- Pass 1 (Tier 1): Rarer washes first, prefer slots without majority wash ready
    local wash_order = {}
    for w, c in pairs(budget) do if c > 0 then wash_order[#wash_order + 1] = { w = w, c = c } end end
    table.sort(wash_order, function(a, b) return a.c < b.c end)
    local maj_w = wash_order[#wash_order] and wash_order[#wash_order].w or "defense"

    if cfg.reuse then
        for _, wo in ipairs(wash_order) do
            local w = wo.w
            local passes = (w == maj_w) and { false } or { true, false }
            for _, no_maj in ipairs(passes) do
                for _, p in ipairs(plan) do
                    if budget[w] <= 0 then break end
                    if not p.is_leader and not p.inst then
                        local m = find_tier1(p.tid, w)
                        if m and (not no_maj or not find_tier1(p.tid, maj_w)) then
                            p.wash = w
                            p.inst = m.instance_id
                            p.tier = 1
                            used[m.instance_id] = true
                            budget[w] = budget[w] - 1
                            STATE.stats.reused_tier1 = STATE.stats.reused_tier1 + 1
                        end
                    end
                end
            end
        end

        -- Pass 2 (Tier 2): Idle mercs NOT in F1..F4 (including already-in-F5 unmaxed mercs!)
        -- Assign remaining wash budget labels first (preferring slots where an idle merc ALREADY matches a wash!)
        local function train_score(m)
            return (in_target[m.instance_id] and 10000 or 0)
                + (m.level or 0) * 10
                + (m.wakeup or 0) * 100
                + (m.weapon_lv or 0) * 15
                + (m.artifact_lv or 0) * 20
                + (m.contract_lv or 0) * 100
                + (m.force_lv or 0) * 5
        end
        local function find_tier2(tid, prefer_wcode)
            local best, best_sc = nil, -1
            for _, m in ipairs(owned_by_tid[tid] or {}) do
                if not used[m.instance_id] and not in_protected[m.instance_id] then
                    local sc = train_score(m) + ((prefer_wcode and m.ex_prop_type == prefer_wcode) and 500 or 0)
                    if sc > best_sc then best, best_sc = m, sc end
                end
            end
            return best
        end

        if cfg.reuse_idle then
            for _, p in ipairs(plan) do
                if not p.is_leader and not p.inst then
                    -- Pick a wash from remaining budget
                    local chosen_w = nil
                    for _, wo in ipairs(wash_order) do
                        if budget[wo.w] > 0 then
                            local m_match = find_tier2(p.tid, WASH_CODE[wo.w])
                            if m_match and m_match.ex_prop_type == WASH_CODE[wo.w] then
                                chosen_w = wo.w
                                break
                            end
                        end
                    end
                    if not chosen_w then
                        for _, wo in ipairs(wash_order) do
                            if budget[wo.w] > 0 then chosen_w = wo.w; break end
                        end
                    end
                    local m = find_tier2(p.tid, chosen_w and WASH_CODE[chosen_w] or nil)
                    if m and chosen_w then
                        p.wash = chosen_w
                        p.inst = m.instance_id
                        p.tier = 2
                        used[m.instance_id] = true
                        budget[chosen_w] = budget[chosen_w] - 1
                        STATE.stats.reused_tier2 = STATE.stats.reused_tier2 + 1
                    end
                end
            end
        end
    end

    -- Pass 3 (Tier 3): Remaining slots need fresh recruit
    for _, p in ipairs(plan) do
        if not p.is_leader and not p.inst then
            local chosen_w = nil
            for _, wo in ipairs(wash_order) do
                if budget[wo.w] > 0 then chosen_w = wo.w; break end
            end
            p.wash = chosen_w or "defense"
            if budget[p.wash] then budget[p.wash] = budget[p.wash] - 1 end
            p.tier = 3
            STATE.stats.recruited_tier3 = STATE.stats.recruited_tier3 + 1
        end
    end

    return plan, nil
end

local function set_phase(next_phase)
    local now = os.clock()
    if STATE._phase_t0 and STATE._phase_t0 > 0 and STATE.phase_sec[STATE.phase] ~= nil then
        STATE.phase_sec[STATE.phase] = STATE.phase_sec[STATE.phase] + (now - STATE._phase_t0)
    end
    STATE.phase = next_phase
    STATE._phase_t0 = now
end

local function set_inflight(tag, mid, op)
    STATE._inflight = tag
    STATE._inflight_ts = os.clock()
    STATE._inflight_mid = mid
    STATE._inflight_op = op
    STATE.stats.packets = STATE.stats.packets + 1
end

-- Phase 1: Recruit missing Tier 3 mercs (strict global in_flight=1)
local function step_recruit()
    for _, p in ipairs(STATE._plan) do
        if p.tier == 3 and not p.inst then
            local lib = tl:GetMercenaryLibraryCount(p.tid)
            if lib == nil then
                return fail(string.format("Merc %s (%d) not unlocked in library!", p.name, p.tid))
            end
            if lib <= 0 then
                local mc = cm.mercenary_config[p.tid]
                if mc and mc.is_unique then
                    return fail(string.format("Merc %s (%d) has 0 soul stones and is unique!", p.name, p.tid))
                end
                set_inflight("craft:" .. tostring(p.tid))
                log("Crafting soul stone for " .. p.name)
                tl:CraftSoulStone(p.tid)
                return
            end
            local pre = {}
            for id, m in pairs(tl:GetMercenaryList() or {}) do
                if m.template_id == p.tid then pre[id] = true end
            end
            STATE._recruit_target = { p = p, pre = pre }
            set_inflight("recruit:" .. tostring(p.tid))
            tl:LibraryRecruit(p.tid)
            return
        end
    end
    set_phase("formation")
    log("Phase -> formation")
    step_machine()
end

-- Phase 2: Formation diff & weapon equip (strict global in_flight=1, zero 9.6s stall!)
local function step_formation()
    if tl.formation_member_inflight ~= nil or tl.formation_members_refresh == true then
        return
    end
    local fid = STATE._cfg.fid
    local target_set = {}
    local exact_ids = {}
    for i, p in ipairs(STATE._plan) do
        target_set[p.inst] = true
        exact_ids[i] = p.inst
    end
    local cur_list = tl:GetFormationMercenaryList(fid) or {}
    local cur_set = {}
    for _, m in ipairs(cur_list) do cur_set[m.instance_id] = true end

    local order_ok = (#cur_list == #exact_ids)
    local same_members = (#cur_list == #exact_ids)
    if same_members then
        for i = 1, #exact_ids do
            if not cur_set[exact_ids[i]] then same_members = false; break end
            if cur_list[i].instance_id ~= exact_ids[i] then order_ok = false end
        end
    end

    -- Case A: Same member set, only order differs -> 1-packet AdjustMercenaryPosition (~0.1s!)
    if same_members and not order_ok then
        set_inflight("reorder")
        tl:AdjustMercenaryPosition(fid, exact_ids)
        if tl.FlushFormationPositionSaves then tl:FlushFormationPositionSaves() end
        return
    end

    -- Case B: Members differ -> prefer 1-packet ReplaceMercenaryFromFormation (29 pkts instead of 58!)
    if not same_members then
        if not STATE._replace_unsupported and tl.ReplaceMercenaryFromFormation then
            local excess_old, missing_new = nil, nil
            for _, m in ipairs(cur_list) do
                if m.template_id < 99000001 and not target_set[m.instance_id] then
                    excess_old = m.instance_id; break
                end
            end
            if excess_old then
                for _, inst in ipairs(exact_ids) do
                    if not cur_set[inst] then missing_new = inst; break end
                end
            end
            if excess_old and missing_new then
                local before_cnt = STATE._send_seq or 0
                set_inflight("replace:" .. tostring(excess_old) .. "->" .. tostring(missing_new))
                local ok_rep = tl:ReplaceMercenaryFromFormation(fid, excess_old, missing_new)
                if ok_rep ~= false and (STATE._send_seq or 0) > before_cnt then
                    return
                end
                STATE._inflight = nil
                STATE.stats.packets = STATE.stats.packets - 1
                STATE._replace_unsupported = true
                log("ReplaceMercenaryFromFormation did not send, using Rest+Insert")
            end
        end

        for _, m in ipairs(cur_list) do
            if m.template_id < 99000001 and not target_set[m.instance_id] then
                set_inflight("rest:" .. tostring(m.instance_id))
                tl:RestMercenary(fid, m.instance_id)
                return
            end
        end
        for _, inst in ipairs(exact_ids) do
            if not cur_set[inst] then
                set_inflight("insert:" .. tostring(inst))
                tl:InsertMercenaryToFormation(fid, inst, #cur_list + 1)
                return
            end
        end
    end

    local pending_pos = (tl.formation_position_pending and tl.formation_position_pending[fid] ~= nil) or (tl.formation_position_inflight ~= nil)
    if pending_pos then return end

    if tl:GetFormationWeaponId(fid) ~= STATE._cfg.weapon_id then
        set_inflight("weapon")
        dw:Equip(fid, STATE._cfg.weapon_id)
        return
    end

    -- Build training/wash queue
    STATE._train_queue = {}
    STATE._merc_map = {}
    STATE._merc_done = {}
    if STATE._cfg.full_auto then
        for _, p in ipairs(STATE._plan) do
            if not p.is_leader and p.tier >= 2 then
                local m = tl:GetMercenaryInfo(p.inst)
                if m and (not is_fully_trained(m) or not is_wash_satisfied(m, WASH_CODE[p.wash])) then
                    STATE._train_queue[#STATE._train_queue + 1] = p
                    STATE._merc_map[p.inst] = p
                end
            end
        end
    end
    STATE._train_idx = 0
    set_phase("train")
    log("Phase -> train (queue=" .. tostring(#STATE._train_queue) .. ", strict global in_flight=1)")
    step_machine()
end

-- Advance a single mercenary by 1 atomic step; returns true if done, false if sent or restocking
local function advance_one_merc(p)
    local mid = p.inst
    if STATE._merc_done[mid] then return true end
    if STATE._inflight ~= nil then return false end

    local m = tl:GetMercenaryInfo(mid)
    if not m then fail("Merc vanished inst=" .. tostring(mid)); return false end
    local wcode = WASH_CODE[p.wash]

    if tl.forge_weapon_inflight ~= nil then
        tl.forge_weapon_inflight = nil
    end

    local function mark(op)
        set_inflight("merc:" .. tostring(mid) .. ":" .. op, mid, op)
    end

    -- 1. Level to 30 (honor CAN_WAKEUP_LEVEL cap when wakeup < max_wakeup)
    if (m.level or 1) < 30 then
        local miss = detect_missing_resource_for_merc(m)
        if miss then
            if try_restock(miss) then return false end
            fail("Resource short for exp (res=" .. tostring(miss) .. ")"); return false
        end
        local max_lv = 30
        local max_wk = (m.template_info and m.template_info.max_wakeup) or 5
        if (m.wakeup or 1) < max_wk and constants["CAN_WAKEUP_LEVEL"] then
            max_lv = math.min(max_lv, constants["CAN_WAKEUP_LEVEL"])
        end
        local delta = math.min(max_lv - (m.level or 1), STATE._exp_step_cap or 29)
        if delta > 0 then
            mark("exp")
            network:Send({ alloc_mercenary_exp = { mercenary_id = mid, target_level = (m.level or 1) + delta } })
            return false
        end
    end

    -- 2. Wakeup to 5
    if (m.wakeup or 1) < merc_max_wake(m) then
        local miss = detect_missing_resource_for_merc(m)
        if miss and try_restock(miss) then return false end
        mark("wake")
        network:Send({ upgrade_mercenary_wakeup = { mercenary_id = mid } })
        return false
    end

    -- 3. Weapon forge up to 20 -> Open Artifact -> Weapon forge up to 30
    if (m.weapon_lv or 0) < 20 then
        local miss = detect_missing_resource_for_merc(m)
        if miss and try_restock(miss) then return false end
        local ex_ch = STATE._cfg.max_forge_chance and best_extra_chance(m) or 0
        mark("forge")
        STATE.stats.forges = STATE.stats.forges + 1
        network:Send({ forge_mercenary_weapon = { mercenary_id = mid, extra_chance = ex_ch } })
        return false
    end

    if merc_has_artifact(m) and not m.is_open_artifact then
        local miss = detect_missing_resource_for_merc(m)
        if miss and try_restock(miss) then return false end
        local RT = constants["RESOURCE_TYPE"] or {}
        local open_type = ((res:GetResourceNum(RT["forge_ticket"]) or 0) >= 1) and "ticket" or "gold"
        mark("open_art")
        network:Send({ open_mercenary_artifact = { mercenary_id = mid, open_type = open_type } })
        return false
    end

    if (m.weapon_lv or 0) < 30 then
        local miss = detect_missing_resource_for_merc(m)
        if miss and try_restock(miss) then return false end
        local ex_ch = STATE._cfg.max_forge_chance and best_extra_chance(m) or 0
        mark("forge")
        STATE.stats.forges = STATE.stats.forges + 1
        network:Send({ forge_mercenary_weapon = { mercenary_id = mid, extra_chance = ex_ch } })
        return false
    end

    -- 4. Artifact upgrade to 15
    if merc_has_artifact(m) and m.is_open_artifact and merc_can_upgrade_art(m) and (m.artifact_lv or 1) < 15 then
        local miss = detect_missing_resource_for_merc(m)
        if miss and try_restock(miss) then return false end
        mark("up_art")
        tl:UpdateArtifactGrade(mid)
        return false
    end

    -- 5. Contract up to max (2)
    if (m.contract_lv or 0) < merc_max_contract(m) then
        local miss = detect_missing_resource_for_merc(m)
        if miss and try_restock(miss) then return false end
        mark("contract")
        network:Send({ sign_contract = { mercenary_id = mid } })
        return false
    end

    -- 6. Force level up to 35
    if m.template_info and m.template_info.can_upgrade_force and (m.force_lv or 0) < (constants["MAX_FORCE_LEVEL"] or 35) then
        local miss = detect_missing_resource_for_merc(m)
        if miss and try_restock(miss) then return false end
        mark("force")
        network:Send({ upgrade_mercenary_force = { mercenary_id = mid } })
        return false
    end

    -- 7. Per-mercenary exact wash (NEVER touches Tier 1 mercs!)
    if not is_wash_satisfied(m, wcode) then
        if is_temp_wash_satisfied(m, wcode) then
            mark("wash_rep")
            tl:ReplaceExProperty(mid, true)
            return false
        end
        local miss = detect_missing_resource_for_merc(m)
        if miss and try_restock(miss) then return false end
        mark("wash")
        STATE.stats.washes = STATE.stats.washes + 1
        tl:ChangeExProperty(mid)
        return false
    end

    STATE._merc_done[mid] = true
    log(string.format("Trained & washed #%02d %s (inst=%d -> lv%d/w%d/wp%d/art%d/c%d/f%d/ex=%d:%d)",
        p.pos, p.name, m.instance_id, m.level, m.wakeup, m.weapon_lv, m.artifact_lv or 0, m.contract_lv or 0, m.force_lv or 0, m.ex_prop_type or 0, m.ex_prop_val or 0))
    return true
end

-- Phase 3: Strict global in_flight=1 Train & Wash (0% packet drops, ~10-14 pkt/s sustained!)
local function step_train()
    if STATE._inflight ~= nil then return end
    if not STATE._cfg.full_auto then
        set_phase(STATE._cfg.setup_runes and "runes" or "verify")
        return step_machine()
    end
    -- Pre-flight resource scan once
    if not STATE._prechecked_res and #STATE._train_queue > 0 then
        for _, p in ipairs(STATE._train_queue) do
            if not STATE._merc_done[p.inst] then
                local m = tl:GetMercenaryInfo(p.inst)
                local miss = m and detect_missing_resource_for_merc(m) or nil
                if miss and try_restock(miss) then
                    return
                end
            end
        end
        STATE._prechecked_res = true
    end
    local done_cnt = 0
    for _, p in ipairs(STATE._train_queue) do
        if STATE._merc_done[p.inst] then
            done_cnt = done_cnt + 1
        else
            local done_now = advance_one_merc(p)
            if STATE.done then return end
            if done_now or STATE._merc_done[p.inst] then
                done_cnt = done_cnt + 1
            else
                STATE._train_idx = done_cnt
                return
            end
        end
    end
    STATE._train_idx = done_cnt
    set_phase(STATE._cfg.setup_runes and "runes" or "verify")
    log("Phase -> " .. STATE.phase)
    step_machine()
end

-- Phase 4: Safe lossless rune switcher (strict global in_flight=1, QueryRuneLoadouts suppressed mid-batch, ZERO UpgradeRune!)
local function step_runes()
    if STATE._inflight ~= nil then return end
    if not STATE._cfg.setup_runes or not rl then
        set_phase("verify")
        return step_machine()
    end

    if rl.SetSelectedAutoCompose then rl:SetSelectedAutoCompose(false) end
    rl.rune_query_pending = false
    rl.rune_query_needed = false
    rl.rune_native_pending = 0

    if not STATE._rune_queried then
        local list = rl:GetRuneList() or {}
        if #list == 0 then
            STATE._rune_queried = true
            set_inflight("rune_query")
            network:Send({ query_rune_info = {} })
            return
        end
        STATE._rune_queried = true
    end

    local tmp_list = rl:GetRuneTemporaryList() or {}
    if #tmp_list > 0 then
        local cap = rl.GetRuneBagCapacity and rl:GetRuneBagCapacity() or 999
        local cur_n = #(rl:GetRuneList() or {})
        if cur_n + #tmp_list > cap then
            if rl.IsCanBuyRuneBugCell and rl:IsCanBuyRuneBugCell(false) then
                set_inflight("rune_bag")
                rl:BuyRuneBugCell(1)
                return
            else
                return fail("Rune bag full (" .. cur_n .. "/" .. cap .. ") - cannot receive drawn runes safely without UpgradeRune!")
            end
        end
        set_inflight("rune_recv")
        network:Send({ rune_one_key_receive = { is_auto_compose = false } })
        return
    end

    local target_runes = STATE._cfg.runes
    local all_runes = rl:GetRuneList() or {}
    local eq = rl:GetRuneEquipmentList() or {}

    local need_counts = {}
    for pos = 1, 5 do
        local tid = target_runes[pos][1]
        need_counts[tid] = (need_counts[tid] or 0) + 1
    end

    local chosen_rids = {}
    local used_rids = {}
    local missing_slots = {}

    for pos = 1, 5 do
        local cur_r = eq[pos]
        if cur_r and (cur_r.level or 0) >= 20 and (need_counts[cur_r.template_id] or 0) > 0 and not used_rids[cur_r.rune_id] then
            chosen_rids[pos] = cur_r
            used_rids[cur_r.rune_id] = true
            need_counts[cur_r.template_id] = need_counts[cur_r.template_id] - 1
        end
    end

    local rem_tids = {}
    for pos = 1, 5 do
        local tid = target_runes[pos][1]
        if (need_counts[tid] or 0) > 0 then
            rem_tids[#rem_tids + 1] = tid
            need_counts[tid] = need_counts[tid] - 1
        end
    end
    local rem_i = 1
    local slot_want = {}
    for pos = 1, 5 do
        if not chosen_rids[pos] then
            slot_want[pos] = rem_tids[rem_i]
            rem_i = rem_i + 1
        end
    end

    for pos = 1, 5 do
        if not chosen_rids[pos] and slot_want[pos] then
            local want_tid = slot_want[pos]
            for _, r in ipairs(all_runes) do
                if r.template_id == want_tid and (r.level or 0) >= 20 and not used_rids[r.rune_id] then
                    chosen_rids[pos] = r
                    used_rids[r.rune_id] = true
                    break
                end
            end
        end
    end
    for pos = 1, 5 do
        if not chosen_rids[pos] and slot_want[pos] then
            local want_tid = slot_want[pos]
            local low_copy = nil
            for _, r in ipairs(all_runes) do
                if r.template_id == want_tid and not used_rids[r.rune_id] then
                    if not low_copy or (r.level or 0) > (low_copy.level or 0) then low_copy = r end
                end
            end
            if low_copy then
                local donor = nil
                for _, r in ipairs(all_runes) do
                    if (r.level or 0) >= 20 and not used_rids[r.rune_id] and r.rune_id ~= low_copy.rune_id then
                        if not donor or ((r.equip_pos or 0) == 0 and (donor.equip_pos or 0) ~= 0) then
                            donor = r
                        end
                    end
                end
                if not donor then
                    return fail("No Lv20 donor rune available to ExchangeRuneProperty onto tid=" .. tostring(want_tid))
                end
                set_inflight("rune_exch")
                STATE.stats.rune_exchanges = STATE.stats.rune_exchanges + 1
                log(string.format("ExchangeRuneProperty: tid=%d (lv%d) <-> donor tid=%d (lv%d)",
                    low_copy.template_id, low_copy.level or 0, donor.template_id, donor.level or 0))
                rl:ExchangeRuneProperty(low_copy.rune_id, donor.rune_id)
                return
            else
                missing_slots[#missing_slots + 1] = { pos = pos, tid = want_tid }
            end
        end
    end

    if #missing_slots > 0 then
        if STATE.stats.rune_draws >= 60 then
            return fail("Reached 60 rune draws without finding tid=" .. tostring(missing_slots[1].tid))
        end
        local cap = rl.GetRuneBagCapacity and rl:GetRuneBagCapacity() or 999
        if #all_runes >= cap then
            if rl.IsCanBuyRuneBugCell and rl:IsCanBuyRuneBugCell(false) then
                set_inflight("rune_bag")
                rl:BuyRuneBugCell(1)
                return
            else
                return fail("Rune bag full (" .. #all_runes .. "/" .. cap .. ") before drawing tid=" .. tostring(missing_slots[1].tid))
            end
        end
        set_inflight("rune_draw")
        STATE.stats.rune_draws = STATE.stats.rune_draws + 1
        local draw_type = (constants["RUNE_DRAW_TYPE"] and constants["RUNE_DRAW_TYPE"]["TEN"]) or 2
        rl:DrawRune(draw_type, true)
        return
    end

    local eq2 = rl:GetRuneEquipmentList() or {}
    for pos = 1, 5 do
        local want_r = chosen_rids[pos]
        local cur_r = eq2[pos]
        if not cur_r or cur_r.rune_id ~= want_r.rune_id then
            set_inflight("rune_equip:" .. tostring(pos))
            rl:EquipRune(pos, want_r)
            return
        end
    end

    log("All 5 target Lv20 runes equipped!")
    set_phase("verify")
    step_machine()
end

local function step_verify()
    local fid = STATE._cfg.fid
    STATE._allow_bp_calc = true
    if _G.__ANTIGRA_ORIG_CALC_BP then
        tl.CalcTroopBP = _G.__ANTIGRA_ORIG_CALC_BP
    end
    if rl and _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS then
        rl.QueryRuneLoadouts = _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS
    end
    tl:CalcTroopBP(fid, false)
    local spd, aut, dge, def = tl:GetTroopProperty(fid)
    local bp = tl:GetTroopBP(fid)
    local f_slots = {}
    for i, m in ipairs(tl:GetFormationMercenaryList(fid) or {}) do
        f_slots[i] = {
            pos = i,
            inst = m.instance_id,
            tid = m.template_id,
            name = m.template_info and m.template_info.name or "",
            lv = m.level, wake = m.wakeup, wp = m.weapon_lv,
            art = m.artifact_lv or 0, clv = m.contract_lv or 0,
            flv = m.force_lv or 0, ex_type = m.ex_prop_type or 0, ex_val = m.ex_prop_val or 0,
        }
    end
    local eq_runes = {}
    if rl and rl.GetRuneEquipmentList then
        for pos = 1, 5 do
            local r = (rl:GetRuneEquipmentList() or {})[pos]
            if r then eq_runes[pos] = { pos = pos, rid = r.rune_id, tid = r.template_id, lv = r.level } end
        end
    end
    -- Restore UI hooks & trigger one clean final UI + loadout sync
    if graphic_ok and graphic and _G.__ANTIGRA_ORIG_GRAPHIC_DISPATCH then
        graphic.DispatchEvent = _G.__ANTIGRA_ORIG_GRAPHIC_DISPATCH
        pcall(function()
            graphic:DispatchEvent("update_battle_point")
            graphic:DispatchEvent("update_troop_info")
            graphic:DispatchEvent("update_resource_list")
            graphic:DispatchEvent("update_rune_equipment")
        end)
    end
    if STATE._cfg.setup_runes and rl and rl.QueryRuneLoadouts then
        pcall(function() rl:QueryRuneLoadouts() end)
    end
    local total_sec = math.max(0.01, os.clock() - (STATE._t0 or os.clock()))
    log(string.format("Timing: recruit=%.2fs form=%.2fs train=%.2fs runes=%.2fs | pkts=%d (%.1f/s) watchdogs=%d",
        STATE.phase_sec.recruit, STATE.phase_sec.formation, STATE.phase_sec.train, STATE.phase_sec.runes,
        STATE.stats.packets, STATE.stats.packets / total_sec, STATE.stats.watchdogs))
    finish({
        fid = fid,
        weapon = tl:GetFormationWeaponId(fid),
        bp = bp,
        base_stats = { speed = spd, defense = def, dodge = dge, authority = aut },
        stats = STATE.stats,
        phase_sec = STATE.phase_sec,
        slots = f_slots,
        eq_runes = eq_runes,
    })
end

step_machine = function()
    if not STATE.running or STATE.done or STATE._inflight ~= nil then return end
    local ok, err = pcall(function()
        if not network or not network:IsConnected() then
            return fail("Client network disconnected (network:IsConnected() == false)")
        end
        if STATE.phase == "recruit" then
            step_recruit()
        elseif STATE.phase == "formation" then
            step_formation()
        elseif STATE.phase == "train" then
            step_train()
        elseif STATE.phase == "runes" then
            step_runes()
        elseif STATE.phase == "verify" then
            step_verify()
        end
    end)
    if not ok then
        fail("Lua exception in " .. tostring(STATE.phase) .. ": " .. tostring(err))
    end
end

-- Direct 0ms network-event dispatcher (strict global in_flight=1!)
local MERC_EVENTS = {
    alloc_mercenary_exp_ret = true,
    upgrade_mercenary_wakeup_ret = true,
    forge_mercenary_weapon_ret = true,
    open_mercenary_artifact_ret = true,
    upgrade_mercenary_artifact_ret = true,
    sign_contract_ret = true,
    upgrade_mercenary_force_ret = true,
    change_mercenary_exproperty_ret = true,
}

local FORM_EVENTS = {
    craft_soul_stone_ret = true,
    library_recruit_ret = true,
    insert_formation_ret = true,
    replace_mercenary_ret = true,
    exchange_formation_pos_ret = true,
    rest_mercenary_ret = true,
    adjust_mercenary_position_ret = true,
    change_formation_ret = true,
    choose_destiny_weapon_ret = true,
    equip_destiny_weapon_ret = true,
}

local STORE_EVENTS = {
    query_store_info_ret = true,
    buy_store_goods_ret = true,
}

local RUNE_EVENTS = {
    query_rune_info_ret = true,
    query_rune_loadouts_ret = true,
    rune_one_key_receive_ret = true,
    draw_rune_ret = true,
    buy_rune_bag_cell_ret = true,
    equip_rune_ret = true,
    rune_exchange_ret = true,
}

local SUPPRESS_UI_EVENTS = {
    show_prompt_panel = true,
    update_mercenary_info = true,
    update_mercenary_list = true,
    update_battle_point = true,
    update_resource_list = true,
    update_troop_info = true,
    update_force_panel = true,
    update_wakeup_panel = true,
    update_weapon_forge = true,
    update_artifact_panel = true,
    sign_mercenary_contract = true,
    update_mercenary_exproperty = true,
    change_mercenary_exproperty = true,
    update_rune_bag = true,
    update_rune_equipment = true,
}

local function on_net_event(ev_name, recv_msg)
    if not STATE.running or STATE.done then return end
    local res_str = type(recv_msg) == "table" and recv_msg.result or ""

    if STORE_EVENTS[ev_name] then
        if STATE._inflight == "store_query" or STATE._inflight == "store_buy" then
            STATE._inflight = nil
            step_machine()
        end
        return
    end

    if MERC_EVENTS[ev_name] and STATE.phase == "train" then
        local mid = STATE._inflight_mid
        if type(recv_msg) == "table" then
            mid = recv_msg.mercenary_id or recv_msg.instance_id or (type(recv_msg.mercenary) == "table" and recv_msg.mercenary.instance_id) or mid
        end
        if ev_name == "forge_mercenary_weapon_ret" then
            tl.forge_weapon_inflight = nil
        end
        STATE._inflight = nil
        STATE._inflight_mid = nil
        STATE._inflight_op = nil

        if res_str == "not_enough_resource" then
            local m = mid and tl:GetMercenaryInfo(mid) or nil
            local miss = (ev_name == "alloc_mercenary_exp_ret" and constants["RESOURCE_TYPE"]["exp"]) or (m and detect_missing_resource_for_merc(m))
            log("not_enough_resource on " .. tostring(ev_name) .. " mid=" .. tostring(mid) .. " miss=" .. tostring(miss))
            if miss then try_restock(miss) end
            return
        elseif ev_name == "alloc_mercenary_exp_ret" and res_str ~= "success" and res_str ~= "" then
            local cur_cap = STATE._exp_step_cap or 29
            if cur_cap > 5 then
                STATE._exp_step_cap = math.floor(cur_cap / 2)
                log("Exp jump rejected (" .. res_str .. "), halving step to " .. tostring(STATE._exp_step_cap))
            else
                return fail("alloc_mercenary_exp_ret rejected: " .. tostring(res_str))
            end
        elseif res_str ~= "success" and res_str ~= "" and res_str ~= "forge_failure" then
            log("Non-success " .. tostring(ev_name) .. ": " .. tostring(res_str))
        end

        if ev_name == "change_mercenary_exproperty_ret" and res_str == "success" and mid and STATE._merc_map[mid] then
            local p = STATE._merc_map[mid]
            local wcode = WASH_CODE[p.wash]
            local min_v = wash_min_val(wcode)
            if recv_msg.ex_prop_type == wcode and (recv_msg.ex_prop_val or 0) >= min_v then
                local m = tl:GetMercenaryInfo(mid)
                if m and is_fully_trained(m) then
                    STATE._merc_done[mid] = true
                    log(string.format("Trained & washed #%02d %s (inst=%d -> lv%d/w%d/wp%d/art%d/c%d/f%d/ex=%d:%d)",
                        p.pos, p.name, m.instance_id, m.level, m.wakeup, m.weapon_lv, m.artifact_lv or 0, m.contract_lv or 0, m.force_lv or 0, m.ex_prop_type or 0, m.ex_prop_val or 0))
                end
            elseif recv_msg.ex_prop_type_temp == wcode and (recv_msg.ex_prop_val_temp or 0) >= min_v then
                set_inflight("merc:" .. tostring(mid) .. ":wash_rep", mid, "wash_rep")
                tl:ReplaceExProperty(mid, true)
                return
            end
        end
        step_machine()
        return
    end

    if RUNE_EVENTS[ev_name] and STATE.phase == "runes" then
        if ev_name == "query_rune_loadouts_ret" then
            if STATE._inflight == nil then step_machine() end
            return
        end
        if ev_name ~= "query_rune_info_ret" and res_str ~= "success" and res_str ~= "" then
            return fail("Runes " .. ev_name .. " failed: " .. tostring(res_str))
        end
        STATE._inflight = nil
        step_machine()
        return
    end

    if FORM_EVENTS[ev_name] and (STATE.phase == "recruit" or STATE.phase == "formation") then
        if ev_name == "library_recruit_ret" and STATE._recruit_target then
            if res_str == "success" then
                local rt = STATE._recruit_target
                for id, m in pairs(tl:GetMercenaryList() or {}) do
                    if m.template_id == rt.p.tid and not rt.pre[id] then
                        rt.p.inst = id
                        log(string.format("Recruited #%02d %s -> inst=#%d", rt.p.pos, rt.p.name, id))
                        break
                    end
                end
                STATE._recruit_target = nil
            else
                return fail("library_recruit_ret failed: " .. tostring(res_str))
            end
        end
        STATE._inflight = nil
        step_machine()
    end
end

local function install_hooks()
    if STATE._hooks_installed then return end
    STATE._hooks_installed = true

    -- 0. Lightweight Send counter (detects whether ReplaceMercenaryFromFormation actually sent a packet)
    if not _G.__ANTIGRA_ORIG_NET_SEND then
        _G.__ANTIGRA_ORIG_NET_SEND = network.Send
    end
    local orig_send = _G.__ANTIGRA_ORIG_NET_SEND
    network.Send = function(self, msg, ...)
        if STATE.running and not STATE.done then
            STATE._send_seq = (STATE._send_seq or 0) + 1
        end
        return orig_send(self, msg, ...)
    end

    -- 1. Direct 0ms hook on network.event_listener.Dispatch
    local el = network.event_listener
    if not _G.__ANTIGRA_ORIG_DISPATCH then
        _G.__ANTIGRA_ORIG_DISPATCH = el.Dispatch
    end
    local orig_dispatch = _G.__ANTIGRA_ORIG_DISPATCH
    el.Dispatch = function(self, ev_name, recv_msg, ...)
        local r1, r2, r3 = orig_dispatch(self, ev_name, recv_msg, ...)
        if STATE.running and not STATE.done and (MERC_EVENTS[ev_name] or FORM_EVENTS[ev_name] or STORE_EVENTS[ev_name] or RUNE_EVENTS[ev_name]) then
            pcall(on_net_event, ev_name, recv_msg)
        end
        return r1, r2, r3
    end

    -- 1b. Suppress intermediate QueryRuneLoadouts collisions during batch rune ops!
    if rl and rl.QueryRuneLoadouts then
        if not _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS then
            _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS = rl.QueryRuneLoadouts
        end
        local orig_ql = _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS
        rl.QueryRuneLoadouts = function(self, ...)
            if STATE.running and not STATE.done then
                self.rune_query_pending = false
                self.rune_query_needed = false
                self.rune_native_pending = 0
                return false
            end
            return orig_ql(self, ...)
        end
    end

    -- 1c. Suppress noisy UI redraws & repeated full-roster CalcTroopBP during batch deploy!
    if graphic_ok and graphic and graphic.DispatchEvent then
        if not _G.__ANTIGRA_ORIG_GRAPHIC_DISPATCH then
            _G.__ANTIGRA_ORIG_GRAPHIC_DISPATCH = graphic.DispatchEvent
        end
        local orig_gd = _G.__ANTIGRA_ORIG_GRAPHIC_DISPATCH
        graphic.DispatchEvent = function(self, ev, ...)
            if STATE.running and not STATE.done and SUPPRESS_UI_EVENTS[ev] then
                return
            end
            return orig_gd(self, ev, ...)
        end
    end
    if tl and tl.CalcTroopBP then
        if not _G.__ANTIGRA_ORIG_CALC_BP then
            _G.__ANTIGRA_ORIG_CALC_BP = tl.CalcTroopBP
        end
        local orig_bp = _G.__ANTIGRA_ORIG_CALC_BP
        tl.CalcTroopBP = function(self, ...)
            if STATE.running and not STATE.done and not STATE._allow_bp_calc then
                return
            end
            return orig_bp(self, ...)
        end
    end

    -- 2. Tight 1.2s global watchdog in tl.Update (zero concurrent collisions -> ~100ms true RTT!)
    STATE._orig_update = tl.Update
    tl.Update = function(self, dt)
        if STATE._orig_update then STATE._orig_update(self, dt) end
        if not STATE.running or STATE.done then return end
        if STATE._inflight ~= nil then
            local limit = (STATE._inflight == "store_query" or STATE._inflight == "store_buy") and 3.0 or 1.2
            if (os.clock() - STATE._inflight_ts) > limit then
                STATE.stats.watchdogs = STATE.stats.watchdogs + 1
                log("Watchdog #" .. tostring(STATE.stats.watchdogs) .. " clearing stale inflight: " .. tostring(STATE._inflight))
                if tl.forge_weapon_inflight then tl.forge_weapon_inflight = nil end
                STATE._inflight = nil
                STATE._inflight_mid = nil
                STATE._inflight_op = nil
                step_machine()
            end
        end
    end
end

local function cleanup()
    STATE.running = false
    STATE._allow_bp_calc = true
    if STATE._hooks_installed then
        if STATE._orig_update then tl.Update = STATE._orig_update end
        if _G.__ANTIGRA_ORIG_NET_SEND then network.Send = _G.__ANTIGRA_ORIG_NET_SEND end
        if _G.__ANTIGRA_ORIG_DISPATCH then network.event_listener.Dispatch = _G.__ANTIGRA_ORIG_DISPATCH end
        if _G.__ANTIGRA_ORIG_CALC_BP then tl.CalcTroopBP = _G.__ANTIGRA_ORIG_CALC_BP end
        if rl and _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS then rl.QueryRuneLoadouts = _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS end
        if graphic_ok and graphic and _G.__ANTIGRA_ORIG_GRAPHIC_DISPATCH then
            graphic.DispatchEvent = _G.__ANTIGRA_ORIG_GRAPHIC_DISPATCH
        end
        STATE._hooks_installed = false
    end
end

local function dry_run(cfg)
    STATE._cfg = cfg
    STATE.stats = { reused_tier1=0, reused_tier2=0, recruited_tier3=0, restocks=0, forges=0, washes=0, rune_draws=0, rune_exchanges=0, packets=0, watchdogs=0 }
    local plan, err = build_reuse_plan(cfg)
    if err then return { ok = false, err = err } end
    local details = {}
    for i, p in ipairs(plan) do
        local m = p.inst and tl:GetMercenaryInfo(p.inst) or nil
        details[i] = {
            pos = p.pos, tid = p.tid, name = p.name, wash = p.wash, tier = p.tier, inst = p.inst,
            lv = m and m.level or 0, wake = m and m.wakeup or 0, wp = m and m.weapon_lv or 0,
            art = m and m.artifact_lv or 0, clv = m and m.contract_lv or 0, flv = m and m.force_lv or 0,
            ex_type = m and m.ex_prop_type or 0, ex_val = m and m.ex_prop_val or 0,
        }
    end
    return {
        ok = true,
        stats = STATE.stats,
        plan = details,
        rune_list_len = rl and #(rl:GetRuneList() or {}) or 0,
    }
end

local function start(cfg)
    cleanup()
    STATE.running = true
    STATE.done = false
    STATE.ok = false
    STATE.err = nil
    STATE.logs = {}
    STATE.result = nil
    STATE.stats = { reused_tier1=0, reused_tier2=0, recruited_tier3=0, restocks=0, forges=0, washes=0, rune_draws=0, rune_exchanges=0, packets=0, watchdogs=0 }
    STATE.phase_sec = { recruit = 0, formation = 0, train = 0, runes = 0 }
    STATE._cfg = cfg
    STATE._t0 = os.clock()
    STATE._phase_t0 = STATE._t0
    STATE._inflight = nil
    STATE._inflight_mid = nil
    STATE._inflight_op = nil
    STATE._merc_done = {}
    STATE._merc_map = {}
    STATE._train_queue = {}
    STATE._train_idx = 0
    STATE._send_seq = 0
    STATE._allow_bp_calc = false
    STATE._store_queried = false
    STATE._rune_queried = false
    STATE._exp_step_cap = 29
    STATE._replace_unsupported = false
    STATE._prechecked_res = false

    local plan, err = build_reuse_plan(cfg)
    if err then fail(err); return { ok = false, err = err } end
    STATE._plan = plan

    log(string.format("ZeroStall v3.0 ready: Tier1=%d, Tier2(idle)=%d, Tier3(recruit)=%d, global_inflight=1, runes=%s",
        STATE.stats.reused_tier1, STATE.stats.reused_tier2, STATE.stats.recruited_tier3, cfg.setup_runes and "on" or "off"))

    install_hooks()
    STATE.phase = "recruit"
    STATE._phase_t0 = os.clock()
    step_machine()
    return { ok = true, started = true }
end

local function status()
    return {
        running = STATE.running,
        done = STATE.done,
        ok = STATE.ok,
        err = STATE.err,
        phase = STATE.phase,
        train_idx = STATE._train_idx,
        train_total = #STATE._train_queue,
        stats = STATE.stats,
        phase_sec = STATE.phase_sec,
        logs = STATE.logs,
        result = STATE.result,
    }
end

_G.__ANTIGRA_AUTO_DEPLOY = {
    VERSION = DEPLOYER_VERSION,
    start = start,
    dry_run = dry_run,
    status = status,
    cleanup = cleanup,
}
return { loaded = true, version = DEPLOYER_VERSION }
