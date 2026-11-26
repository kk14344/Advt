-- The guide's "first stage" is game map 2; map 1 grants the initial pool.
local GUIDE = -- @GUIDE@
local WEEKDAYS = {"周一", "周二", "周三", "周四", "周五", "周六", "周日"}
local network = require("util.network")
local utils = require("util.utils")

local function task_now(self)
    return tonumber(self.now) or 0
end

local function reply(self, message, fields)
    fields = fields or {}
    local pending=self._task_pending
    if fields.wait and pending and pending.description then
        fields.operation=pending.description
        fields.waitState=pending.queued and "等待发送" or (self._request_probe and "核对游戏状态" or "等待游戏确认")
    end
    fields.message = message
    self.task_message = message
    return fields
end

-- Wrap the game's one-to-one handlers, preserving their original callbacks.
-- RegisterEvent alone would replace the game's handlers and break its state.
function A:install_fast_graphics()
    local ok,g=pcall(require,"logic.graphic")
    if not ok or type(g)~="table" or type(g.DispatchEvent)~="function" then return false end
    if g.DispatchEvent==g._mwa_fast_dispatch then return true end
    local previous=g.DispatchEvent
    local wrapped=function(bus,name,...)
        local active=__MWA
        if active and active.orepacks_state and active.orepacks_state.running
            and name=="show_world_sub_panel" and (select(1,...)=="reward_panel" or select(1,...)=="single_reward_panel") then
            -- Resource accounting has already run in the native handler.
            -- Skip only its presentation while opening packs, not its rewards.
            local ok,reward=pcall(require,"logic.reward")
            if ok and type(reward.ResetRewardInfoList)=="function" then pcall(function()reward:ResetRewardInfoList()end) end
            return true
        end
        if active and active.cfg.autoSkipBattle~=false and name=="show_battle_room" then
            local callback=select(6,...)
            active._cart_visual_battle=nil
            active.inBattle=false
            active.fast_battles=(active.fast_battles or 0)+1
            if type(callback)=="function" then
                active._fast_settlements=active._fast_settlements or {}
                active._fast_settlements[#active._fast_settlements+1]=callback
            end
            return true
        end
        if active and active.daily_state and active.cfg.autoSkipBattle~=false
            and name=="show_world_sub_panel" and select(1,...)=="escort_start_rob_panel" then
            local types=(package.loaded["util.client_constants"] or {}).BATTLE_TYPE or {}
            local target,properties,record,winner=select(3,...),select(4,...),select(5,...),select(6,...)
            -- The panel normally waits for its cart animation before dispatching
            -- this exact battle event. Use the same settlement callback now.
            return bus:DispatchEvent("show_battle_room",types.vs_escort_target,target,properties,record,winner,function()
                if winner then bus:DispatchEvent("show_world_sub_panel","reward_panel") end
            end)
        end
        if active and active.daily_state and active.cfg.autoSkipBattle==false then
            if name=="show_world_sub_panel" and select(1,...)=="escort_start_rob_panel" then
                active._cart_visual_battle="intro"
            elseif (name=="hide_world_sub_panel" and select(1,...)=="escort_start_rob_panel" or name=="hide_all_sub_panel")
                and active._cart_visual_battle=="intro" then active._cart_visual_battle=nil
            elseif name=="show_battle_room" then
                local types=(package.loaded["util.client_constants"] or {}).BATTLE_TYPE or {}
                if types.vs_escort_target and select(1,...)==types.vs_escort_target then
                    active._cart_visual_battle="battle"
                    local callback=select(6,...)
                    return previous(bus,name,select(1,...),select(2,...),select(3,...),select(4,...),select(5,...),function(...)
                        active._cart_visual_battle=nil
                        if type(callback)=="function" then return callback(...) end
                    end)
                end
            end
        end
        if active and active.destiny_state and name=="weapon_upgrade_star_success" then
            local p=active._task_pending
            if p and p.kind=="growth_star_upgrade" then
                local loaded,weapon=pcall(require,"logic.destiny_weapon")
                if loaded then weapon.show_animation=false end
                return true
            end
        end
        return previous(bus,name,...)
    end
    g.DispatchEvent=wrapped;g._mwa_fast_dispatch=wrapped
    return true
end

function A:drain_fast_settlements()
    if self._draining_settlements then return end
    self._draining_settlements=true
    local callbacks=self._fast_settlements or {};self._fast_settlements={}
    for _,callback in ipairs(callbacks) do
        local ok,err=pcall(callback)
        if not ok then self:log("战斗结算回调失败：" .. tostring(err)) end
    end
    self._draining_settlements=false
end

function A:install_task_hooks()
    self:install_fast_graphics()
    local listener = network.event_listener
    if type(listener) ~= "table" then return false end
    self._task_hooks = self._task_hooks or {}
    local names = {
        "query_arena_rival_ret", "challenge_arena_rival_ret", "refresh_arena_rival_ret",
        "query_vanity_troop_ret", "query_vanity_maze_states_ret",
        "get_vanity_mercenary_ret", "vanity_sync_troop_ret", "vanity_challenge_ret", "vanity_buy_recruit_ret",
        "query_mine_info_ret", "mine_start_ret", "mine_receive_reward_ret",
        "query_cultivation_info_ret",
        "dig_block_ret","finish_random_event_ret","solve_adventure_event_ret","accept_random_event_ret",
        "use_tnt_ret","collect_mine_ret",
        "query_golem_info_ret","query_mining_block_info_ret",
        "challenge_friend_ret","challenge_server_pvp_ret","challenge_battlefront_boss_ret","challenge_glory_ret",
    }
    for _,name in ipairs(self.REQUEST_EVENTS or {})do names[#names+1]=name end
    for _,name in ipairs(self.ORE_PACK_EVENTS or {})do names[#names+1]=name end
    for _,name in ipairs(self.PUSH_EVENTS or {})do names[#names+1]=name end
    for _,name in ipairs(self.REFILL_EVENTS or {}) do names[#names+1]=name end
    for _,name in ipairs(self.DAILY_EVENTS or {}) do names[#names+1]=name end
    for _,name in ipairs(self.RESEARCH_MINE_EVENTS or {}) do names[#names+1]=name end
    for _,name in ipairs(self.WORKSHOP_EVENTS or {}) do names[#names+1]=name end
    for _,name in ipairs(self.GROWTH_EVENTS or {}) do names[#names+1]=name end
    for _, name in ipairs(names) do
        local original = listener[name]
        if type(original) == "function" and original ~= self._task_hooks[name] then
            local wrapped = function(message, ...)
                network._mwa_incoming=network._mwa_incoming or {}
                if network._mwa_incoming[name]==message then return original(message,...) end
                local previous_message=network._mwa_incoming[name];network._mwa_incoming[name]=message
                local ok,err=pcall(original,message,...)
                network._mwa_incoming[name]=previous_message
                local active = __MWA
                if not ok and active and task_now(active)>=(active._callback_error_log or 0) then
                    active._callback_error_log=task_now(active)+5
                    active:log("游戏回执处理异常，已保留回执并重新同步："..tostring(name))
                end
                if active then active:drain_fast_settlements() end
                if active and active.audit_record then
                    pcall(function()
                        local result = tostring((message or {}).result or "")
                        active:audit_record({ dir = "ret", name = name, result = result })
                        active:spend_on_ret(name, result, message)
                    end)
                end
                if active and active._cancelled_task_pending and active._cancelled_task_pending.event == name then
                    local cancelled=active._cancelled_task_pending
                    if cancelled.accept_response and not cancelled.accept_response(message or {}) then return end
                    active._cancelled_task_pending = nil
                    if cancelled.kind=="mine_target_blast" and active.mining_on_response then active:mining_on_response(name,message or {}) end
                    return
                end
                if active and active._task_pending and active._task_pending.event == name then
                    local p=active._task_pending
                    if not p.accept_response or p.accept_response(message or {}) then
                        p.response = message or {}
                        p.received_at = task_now(active)
                    end
                end
                if active and active.mining_on_response then active:mining_on_response(name,message or {}) end
                if active and active.refill_on_response then active:refill_on_response(name,message or {}) end
                if active and active.orepacks_on_response then active:orepacks_on_response(name,message or {}) end
            end
            listener[name] = wrapped
            self._task_hooks[name] = wrapped
        end
    end
    return true
end

function A:task_request(event, kind, action)
    if not self:install_task_hooks() or not self._task_hooks[event] then
        error("游戏功能尚未初始化，请进入游戏后重试（缺少事件：" .. tostring(event) .. "）")
    end
    local pending = {event=event, kind=kind, sent_at=task_now(self)}
    self._task_pending = pending
    local ok, err = pcall(action)
    if not ok then self._task_pending = nil; error(err) end
    if err == false then self._task_pending=nil;error("游戏未接受本次操作") end
    return pending
end

function A:cancel_task()
    local pending=(self._mine_blast and self._mine_blast.uncertain) or (self.mine_refill and self.mine_refill.uncertain) or self._task_pending
    if pending and not pending.response and not pending.event:match('^query_') then
        self._cancelled_task_pending=pending
    end
    self._task_pending = nil
    self.fast_refill=nil
    self.mine_refill=nil;self._mine_event=nil;self._mine_sync=nil
    self.arena_state = nil
    self.void_state = nil
    self.mine_state = nil
    self.workshop_state = nil
    self.guild_state = nil
    self.rune_state = nil
    self.destiny_state = nil
    self.earth_state = nil
    self.merc_state = nil
    if self.orepacks_state then self:close_ore_pack_rewards() end
    self.orepacks_state=nil
    self.push_state=nil
    self._cart_visual_battle=nil
    self.mining_boss_state=nil
    self.void_exchange_state = nil
    if self.daily_state then self.daily_state.running=false end
    self.daily_state = nil
    self.task_message = ""
    return true
end

function A:task_barrier()
    local pending=self._cancelled_task_pending
    if not pending then return nil end
    if task_now(self)-pending.sent_at<=20 then
        return reply(self,"等待上次操作结算，暂缓新任务",{wait=true})
    end
    self._cancelled_task_pending=nil
end

-- 神力矿工包每个恢复5次；游戏支持每批最多使用5个。
-- 只补当前计划缺口，避免库存足够时仍固定消耗5个。
function A:mining_bag_count(needed,have,available)
    local cap=tonumber(mining.dig_max_count) or 0
    local target=tonumber(needed) or 0
    if cap>0 then target=math.min(target,cap) end
    return math.max(0,math.min(5,math.floor(tonumber(available) or 0),
        math.ceil(math.max(0,target-(tonumber(have) or 0))/5)))
end

-- ------------------------------------------------------------
-- 结构化审计（PRD P0-04）：记录每一条发往服务器的消息与返回结果，
-- 供 C# 落盘到 audit/*.log，出问题时可回放"到底做了什么"。
-- ------------------------------------------------------------
local function audit_sprint(value, depth)
    depth = depth or 0
    local kind = type(value)
    if kind == "string" then
        local text = value
        if #text > 60 then text = text:sub(1, 57) .. "..." end
        return string.format("%q", text)
    elseif kind == "number" or kind == "boolean" or kind == "nil" then
        return tostring(value)
    elseif kind == "table" and depth < 2 then
        local parts, count = {}, 0
        for key, item in pairs(value) do
            count = count + 1
            if count > 6 then parts[#parts + 1] = "..."; break end
            parts[#parts + 1] = tostring(key) .. "=" .. audit_sprint(item, depth + 1)
        end
        return "{" .. table.concat(parts, ",") .. "}"
    end
    return "[" .. kind .. "]"
end

function A:audit_record(entry)
    self.audit_seq = (self.audit_seq or 0) + 1
    entry.seq = self.audit_seq
    entry.t = math.floor((tonumber(self.now) or 0) * 1000) / 1000
    local buffer = self.audit_buf or {}
    buffer[#buffer + 1] = entry
    if #buffer > 400 then table.remove(buffer, 1) end
    self.audit_buf = buffer
end

function A:audit_tail(since_seq)
    since_seq = tonumber(since_seq) or 0
    local out = {}
    local buffer = self.audit_buf or {}
    for index = 1, #buffer do
        local entry = buffer[index]
        if entry.seq > since_seq then out[#out + 1] = entry end
    end
    return out
end

-- 幂等安装：始终把自己包在最外层，这样日常消费护栏拦截的请求也会被记录。
function A:install_audit_hook()
    if network.Send == network._mwa_audit then return end
    local previous = network.Send
    local wrapped = function(net, message, ...)
        local active = __MWA
        if active and active.audit_record then
            pcall(function()
                local name, payload = next(message or {})
                active:audit_record({ dir = "send", name = tostring(name or "?"), data = audit_sprint(payload) })
            end)
        end
        return previous(net, message, ...)
    end
    network.Send = wrapped
    network._mwa_audit = wrapped
end

-- ------------------------------------------------------------
-- 消费策略中心（PRD P0-03）：血钻每日预算 + 分项开关 + 资源保留线。
-- 已消耗基数由 C# 持久化并回推（跨页面重载连续），会话内累加。
-- ------------------------------------------------------------
function A:spend_day_now()
    local seconds = tonumber(time_logic:Now()) or 0
    local offset = tonumber(time_logic.time_zone_offset) or 28800
    return math.floor((seconds + offset) / 86400)
end

function A:spend_base()
    local day = tonumber(self.cfg.spendDay) or 0
    if day ~= self:spend_day_now() then return 0 end
    return tonumber(self.cfg.spendBase) or 0
end

function A:spend_total()
    return self:spend_base() + (self.spend_session or 0)
end

function A:spend_record(cost, context)
    cost = tonumber(cost) or 0
    if cost <= 0 then return end
    self.spend_session = (self.spend_session or 0) + cost
    self:log(string.format("资源消耗：%s -%d 血钻（今日累计 %d，预算不限）",
        tostring(context or "未分类"), cost, self:spend_total()))
end

-- C# 在把持久化基数回推时调用：把已确认的增量从会话计数中扣除。
-- 用扣减而不是清零，避免「读快照→回推」间隙里的新消费被丢掉。
function A:spend_reset_session(new_base)
    new_base = tonumber(new_base) or 0
    local old_base = tonumber(self.cfg.spendBase) or 0
    local consumed = new_base - old_base
    if consumed > 0 then
        self.spend_session = math.max(0, (self.spend_session or 0) - consumed)
    end
    self.cfg.spendBase = new_base
    self.cfg.spendDay = self:spend_day_now()
end

-- 跨游戏日：基数与会话增量整体清零（昨天的消耗不计入今天）。
function A:spend_reset_day()
    self.cfg.spendBase = 0
    self.cfg.spendDay = self:spend_day_now()
    self.spend_session = 0
end

function A:spend_can_afford(cost)
    -- 1.20：旧预算设置不再限制购买；每个购买步骤仍核对实际余额。
    return true
end

-- 按返回事件记账：只认服务器 success，数值从游戏常量推导。
function A:spend_on_ret(name, result, message)
    if result ~= "success" then return end
    if name == "refresh_arena_rival_ret" then
        local cost = tonumber(constants.ARENA_REFRESH_COST_BLOOD_DIAMOND) or 0
        self:spend_record(cost, "竞技场血钻刷新")
    elseif name == "buy_cave_challenge_ret" then
        local cave_type = tonumber((message or {}).cave_type) or 0
        local max_buy = tonumber((constants.CAVE_DAILY_BUY_CHALLENGE_NUM or {})[cave_type]) or 0
        -- 游戏原始处理器已先把剩余次数减 1，再取价格阶梯。
        local left = tonumber((mining.cave_buy_challenge_nums or {})[cave_type]) or 0
        local prices = (constants.CAVE_DAILY_BUY_PRICE or {})[cave_type] or {}
        local pending=self._task_pending
        local price = pending and pending.event==name and pending.cave_price
            or tonumber(prices[max_buy - left]) or tonumber(prices[#prices]) or 0
        self:spend_record(price, "巨魔巢穴额外次数（副本" .. cave_type .. "）")
    end
end

-- ------------------------------------------------------------
-- 兼容性自检（PRD P1-02）：探测助手依赖的接口与配置表是否存在。
-- 每项检查返回 nil 视为通过，返回字符串为失败原因。
-- ------------------------------------------------------------
function A:compat_report()
    local results = {}
    local function check(name, fn)
        local ok, reason = pcall(fn)
        local pass = ok and reason == nil
        results[#results + 1] = {
            name = name,
            ok = pass,
            reason = pass and "" or tostring(reason or "执行出错"),
        }
    end
    check("network.Send", function()
        if type(network) ~= "table" or type(network.Send) ~= "function" then
            return "网络模块不可用"
        end
    end)
    check("time.GetDateInfo", function()
        if type(time_logic) ~= "table" or type(time_logic.GetDateInfo) ~= "function" then
            return "缺少 GetDateInfo（副本日期键依赖）"
        end
    end)
    check("constants.CAVE_DAILY_BUY_PRICE", function()
        local table_ = constants and constants.CAVE_DAILY_BUY_PRICE
        if type(table_) ~= "table" or type(table_[3]) ~= "table" then
            return "巨魔巢穴购买价格表缺失"
        end
    end)
    check("constants.CAVE_DAILY_BUY_CHALLENGE_NUM", function()
        local table_ = constants and constants.CAVE_DAILY_BUY_CHALLENGE_NUM
        if type(table_) ~= "table" or not tonumber(table_[3]) then
            return "巨魔巢穴每日购买上限缺失"
        end
    end)
    check("constants.ARENA_REFRESH_COST_BLOOD_DIAMOND", function()
        if not tonumber(constants and constants.ARENA_REFRESH_COST_BLOOD_DIAMOND) then
            return "竞技场刷新费用缺失"
        end
    end)
    check("config_manager.mining_event_config", function()
        if type(config_manager) ~= "table" or type(config_manager.mining_event_config) ~= "table" then
            return "副本事件配置缺失"
        end
    end)
    check("config_manager.mining_quarry_config", function()
        if type(config_manager) ~= "table" or type(config_manager.mining_quarry_config) ~= "table" then
            return "工坊工程配置缺失"
        end
    end)
    check("mining.SolveCaveEvent", function()
        if type(mining) ~= "table" or type(mining.SolveCaveEvent) ~= "function" then
            return "副本挑战接口缺失"
        end
    end)
    check("mining.BuyCaveChallengeCounts", function()
        if type(mining) ~= "table" or type(mining.BuyCaveChallengeCounts) ~= "function" then
            return "副本购买接口缺失"
        end
    end)
    check("troop.VanityExchangeReward", function()
        local ok_module, troop = pcall(require, "logic.troop")
        if not ok_module or type(troop) ~= "table" or type(troop.VanityExchangeReward) ~= "function" then
            return "虚空兑换接口缺失"
        end
    end)
    check("arena.ChallengeRival", function()
        local ok_module, arena = pcall(require, "logic.arena")
        if not ok_module or type(arena) ~= "table" or type(arena.ChallengeRival) ~= "function" then
            return "竞技场接口缺失"
        end
    end)
    local failures = {}
    for index = 1, #results do
        if not results[index].ok then failures[#failures + 1] = results[index] end
    end
    return {
        status = #failures == 0 and "ok" or "warning",
        total = #results,
        failures = failures,
    }
end

function A:start_arena(rounds)
    self:cancel_task()
    -- 独立竞技场按「轮次」执行：每一轮固定依次挑战当前对手列表中的九名对手。
    self.arena_state = {
        round_mode=true,
        rounds=math.max(1, math.min(99, math.floor(tonumber(rounds) or 1))),
        round=1, pos=0, count=0, queried=false, attempted={}, refreshes=0, cost=0,
    }
    return true
end

function A:arena_step()
    local state = self.arena_state
    if not state then return reply(self, "竞技场任务已停止", {done=true}) end
    local waiting=self:task_barrier();if waiting then return waiting end
    if scene_manager:GetCurrentSceneName() ~= "world" then
        return reply(self, "等待进入游戏", {wait=true})
    end
    if self.inBattle and self.cfg.autoSkipBattle==false then return reply(self, "等待竞技场战斗结束", {wait=true}) end
    self:hide_panel("reward_panel")
    local arena = require("logic.arena")
    local pending = self._task_pending
    if pending then
        if not pending.response then
            if task_now(self) - pending.sent_at > 20 then
                return reply(self, "竞技场请求超时，已停止；请确认游戏状态后重试", {done=true, error=true})
            end
            return reply(self, "等待竞技场服务器响应", {wait=true})
        end
        local result = pending.response.result
        self._task_pending = nil
        if pending.kind == "arena_query" then
            if type(pending.response.rival) ~= "table" then
                return reply(self, "竞技场未返回对手信息", {done=true, error=true})
            end
            state.queried = true
            state.attempted = {}
            state.pos = 0
        elseif pending.kind == "arena_refresh" then
            if result ~= "success" then
                return reply(self, "竞技场刷新失败：" .. tostring(result), {done=true, error=true})
            end
            state.refreshes = state.refreshes + 1
            state.cost = state.cost + (pending.cost or 0)
            state.queried = false
            -- RefreshRival only acknowledges the payment; Query() may reuse cache.
            self:task_request("query_arena_rival_ret", "arena_query", function()
                network:Send({query_arena_rival={}})
            end)
            return reply(self, "刷新成功，重新读取竞技场对手", {action="刷新完成"})
        elseif pending.kind == "arena_challenge" then
            if result == "not_enough_count" then
                -- 挑战券刚好用完：不是错误，交给刷新流程继续下一轮。
                arena.challenge_num = 0
                state.attempted[pending.pos] = nil
                state.pos = math.max(0, pending.pos - 1)
                return reply(self, "竞技场挑战券已用完，准备刷新", {action="挑战券用尽"})
            end
            if result ~= "success" and result ~= "failure" and result ~= "battle_failure" then
                return reply(self, "竞技场挑战未被接受：" .. tostring(result), {done=true, error=true})
            end
            state.count = state.count + 1
            state.attempted[pending.pos] = true
            self.stats[result == "success" and "wins" or "losses"] =
                self.stats[result == "success" and "wins" or "losses"] + 1
            if not state.round_mode then
                return reply(self, "竞技场已结算 " .. state.count .. "/" .. state.target ..
                    "，血钻刷新 " .. state.refreshes .. " 次（" .. state.cost .. " 血钻）", {action="战斗结算"})
            end
            -- 轮次模式：结算后继续在同一轮询里发起下一位对手的挑战。
            self:log("第 " .. state.round .. "/" .. state.rounds .. " 轮 · 第 " .. pending.pos ..
                " 位已结算，累计 " .. state.count .. " 场")
        end
    end
    if state.round_mode then
        if state.round > state.rounds then
            return reply(self, "竞技场完成 " .. state.rounds .. " 轮共 " .. state.count ..
                " 场，血钻刷新 " .. state.refreshes .. " 次（" .. state.cost .. " 血钻）", {done=true})
        end
        if not state.queried then
            self:task_request("query_arena_rival_ret", "arena_query", function()
                network:Send({query_arena_rival={}})
            end)
            return reply(self, "第 " .. state.round .. "/" .. state.rounds .. " 轮：正在读取竞技场对手", {wait=true})
        end
        -- A round expires independently of whether every old rival was beaten.
        if time_logic:Now() > (tonumber(arena.refresh_time) or 0) then
            state.queried = false
            return self:arena_step()
        end
        -- Fixed order: challenge positions 1..9 one by one, skipping beaten rivals.
        -- 挑战券用尽时不结束任务，而是像打完九人一样进入刷新流程。
        local tickets = tonumber(arena.challenge_num) or 0
        local rivals = arena.rival_list or {}
        if tickets > 0 then
            while state.pos < 9 do
                state.pos = state.pos + 1
                local rival = rivals[state.pos]
                local beaten = rival and (rival.state == true or rival.state == 1)
                if rival and not beaten and not state.attempted[state.pos] then
                    local pos = state.pos
                    local p = self:task_request("challenge_arena_rival_ret", "arena_challenge", function()
                        arena:ChallengeRival(pos)
                    end)
                    p.pos = pos
                    return reply(self, "第 " .. state.round .. "/" .. state.rounds .. " 轮：挑战第 " .. pos ..
                        " 位对手（累计 " .. state.count .. " 场）", {action="竞技场挑战"})
                end
                state.attempted[state.pos] = true
            end
        end
        -- 九位都处理完（或挑战券已用完）：本轮结束，进入刷新流程。
        if state.round >= state.rounds then
            return reply(self, "竞技场完成 " .. state.rounds .. " 轮共 " .. state.count ..
                " 场，血钻刷新 " .. state.refreshes .. " 次（" .. state.cost .. " 血钻）", {done=true})
        end
        state.round = state.round + 1
        state.pos = 0
        state.attempted = {}
        if tickets <= 0 then
            self:log("竞技场挑战券已用完，刷新对手继续第 " .. state.round .. " 轮")
        end
        if state.freeOnly or not self.cfg.arenaAllowBloodRefresh then
            local remain = math.max(1, math.ceil((tonumber(arena.refresh_time) or 0) - time_logic:Now() + 1))
            return reply(self, "第 " .. (state.round - 1) .. " 轮已完成，等待免费刷新：" ..
                remain .. " 秒后开始第 " .. state.round .. " 轮", {wait=true, waitFree=true})
        end
        local cost = tonumber(constants.ARENA_REFRESH_COST_BLOOD_DIAMOND)
        if not cost or cost <= 0 then
            return reply(self, "无法读取游戏刷新费用，已停止", {done=true, error=true})
        end
        local budget_ok, budget_reason = self:spend_can_afford(cost)
        if not budget_ok then
            return reply(self, "消费策略拦截：" .. budget_reason .. "；已完成 " ..
                (state.round - 1) .. "/" .. state.rounds .. " 轮", {done=true})
        end
        local resource_type = constants.RESOURCE_TYPE.blood_diamond
        if not resource_logic:CheckResourceNum(resource_type, cost, false) then
            return reply(self, "血钻不足，刷新需 " .. cost .. " 血钻；已完成 " ..
                (state.round - 1) .. "/" .. state.rounds .. " 轮", {done=true})
        end
        local p = self:task_request("refresh_arena_rival_ret", "arena_refresh", function() arena:RefreshRival() end)
        p.cost = cost
        return reply(self, "开始第 " .. state.round .. " 轮，正在刷新竞技场，本次 " .. cost .. " 血钻", {action="血钻刷新"})
    end
    if state.count >= state.target then
        return reply(self, "竞技场完成 " .. state.count .. " 次，刷新消耗 " .. state.cost .. " 血钻", {done=true})
    end
    if not state.queried then
        self:task_request("query_arena_rival_ret", "arena_query", function()
            network:Send({query_arena_rival={}})
        end)
        return reply(self, "正在读取竞技场对手", {wait=true})
    end
    if (tonumber(arena.challenge_num) or 0) <= 0 then
        return reply(self, "竞技场挑战券不足，已完成 " .. state.count .. " 次", {done=true})
    end
    -- A round expires independently of whether every old rival was beaten.
    if time_logic:Now() > (tonumber(arena.refresh_time) or 0) then
        state.queried = false
        return self:arena_step()
    end
    local best_pos, best_bp
    for pos, rival in ipairs(arena.rival_list or {}) do
        local beaten = rival.state == true or rival.state == 1
        if not beaten and not state.attempted[pos] then
            local bp = tonumber(rival.battle_point or rival.bp or rival.combat_power) or math.huge
            if best_bp == nil or bp < best_bp then best_pos=pos; best_bp=bp end
        end
    end
    if best_pos then
        local p = self:task_request("challenge_arena_rival_ret", "arena_challenge", function()
            arena:ChallengeRival(best_pos)
        end)
        p.pos = best_pos
        return reply(self, "挑战第 " .. best_pos .. " 位对手，进度 " .. state.count .. "/" .. state.target,
            {action="竞技场挑战"})
    end
    if state.freeOnly or not self.cfg.arenaAllowBloodRefresh then
        local remain = math.max(1, math.ceil((tonumber(arena.refresh_time) or 0) - time_logic:Now() + 1))
        return reply(self, "等待免费刷新：" .. remain .. " 秒（" .. state.count .. "/" .. state.target .. "）", {wait=true,waitFree=true})
    end
    local cost = tonumber(constants.ARENA_REFRESH_COST_BLOOD_DIAMOND)
    if not cost or cost <= 0 then
        return reply(self, "无法读取游戏刷新费用，已停止", {done=true, error=true})
    end
    local budget_ok, budget_reason = self:spend_can_afford(cost)
    if not budget_ok then
        return reply(self, "消费策略拦截：" .. budget_reason, {done=true})
    end
    local resource_type = constants.RESOURCE_TYPE.blood_diamond
    if not resource_logic:CheckResourceNum(resource_type, cost, false) then
        return reply(self, "血钻不足，本次刷新需要 " .. cost .. " 血钻", {done=true})
    end
    local p = self:task_request("refresh_arena_rival_ret", "arena_refresh", function() arena:RefreshRival() end)
    p.cost = cost
    return reply(self, "正在刷新竞技场，本次 " .. cost .. " 血钻", {action="血钻刷新"})
end

local function skill_tags(info)
    local template = info.template_info or config_manager.mercenary_config[info.template_id] or {}
    local tags = { ["随意"]=true }
    local adds_turns = false
    for index=1,3 do
        local id = tonumber(template["skill" .. index]) or 0
        local sk = config_manager.active_skill_config[id]
        if sk then
            tags["有技能"] = true
            local effect = tonumber(sk.effect_type)
            local param = tonumber(sk.param) or 0
            if effect == 2 then tags["暴击"]=true; if param >= 150 then tags["高暴击"]=true end end
            if effect == 13 then tags["纯粹"]=true end
            if effect == 5 then tags["当前"]=true end
            if effect == 10 then
                tags["自总回"]=true; tags["自回"]=true
                if param >= 25 then tags.strong_heal=true end
            end
            if effect == 8 then tags["自回"]=true end
            if effect == 11 or effect == 12 then
                tags["敌回"]=true
                if param >= 25 then tags["敌回25"]=true end
                if param >= 20 then tags.strong_heal=true end
            end
            if effect and effect >= 9 and effect <= 12 then tags["恢复"]=true end
            if (tonumber(sk.exhaust_turn) or 0) > 0 then tags["虚弱"]=true end
            if (tonumber(sk.seal_turn) or 0) > 0 then tags["封印"]=true end
            for b=1,3 do
                local key = b == 1 and "buffer_id" or "buffer_id" .. b
                local buff = (config_manager.skill_buffer_config or {})[tonumber(sk[key]) or 0]
                if buff and buff.name == "weak" and tonumber(buff.type) == 1 and (tonumber(buff.factor) or 0)>=100 then
                    tags["虚弱"]=true
                end
            end
            if (tonumber(sk.turn_fixed) or 0)>0 then adds_turns=true end
        end
    end
    tags["虚弱或封印"] = tags["虚弱"] or tags["封印"]
    if tonumber(template.genre) == 4 and tags["有技能"] then tags["瞎流"]=true end
    local name = tostring(template.name or "")
    tags[name] = true
    tags["真田"] = name == "真田幸村"
    tags["铃铛"] = name == "铃彦姬"
    -- Unresolved old nicknames are matched literally, never guessed.
    tags["冰山"] = name == "冰山"
    tags["把妹"] = name == "把妹"
    return tags, adds_turns, tonumber(template.quality) or 0, name
end

function A:guide_week()
    local ok, week = pcall(function() return utils:getWDay(time_logic:Now()) end)
    if ok and tonumber(week) and week >= 1 and week <= 7 then return week end
    local seconds=tonumber(time_logic:Now()) or os.time()
    local offset=tonumber(time_logic.time_zone_offset) or 28800
    return (math.floor((seconds+offset)/86400)+3)%7+1
end

local function formation_stats(troop, chosen)
    local saved = troop.vanity_formation
    local temp = {}
    local bp = 0
    for i, merc in ipairs(chosen) do temp[i]=merc.info; bp=bp+merc.bp end
    temp.battle_point = bp
    troop.vanity_formation = temp
    local ok, err = pcall(function() troop:GetVanityTroopProperty() end)
    troop.vanity_formation = saved
    if not ok then return nil, tostring(err) end
    return {bp=tonumber(temp.battle_point) or bp, speed=tonumber(temp.speed) or 0,
        defense=tonumber(temp.defense) or 0,dodge=tonumber(temp.dodge) or 0,authority=tonumber(temp.authority) or 0}
end

-- Rectangular assignment maximizes the team's score without reusing a card.
-- A greedy augmenting path can waste a high-power alternative on the wrong slot.
local function weighted_assignment(slots, eligible, plan, mode)
    local n,m=#slots,#eligible
    if n>m then return nil,"可用佣兵数量不足" end
    local costs={}
    for i,tag in ipairs(slots) do
        costs[i]={}
        local count=0
        for j,merc in ipairs(eligible) do
            if merc.tags[tag] and not (tag == "恢复" and plan.strongHeal and not merc.tags.strong_heal) then
                local initiative=tonumber(merc.info.speed) or 0
                local score=merc.bp
                if mode==2 then
                    score=(plan.speedMax and -initiative or initiative)*1000000000+merc.bp
                elseif mode==3 then
                    score=merc.bp+(plan.speedMax and -initiative or initiative)*100000
                end
                costs[i][j]=-score;count=count+1
            else costs[i][j]=1e30 end
        end
        if count==0 then return nil,"缺少「" .. tag .. "」" end
    end
    local u,v,p,way={},{},{[0]=0},{}
    for i=0,n do u[i]=0 end
    for j=0,m do v[j]=0;p[j]=0 end
    for i=1,n do
        p[0]=i
        local j0=0
        local minv,used={},{}
        for j=1,m do minv[j]=1e30 end
        repeat
            used[j0]=true
            local i0=p[j0]
            local delta,j1=1e30,nil
            for j=1,m do
                if not used[j] then
                    local cur=costs[i0][j]-u[i0]-v[j]
                    if cur<minv[j] then minv[j]=cur;way[j]=j0 end
                    if minv[j]<delta then delta=minv[j];j1=j end
                end
            end
            if not j1 or delta>=1e29 then return nil,"佣兵类型或数量不足" end
            for j=0,m do
                if used[j] then u[p[j]]=u[p[j]]+delta;v[j]=v[j]-delta
                elseif j>0 then minv[j]=minv[j]-delta end
            end
            j0=j1
        until p[j0]==0
        repeat
            local j1=way[j0];p[j0]=p[j1];j0=j1
        until j0==0
    end
    local chosen={}
    for j=1,m do if p[j]>0 then chosen[p[j]]=eligible[j] end end
    return chosen
end

function A:match_guide(troop, week, stage, capacity)
    local alternatives = GUIDE[week] and GUIDE[week][stage]
    if not alternatives then return nil, "攻略没有该星期/关卡方案" end
    local pool = {}
    for _, entry in ipairs(troop.vanity_mercenarys_list or {}) do
        if (tonumber(entry.battle_num) or 0)>0 then
            local info = troop:GetVanityMercenarys(entry.instance_id)
            if info then
                local tags, adds, quality, name = skill_tags(info)
                pool[#pool+1] = {id=entry.instance_id, info=info, tags=tags,
                    adds=adds, quality=quality, name=name, bp=tonumber(info.battle_point) or 0}
            end
        end
    end
    local diagnostics = {}
    for plan_index, plan in ipairs(alternatives) do
        local slots = {}
        for tag in string.gmatch(plan.slots, "%S+") do slots[#slots+1]=tag end
        if #slots > capacity then
            diagnostics[#diagnostics+1] = "方案" .. plan_index .. "需" .. #slots .. "席，当前" .. capacity .. "席"
        else
            local eligible = {}
            for _, merc in ipairs(pool) do
                if merc.quality >= (plan.minQuality or 4) and not (week == 5 and merc.adds) then
                    eligible[#eligible+1]=merc
                end
            end
            local last_reason = "佣兵类型或数量不足"
            for mode=1,(plan.speedMin or plan.speedMax) and 3 or 1 do
                table.sort(eligible, function(a,b)
                    if mode == 2 and plan.speedMin then
                        local sa=tonumber(a.info.speed) or 0
                        local sb=tonumber(b.info.speed) or 0
                        if sa ~= sb then return sa>sb end
                    end
                    if a.bp ~= b.bp then return a.bp>b.bp end
                    return tostring(a.id)<tostring(b.id)
                end)
                local chosen, match_reason=weighted_assignment(slots,eligible,plan,mode)
                if not chosen then last_reason=match_reason end
                if chosen then
                    local stats, err = formation_stats(troop,chosen)
                    if not stats then
                        last_reason="阵容属性计算失败：" .. err
                    elseif plan.bpMin and stats.bp <= plan.bpMin then
                        last_reason="战力需高于" .. plan.bpMin .. "，当前" .. stats.bp
                    elseif plan.speedMin and stats.speed <= plan.speedMin then
                        last_reason="先攻需高于" .. plan.speedMin .. "，当前" .. stats.speed
                    elseif plan.speedMax and stats.speed >= plan.speedMax then
                        last_reason="先攻需低于" .. plan.speedMax .. "，当前" .. stats.speed
                    else
                        return {chosen=chosen, plan=plan, index=plan_index, stats=stats, slots=slots}
                    end
                end
            end
            diagnostics[#diagnostics+1]="方案" .. plan_index .. "：" .. last_reason
        end
    end
    return nil, table.concat(diagnostics, "；")
end

function A:start_void()
    self:cancel_task()
    self.void_state={week=scene_manager:GetCurrentSceneName() == "world" and self:guide_week() or nil,
        queried=false, mazes=false, attempts=0, team_for=nil}
    return true
end

function A:void_step()
    local state=self.void_state
    if not state then return reply(self,"虚空任务已停止",{done=true}) end
    local waiting=self:task_barrier();if waiting then return waiting end
    if scene_manager:GetCurrentSceneName() ~= "world" then return reply(self,"等待进入游戏",{wait=true}) end
    if not state.week then state.week=self:guide_week() end
    if self:guide_week() ~= state.week then
        return reply(self,"服务器日期已变化，请重新启动以切换星期攻略",{done=true})
    end
    if self.inBattle and self.cfg.autoSkipBattle==false then return reply(self,"等待虚空战斗结束",{wait=true}) end
    self:hide_panel("reward_panel")
    self:hide_panel("vanity_adventure_result")
    local troop=require("logic.troop")
    local pending=self._task_pending
    if pending then
        if not pending.response then
            if task_now(self)-pending.sent_at > 20 then
                return reply(self,"虚空请求超时，已停止；请确认游戏状态后重试",{done=true,error=true})
            end
            return reply(self,"等待虚空服务器响应",{wait=true})
        end
        self._task_pending=nil
        local result=pending.response.result
        if pending.kind == "void_query" then
            state.queried=true
        elseif pending.kind == "void_mazes" then
            state.mazes=true
        elseif pending.kind == "void_team" then
            if result ~= "success" then return reply(self,"虚空上阵失败：" .. tostring(result),{done=true,error=true}) end
            -- Validate server acknowledgement against the exact requested IDs.
            for i,id in ipairs(state.formation) do
                if (troop.vanity_troop or {})[i] ~= id then
                    return reply(self,"虚空阵容同步不一致，请重试",{done=true,error=true})
                end
            end
            state.team_for=state.maze
            return reply(self,"攻略阵容同步成功，即将挑战",{action="阵容同步"})
        elseif pending.kind == "void_claim" then
            if result ~= "success" then return reply(self,"虚空奖励领取失败：" .. tostring(result),{done=true,error=true}) end
            state.team_for=nil; state.attempts=0
            return reply(self,"已领取佣兵奖励，继续下一关",{action="领取奖励"})
        elseif pending.kind == "void_challenge" then
            if result ~= "success" and result ~= "battle_failure" then
                return reply(self,"虚空挑战未被接受：" .. tostring(result),{done=true,error=true})
            end
            state.team_for=nil
            if result == "success" then state.attempts=0; self.stats.wins=self.stats.wins+1
            else state.attempts=state.attempts+1; self.stats.losses=self.stats.losses+1 end
            -- The game updates stage state and battle counts in its battle-end callback.
            return reply(self,result == "success" and "虚空战斗胜利，检查奖励与下一关" or "虚空战斗失败，重新检查攻略阵容",{action="战斗结算"})
        end
    end
    if not state.queried then
        self:task_request("query_vanity_troop_ret","void_query",function() troop:QueryVantiyToopInfo() end)
        return reply(self,"读取虚空佣兵 · " .. WEEKDAYS[state.week] .. "攻略",{wait=true})
    end
    if not state.mazes then
        self:task_request("query_vanity_maze_states_ret","void_mazes",function()
            network:Send({query_vanity_maze_states={}})
        end)
        return reply(self,"读取今日虚空关卡",{wait=true})
    end
    local states=troop.vanity_maze_state_list or {}
    local maze_states=constants.VANITY_MAZE_STATE
    for maze=1,(tonumber(constants.VANITY_MAX_MAZE_ID) or 6) do
        if states[maze] == maze_states.challenge_success then
            self:task_request("get_vanity_mercenary_ret","void_claim",function() troop:GetVanityMercenaryByMazeId(maze) end)
            return reply(self,maze == 1 and "领取今日初始佣兵" or "领取第" .. (maze-1) .. "关佣兵奖励",{action="领取虚空奖励"})
        end
    end
    local maze
    for index=2,(tonumber(constants.VANITY_MAX_MAZE_ID) or 6) do
        if states[index] == maze_states.challenge_able then maze=index; break end
    end
    if not maze then return reply(self,"今日虚空没有可挑战关卡（已完成或尚未开放）",{done=true}) end
    if state.attempts >= 3 then return reply(self,"本关连续失败3次，已停止，请检查攻略阵容",{done=true,error=true}) end
    if state.team_for ~= maze then
        local capacity=#(troop.vanity_troop or {})
        local plan, reason=self:match_guide(troop,state.week,maze-1,capacity)
        if not plan then
            return reply(self,WEEKDAYS[state.week] .. "第" .. (maze-1) .. "关不满足攻略：" .. reason,{done=true,error=true})
        end
        local formation, names={},{}
        for i,merc in ipairs(plan.chosen) do formation[i]=merc.id; names[i]=merc.name end
        for i=#formation+1,capacity do formation[i]=0 end
        state.maze=maze; state.formation=formation
        -- Required by the game's vanity_sync_troop_ret handler.
        troop.change_troop=formation
        self:task_request("vanity_sync_troop_ret","void_team",function()
            network:Send({vanity_sync_troop={troop=formation}})
        end)
        self:log(WEEKDAYS[state.week] .. "第" .. (maze-1) .. "关方案" .. plan.index .. "：" .. plan.plan.slots)
        self:log("阵容：" .. table.concat(names," → ") .. "；战力" .. plan.stats.bp .. "，先攻" .. plan.stats.speed)
        return reply(self,WEEKDAYS[state.week] .. "第" .. (maze-1) .. "关 · 方案" .. plan.index .. " · " .. #plan.chosen .. "名佣兵",{action="按攻略上阵"})
    end
    self:task_request("vanity_challenge_ret","void_challenge",function() troop:FightingByMazeId(maze) end)
    return reply(self,"挑战" .. WEEKDAYS[state.week] .. "第" .. (maze-1) .. "关（游戏地图" .. maze .. "）",{action="虚空挑战"})
end

A._guide = GUIDE
A._skill_tags = skill_tags

-- @DAILY@

-- @MINE@

-- @WORKSHOP@

-- @GROWTH@

-- @FAST_TASKS@

-- @REFILL@

-- @MINING_RUNTIME@
-- @MINING_ROUTE@

-- @MINING_CAMERA@

-- @VOID_PLANNER@

-- @VOID_ROUTE@

-- @RUNE_LOOP@

-- @DAILY_TAIL@
-- @DAILY_COMPLETION@

-- @RUNE_RECOVERY@

-- @ORE_PACKS@
-- @ADVENTURE_PUSH@

-- @DAILY_CART@

-- @REQUEST_RETRY@

-- @TEST_MERC@
