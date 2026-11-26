-- ============================================================
-- 冒险与挖矿 · 矿区自动助手 (Lua core)
-- 只调用游戏客户端已有的正常操作接口，不修改服务器数据。
-- ============================================================
if __MWA and __MWA.VERSION == "1.0.0" then
    return __MWA
end

local mining = require "logic.mining"
local graphic = require "logic.graphic"
local config_manager = require "logic.config_manager"
local constants = require "util.constants"
local scene_manager = require "scene.scene_manager"
local time_logic = require "logic.time"
local user_logic = require "logic.user"
local resource_logic = require "logic.resource"
local bag_logic = require "logic.bag"
local network = require "util.network"

local BT = constants.BLOCK_TYPE
local CT = constants.BLOCK_COLLECT_TYPE
local BOSS_MAP = constants.MINING_BOSS_MAP
local RANDOM_TYPE = constants.RANDOM_EVENT_TYPE

local EMPTY = BT.empty
local PURPLE = BT.rock_purple_gold

local DIRS = {
    { x = 1, y = 0 },
    { x = -1, y = 0 },
    { x = 0, y = 1 },
    { x = 0, y = -1 },
}

local function now_text()
    local ok, value = pcall(os.date, "%H:%M:%S")
    return ok and value or "--:--:--"
end

local A = {
	VERSION = "1.0.0",
    cfg = {
        enabled = false,
        autoMine = true,
        autoGolem = true,
        autoBoss = true,
        autoChest = false,
        autoChest40 = true,
        autoChest41 = true,
        autoChest42 = true,
        autoChest43 = true,
        autoChest44 = false,
        autoChest45 = true,
        autoChest48 = true,
        autoCaveBoss = false,
        autoEnter = true,
        refuseEvents = true,
        allowLongOre = false,
        useTntOnLong = false,
        maxDigTime = 8,
        maxTargetPath = 90,
        lossCooldown = 300,
        nightmareCooldown = 120,
        caveBossInterval = 120,
        actionInterval = 0.03,
        refillInterval = 0,
        autoSkipBattle = true,
        battleSkipDelay = 0,
        skipOneDemonKeyChest = true,
        autoUseMiningBag = true,
        autoTreasureEvent = true,
        layerMinDepth = 20,
        layerMaxDepth = 60,
        layerStep = 10,
        horizontalMining = true,
        horizontalStartDepth = 150,
        rushAvoidGems = true,
        autoMute = true,
        arenaAllowBloodRefresh = false,
        autoUseEarthHeart = true,
        followMiningCamera = true,
        showMiningRoute = false,
        earthUseAll = false,
        dailyRefine = true,
        refineReserve = 1000,
        dailyAlchemyTier = 1,
        dailyTrollLevel = 0,
        dailyUndergroundLevel = 0,
        runeUseFlashGold = true,
        workshopDepth = 150,
        workshopMinutes = 120,
        guildBossCount = 3,
        spendBudget = 360,
        spendBase = 0,
        spendDay = 0,
        spendAllowCaveBuy = true,
        reserveTnt = 0,
    },
    stats = {
        digs = 0,
        collects = 0,
        golems = 0,
        bosses = 0,
        chests = 0,
        caveBoss = 0,
        wins = 0,
        losses = 0,
        errors = 0,
    },
    logs = {},
    failed = {},
    -- 结构化审计与消费策略状态（C# 按 audit_seq 增量拉取/持久化基数）
    audit_seq = 0,
    audit_buf = {},
    spend_session = 0,
    -- 挖矿统计（1.16）：本次自动挖矿的矿镐消耗与资源产出；C# 按天累计落盘。
    mining_stats = { picks = 0, res = {}, active = false, prev = 0 },
    fight = nil,
    status = "已暂停",
    detail = "",
    context = "",
    startedAt = os.time(),
}

__MWA = A

function A:log(message)
    local line = now_text() .. "  " .. tostring(message)
    table.insert(self.logs, 1, line)
    while #self.logs > 60 do
        table.remove(self.logs)
    end
end

function A:set(key, value)
    if self.cfg[key] == nil then
        return false
    end
    if type(self.cfg[key]) == "boolean" then
        value = value == true
    elseif type(self.cfg[key]) == "number" then
        value = tonumber(value) or self.cfg[key]
    end
    if key=="horizontalStartDepth" then
        if value~=value then value=150 end
        value=math.floor(math.max(1,math.min(9999,value)))
        if value~=self.cfg[key] then
            self.horizontal_started=nil;self.horizontal_transition_logged=nil
            self.last_descent_log=nil;self.layer_target=nil;self.last_tunnel_y=nil
        end
    end
    self.cfg[key] = value
    if key == "enabled" then
        if not value and (self.mine_refill or self._mine_event) then
            local p=(self.mine_refill and self.mine_refill.uncertain) or self._task_pending
            if p and not p.response and not p.event:match('^query_') then self._cancelled_task_pending=p end
            self._task_pending=nil;self.mine_refill=nil;self._mine_event=nil
        end
        if not value then self._mine_sync=nil end
        if not value and self.fast_refill and self.fast_refill.owner=="mine" then
            local p=self._task_pending
            if p and not p.response and not p.event:match('^query_') then self._cancelled_task_pending=p end
            self._task_pending=nil;self.fast_refill=nil
        end
        self.status = value and "正在启动…" or "已暂停"
        self.detail = ""
        self._errCount = 0
        self:log(value and "自动助手已启动" or "自动助手已暂停")
    elseif self.cfg.enabled then
        self:log("设置 " .. tostring(key) .. " = " .. tostring(value))
    end
    return true
end

function A:resetStats()
    for key in pairs(self.stats) do
        self.stats[key] = 0
    end
    self.failed = {}
    self.failed_info = {}
    self:log("统计已重置")
end

function A:snapshot()
    local stats = {}
    for key, value in pairs(self.stats) do
        stats[key] = value
    end
    local cfg = {}
    for key, value in pairs(self.cfg) do
        cfg[key] = value
    end
    local logs = {}
    for index = 1, math.min(#self.logs, 8) do
        logs[index] = self.logs[index]
    end
    local mining_state = nil
    if type(mining) == "table" and type(mining.block_list) == "table" then
        mining_state = {
            digCount = tonumber(mining.dig_count) or 0,
            digMax = tonumber(mining.dig_max_count) or 0,
            golemLv = tonumber(mining.golem_lv) or 0,
            golemNum = tonumber(mining.golem_num) or 0,
            depth = tonumber(mining.dig_depth) or 0,
            pickaxe = tonumber(mining.pickaxe_id) or 0,
        }
    end
    return {
        version = self.VERSION,
        enabled = self.cfg.enabled,
        status = self.status,
        detail = self.detail,
        context = self.context,
        stats = stats,
        cfg = cfg,
        logs = logs,
        mining = mining_state,
        miningRoute = self.cfg.enabled and self.cfg.showMiningRoute and self.mining_route_preview or nil,
        miningWait = self.cfg.enabled and self:mining_wait_view() or nil,
        mine = (type(self.mine_view) == "function") and (function()
            local ok, view = pcall(function() return self:mine_view() end)
            return ok and view or nil
        end)() or nil,
        workshop = (type(self.workshop_view) == "function") and (function()
            local ok, view = pcall(function() return self:workshop_view() end)
            return ok and view or nil
        end)() or nil,
        guild = (type(self.guild_view) == "function") and (function()
            local ok, view = pcall(function() return self:guild_view() end)
            return ok and view or nil
        end)() or nil,
        auditSeq = self.audit_seq or 0,
        miningStats = (type(self.mining_stats_snapshot) == "function") and (function()
            local ok, view = pcall(function() return self:mining_stats_snapshot() end)
            return ok and view or nil
        end)() or nil,
        spendTotal = (type(self.spend_total) == "function") and self:spend_total() or 0,
        spendDay = (type(self.spend_day_now) == "function") and self:spend_day_now() or 0,
        spendBudget = tonumber(self.cfg.spendBudget) or 0,
        account = (type(self.harvest_account) == "function") and self:harvest_account() or nil,
        refine = (function()
            local ok, rr = pcall(require, "logic.resource_recycle")
            local temp = ok and type(rr) == "table" and tonumber(rr.temperature) or nil
            local proc = ok and type(rr) == "table" and tonumber(rr.process) or nil
            if temp == nil and self.daily_state and self.daily_state.refine_loaded then
                temp = tonumber(self.daily_state.refine_temperature)
                proc = tonumber(self.daily_state.refine_process)
            end
            if temp == nil and proc == nil then return nil end
            return { temperature = temp or 0, process = proc or 0, max = 10000 }
        end)(),
        fight = self.fight and {
            x = self.fight.x,
            y = self.fight.y,
            label = self.fight.label,
            phase = self.fight.phase,
        } or nil,
    }
end

-- ------------------------------------------------------------
-- 基础读取
-- ------------------------------------------------------------
local function block_at(grid, x, y)
    local row = grid[y]
    return row and row[x] or nil
end

local function block_cfg(block_type)
    if not block_type then
        return nil
    end
    return config_manager.mining_dig_info_config[block_type]
end

local function is_empty(block_type)
    return block_type == EMPTY
end

local function is_boss(block_type)
    return block_type ~= nil and BOSS_MAP[block_type] ~= nil
end

local function is_chest(block_type)
    local cfg = block_cfg(block_type)
    return cfg ~= nil and (cfg.collect_type or 0) == CT.chest
end

local function is_target(block_type)
    local cfg = block_cfg(block_type)
    return cfg ~= nil and (cfg.collect_type or 0) >= CT.golem
end

local function is_free_chest(block_type)
    return block_type==(BT.chest1 or 40)
end

local CHEST_OPTION_KEYS = {
    [BT.chest1 or 40] = "autoChest40",
    [BT.chest2 or 41] = "autoChest41",
    [BT.chest3 or 42] = "autoChest42",
    [BT.chest4 or 43] = "autoChest43",
    [BT.chest5 or 44] = "autoChest44",
    [BT.chest6 or 45] = "autoChest45",
    [BT.chest7 or 48] = "autoChest48",
}

local function is_one_demon_key_chest_block(block_type)
    -- 烈焰金宝箱 = 1 恶魔钥匙；默认不勾选，可以在宝箱类别里单独开启。
    return block_type == (BT.chest5 or 44)
end

function A:is_one_demon_key_event(event_id)
    local event_config = config_manager.event_config
    local conf = event_config and event_id and event_config[event_id]
    if not conf then
        return false
    end
    local ids = {}
    local nums = {}
    for value in string.gmatch(conf.need_resource_id or "", "%d+") do
        ids[#ids + 1] = tonumber(value)
    end
    for value in string.gmatch(conf.need_num or "", "%d+") do
        nums[#nums + 1] = tonumber(value)
    end
    local demon_key = constants.RESOURCE_TYPE and constants.RESOURCE_TYPE.chest_key3
    demon_key = tonumber(demon_key) or 40
    return #ids == 1 and #nums == 1 and ids[1] == demon_key and nums[1] == 1
end

local function current_pickaxe()
    local pickaxe_id = tonumber(mining.pickaxe_id) or 1
    return config_manager.mining_pickaxe_config[pickaxe_id]
        or config_manager.mining_pickaxe_config[1]
        or { destroy_level = 1, efficiency = 1 }
end

local function dig_time(block_type, pickaxe)
    local cfg = block_cfg(block_type)
    if not cfg then
        return 999999
    end
    local efficiency = (pickaxe and pickaxe.efficiency) or 1
    local value = cfg["need_time" .. efficiency]
    if type(value) ~= "number" then
        value = cfg.need_time1
    end
    if type(value) ~= "number" then
        return 999999
    end
    return value
end

local function can_dig(block_type, pickaxe, max_time, allow_long)
    local cfg = block_cfg(block_type)
    if not cfg then
        return false
    end
    if (cfg.collect_type or 0) >= CT.golem then
        return false
    end
    if cfg.ID == PURPLE then
        return false
    end
    if (cfg.destroy_level or 0) <= 0 then
        return false
    end
    if (cfg.destroy_level or 0) > ((pickaxe and pickaxe.destroy_level) or 1) then
        return false
    end
    local seconds = dig_time(block_type, pickaxe)
    if not allow_long and seconds > (max_time or 8) then
        return false
    end
    return true
end

local GEM_BLOCK_IDS = {
    [8] = true,  -- 红宝石
    [19] = true, -- 紫宝石
    [21] = true, -- 绿宝石
    [22] = true, -- 黄宝石
    [23] = true, -- 钻石
    [24] = true, -- 富钻石
}

local function is_gem_block(block_type)
    if GEM_BLOCK_IDS[block_type] then
        return true
    end
    local cfg = block_cfg(block_type)
    if not cfg then
        return false
    end
    local name = tostring(cfg.name or "")
    local resource_id = tonumber(cfg.output_resource_id) or 0
    local resource_config = config_manager.resource_config
    local resource = resource_config and resource_config[resource_id]
    if resource and resource.name then
        name = name .. tostring(resource.name)
    end
    return string.find(name, "宝石", 1, true) ~= nil
        or string.find(name, "钻石", 1, true) ~= nil
end

local function adjacent_empty(grid, x, y)
    for index = 1, #DIRS do
        local dir = DIRS[index]
        if is_empty(block_at(grid, x + dir.x, y + dir.y)) then
            return true
        end
    end
    return false
end

local function unknown_neighbor_count(grid, x, y)
    local count = 0
    for index = 1, #DIRS do
        local dir = DIRS[index]
        local nx, ny = x + dir.x, y + dir.y
        local row = grid[ny]
        if type(row) ~= "table" or row[nx] == nil then
            count = count + 1
        end
    end
    return count
end

-- ------------------------------------------------------------
-- 0-1 BFS：从目标四邻出发，找到最近的一段“已挖空区域”
-- 返回需要依次挖掉的方块列表（从靠近空地的 First 到靠近目标的 Last）
-- ------------------------------------------------------------
local function route_work()
    local b=A._route_budget
    if not b then return end
    b.work=b.work+1
    if b.work%32==0 and (b.work>=256 or (b.clock and os.clock()-b.started>=0.004)) then
        coroutine.yield();b.work=0;b.started=b.clock and os.clock() or 0
    end
end
local function find_path(grid, pickaxe, target_x, target_y, max_blocks, max_time, allow_long, avoid_gems, explore, node_limit)
    max_blocks = tonumber(max_blocks) or 90
    local best_starts = {}
    local immediate = false
    local function passable(kind)
        return (explore and kind==nil) or can_dig(kind,pickaxe,max_time,allow_long) or is_free_chest(kind)
    end

    for index = 1, #DIRS do
        local dir = DIRS[index]
        local x, y = target_x + dir.x, target_y + dir.y
        local block_type = block_at(grid, x, y)
        if is_empty(block_type) then
            immediate = true
            break
        end
        if passable(block_type)
            and not (avoid_gems and is_gem_block(block_type)) then
            table.insert(best_starts, { x = x, y = y })
        end
    end

    if immediate then
        return {}, true
    end
    if #best_starts == 0 then
        return nil, false
    end

    local dist = {}
    local parent = {}
    local queue = {}
    local head, tail = 1, 0
    local visited = {}
    local limit = node_limit or math.max(2000, max_blocks * 160)
    local processed = 0

    local function key(x, y)
        return tostring(x) .. ":" .. tostring(y)
    end

    local function push_back(x, y, distance)
        tail = tail + 1
        queue[tail] = { x = x, y = y, d = distance }
    end

    local function push_front(x, y, distance)
        head = head - 1
        queue[head] = { x = x, y = y, d = distance }
    end

    for index = 1, #best_starts do
        local start = best_starts[index]
        local k = key(start.x, start.y)
        if dist[k] == nil or dist[k] > 1 then
            dist[k] = 1
            parent[k] = nil
            push_back(start.x, start.y, 1)
        end
    end

    while head <= tail and processed < limit do
        route_work()
        local node = queue[head]
        queue[head] = nil
        head = head + 1
        local k = key(node.x, node.y)
        local known = dist[k]
        if known == node.d and not visited[k] then
            visited[k] = true
            processed = processed + 1
            local block_type = block_at(grid, node.x, node.y)

            if is_empty(block_type) then
                local digs = {}
                local cx, cy = node.x, node.y
                local guard = 0
                while true do
                    local pk = key(cx, cy)
                    local p = parent[pk]
                    if not p or guard > max_blocks + 2 then
                        break
                    end
                    cx, cy = p.x, p.y
                    if not is_empty(block_at(grid, cx, cy)) then
                        digs[#digs + 1] = { x = cx, y = cy, free_chest=is_free_chest(block_at(grid,cx,cy)),unknown=block_at(grid,cx,cy)==nil }
                    end
                    guard = guard + 1
                end
                if #digs <= max_blocks then
                    return digs, false
                end
                return nil, false
            end

            for index = 1, #DIRS do
                local dir = DIRS[index]
                local nx, ny = node.x + dir.x, node.y + dir.y
                local next_type = block_at(grid, nx, ny)
                local nk = key(nx, ny)
                if is_empty(next_type) then
                    local nd = node.d
                    if nd <= max_blocks and (dist[nk] == nil or nd < dist[nk]) then
                        dist[nk] = nd
                        parent[nk] = { x = node.x, y = node.y }
                        push_front(nx, ny, nd)
                    end
                elseif passable(next_type)
                    and not (avoid_gems and is_gem_block(next_type)) then
                    local nd = node.d + 1
                    if nd <= max_blocks and (dist[nk] == nil or nd < dist[nk]) then
                        dist[nk] = nd
                        parent[nk] = { x = node.x, y = node.y }
                        push_back(nx, ny, nd)
                    end
                end
            end
        end
    end

    return nil, false
end

-- ------------------------------------------------------------
-- 目标与前沿
-- ------------------------------------------------------------
function A:target_label(target)
    if target.treasure then
        return "藏宝箱"
    elseif target.boss then
        return "矿区BOSS"
    elseif target.chest then
        return "宝箱"
    end
    return "魔像"
end

function A:is_failed(x, y)
    local key=tostring(x)..":"..tostring(y)
    local info=self.failed_info and self.failed_info[key]
    if info and (info.kind~=mining:GetBlockType(x,y) or info.generation~=mining.stream_generation) then
        self.failed[key]=nil;self.failed_info[key]=nil;return false
    end
    local expires = self.failed[key]
    return expires ~= nil and expires > self.now
end

function A:blacklist(x, y, seconds, reason)
    self.failed[tostring(x) .. ":" .. tostring(y)] = self.now + (seconds or self.cfg.lossCooldown)
    self.failed_info=self.failed_info or {}
    self.failed_info[tostring(x)..":"..tostring(y)]={kind=mining:GetBlockType(x,y),generation=mining.stream_generation,reason=reason}
    if reason then
        self:log(reason .. "，冷却 " .. tostring(math.floor(seconds or self.cfg.lossCooldown)) .. " 秒")
    end
end

function A:prune_failed()
    local now = self.now
    for key, expires in pairs(self.failed) do
        if expires <= now then
            self.failed[key] = nil
            if self.failed_info then self.failed_info[key]=nil end
        end
    end
end

function A:scan_targets()
    local grid = mining.block_list
    local targets = {}
    if type(grid) ~= "table" then
        return targets
    end
    local boss_time = tonumber(mining.boss_time) or 0
    local game_now = time_logic:Now()
    for y, row in pairs(grid) do
        for x, block_type in pairs(row) do
            if is_target(block_type) then
                local cfg = block_cfg(block_type)
                local boss = is_boss(block_type)
                local chest = (cfg.collect_type or 0) == CT.chest
                local allowed = false
                if boss then
                    allowed = self.cfg.autoBoss
                    if allowed and boss_time > 0 and boss_time <= game_now then
                        allowed = false
                    end
                elseif chest then
                    allowed = self.cfg.autoChest
                    local option_key = CHEST_OPTION_KEYS[block_type]
                    if allowed and option_key and self.cfg[option_key] == false then
                        allowed = false
                    end
                else
                    allowed = self.cfg.autoGolem
                end
                local deferred=self:is_failed(x,y)
                if allowed and (not deferred or (not boss and not chest)) then
                    local manhattan = math.abs(x - (mining.cur_position.x or 0))
                        + math.abs(y - (mining.cur_position.y or 0))
                    targets[#targets + 1] = {
                        x = x,
                        y = y,
                        block_type = block_type,
                        boss = boss,
                        chest = chest,
                        golem = not boss and not chest,
                        manhattan = manhattan,
                        label = boss and "矿区BOSS" or (chest and "宝箱" or "魔像"),
                        defer_until=deferred and self.failed[tostring(x)..":"..tostring(y)] or nil,
                    }
                end
            end
        end
    end

    -- Near visible markers can arrive before their streamed row. Request that
    -- row rather than spending picks while silently overlooking the monster.
    if self.cfg.autoGolem then
        for _,pos in pairs(mining.golem_coordinates or {})do
            local x,y=tonumber(pos.x),tonumber(pos.y)
            if x and y and block_at(grid,x,y)==nil then
                local distance=math.abs(x-mining.cur_position.x)+math.abs(y-mining.cur_position.y)
                if distance<=18 then targets[#targets+1]={x=x,y=y,golem=true,manhattan=distance,label="魔像",needs_snapshot=true} end
            end
        end
    end

    if self.cfg.autoTreasureEvent and type(mining.random_event_list) == "table" then
        for index = 1, #mining.random_event_list do
            local event = mining.random_event_list[index]
            local position = event and event.position
            if event and event.event_type == RANDOM_TYPE.treasure_hunt
                and position and position.x and position.y
                and (tonumber(event.end_time) or 0) > game_now
                and not self:is_failed(position.x, position.y) then
                local manhattan = math.abs(position.x - (mining.cur_position.x or 0))
                    + math.abs(position.y - (mining.cur_position.y or 0))
                targets[#targets + 1] = {
                    x = position.x,
                    y = position.y,
                    treasure = true,
                    boss = false,
                    chest = false,
                    event = event,
                    manhattan = manhattan,
                    label = "藏宝箱",
                }
            end
        end
    end
    return targets
end

function A:frontier(include_free_chests)
    local grid = mining.block_list
    local pickaxe = current_pickaxe()
    local result = {}
    if type(grid) ~= "table" then
        return result
    end
    for y, row in pairs(grid) do
        for x, block_type in pairs(row) do
            local free_chest = include_free_chests and is_free_chest(block_type)
            if (free_chest or can_dig(block_type, pickaxe, self.cfg.maxDigTime, self.cfg.allowLongOre))
                and adjacent_empty(grid, x, y) then
                result[#result + 1] = {
                    x = x,
                    y = y,
                    block_type = block_type,
                    time = free_chest and 0 or dig_time(block_type, pickaxe),
                    free_chest = free_chest,
                }
            end
        end
    end
    return result
end

function A:nearest_target(targets)
    local best = nil
    for index = 1, #targets do
        local target = targets[index]
        if not best or target.manhattan < best.manhattan then
            best = target
        end
    end
    return best
end

function A:note_golems(targets)
    local current = {}
    local new_count = 0
    local first_x, first_y = nil, nil
    for index = 1, #targets do
        local target = targets[index]
        if not target.boss and not target.chest then
            local key = tostring(target.x) .. ":" .. tostring(target.y)
            current[key] = true
            if not (self.seen_golems or {})[key] then
                new_count = new_count + 1
                if not first_x then
                    first_x, first_y = target.x, target.y
                end
            end
        end
    end
    self.seen_golems = current
    if new_count > 0 then
        self:log("发现 " .. tostring(new_count) .. " 个魔像，优先处理 (" .. tostring(first_x) .. "," .. tostring(first_y) .. ")")
    end
end

function A:seek_virtual_target()
    local position = mining.cur_position
    if not position then
        return nil
    end
    local best = nil
    local function consider(x, y, label)
        if not x or not y or (x == 0 and y == 0) then
            return
        end
        if mining:GetBlockType(x, y) ~= nil then
            return
        end
        local distance = math.abs(x - position.x) + math.abs(y - position.y)
        if distance > 130 then
            return
        end
        if not best or distance < best.manhattan then
            best = { x = x, y = y, label = label, manhattan = distance, virtual = true }
        end
    end

    if self.cfg.autoGolem then
        local golems = mining.golem_coordinates or {}
        for index = 1, #golems do
            local point = golems[index]
            if point and point.x and point.y then
                consider(point.x, point.y, "魔像")
            end
        end
    end
    if self.cfg.autoBoss then
        local boss = mining.boss_pos
        local boss_time = tonumber(mining.boss_time) or 0
        if boss and boss_time > time_logic:Now() then
            consider(boss.x, boss.y, "矿区BOSS")
        end
    end
    return best
end

function A:ensure_layer_target()
    local position = mining.cur_position or {}
    local y = tonumber(position.y) or 0
    local min_depth = tonumber(self.cfg.layerMinDepth) or 20
    local max_depth = tonumber(self.cfg.layerMaxDepth) or 60
    if not self.layer_target then
        self.layer_target = math.max(y, min_depth)
    end
    if self.layer_target > max_depth then
        self.layer_target = max_depth
    end
    if not self.layer_dir then
        self.layer_dir = 1
    end
    return self.layer_target, min_depth, max_depth
end

function A:horizontal_frontier(options, row, direction)
    local position = mining.cur_position or {}
    local current_x = tonumber(position.x) or 0
    local best = nil
    local best_score = nil
    for index = 1, #options do
        local option = options[index]
        if option.y == row then
            local dx = option.x - current_x
            if (direction > 0 and dx > 0) or (direction < 0 and dx < 0) then
                local unknown = unknown_neighbor_count(mining.block_list, option.x, option.y)
                local score = math.abs(dx) * 10 - unknown * 3 + option.time * 0.01
                if not best_score or score < best_score then
                    best_score = score
                    best = option
                end
            end
        end
    end
    return best
end

function A:downward_frontier(options, current_y, limit_y)
    local position = mining.cur_position or {}
    local current_x = tonumber(position.x) or 0
    local best = nil
    local best_score = nil
    for index = 1, #options do
        local option = options[index]
        if option.y > current_y and option.y <= limit_y then
            local unknown = unknown_neighbor_count(mining.block_list, option.x, option.y)
            local score = (option.y - current_y) * 100
                + math.abs(option.x - current_x) * 3 - unknown * 2
            if not best_score or score < best_score then
                best_score = score
                best = option
            end
        end
    end
    return best
end

function A:descent_access_frontier(options, row)
    local position = mining.cur_position or {}
    local current_x = tonumber(position.x) or 0
    local pickaxe = current_pickaxe()
    local best = nil
    local best_score = nil
    for index = 1, #options do
        local option = options[index]
        if option.y == row then
            local below = block_at(mining.block_list, option.x, row + 1)
            if below and can_dig(below, pickaxe, self.cfg.maxDigTime, self.cfg.allowLongOre) then
                local dx = math.abs(option.x - current_x)
                local score = dx * 10
                if (option.x - current_x) * self.layer_dir < 0 then
                    score = score + 40
                end
                if not best_score or score < best_score then
                    best_score = score
                    best = option
                end
            end
        end
    end
    return best
end

function A:choose_layer_frontier(options)
    local position = mining.cur_position or {}
    local y = tonumber(position.y) or 0
    local target, min_depth, max_depth = self:ensure_layer_target()
    local step = tonumber(self.cfg.layerStep) or 10

    -- Do not open another horizontal tunnel too close to the previous one.
    -- This is the main pickaxe-saving rule: keep at least layerStep layers
    -- between two horizontal tunnels, even after a map refresh or detour.
    if self.last_tunnel_y and y ~= self.last_tunnel_y
        and y < self.last_tunnel_y + step then
        local desired = math.min(max_depth, self.last_tunnel_y + step)
        if y < desired then
            local down = self:downward_frontier(options, y, desired)
            if down then
                self.layer_target = desired
                return down, "descend_next"
            end
            -- No direct downward block. Open at most one access block that
            -- has a diggable block below it, then continue descending there.
            local access = self:descent_access_frontier(options, y)
            if access then
                self.layer_target = desired
                return access, "access_descend"
            end
            return nil, "blocked"
        end
    end

    -- First get down to the preferred 20-60 band.
    if y < target then
        local down = self:downward_frontier(options, y, target)
        if down then
            return down, "descend"
        end
        local sideways = self:horizontal_frontier(options, y, self.layer_dir)
        if sideways then
            return sideways, "horizontal"
        end
        return nil, "descend"
    end

    -- Once in the band, always try a horizontal block first.
    local row = math.max(y, target)
    local side = self:horizontal_frontier(options, row, self.layer_dir)
    if side then
        return side, "horizontal"
    end
    side = self:horizontal_frontier(options, row, -self.layer_dir)
    if side then
        self.layer_dir = -self.layer_dir
        return side, "horizontal"
    end

    -- Horizontal end: go down one small step, then mine horizontally again.
    if y < max_depth then
        local next_target = math.min(max_depth, math.max(target, y) + step)
        local down = self:downward_frontier(options, y, next_target)
        if down then
            self.layer_target = next_target
            return down, "descend_next"
        end
    end

    -- No straight/downward option (map edge or cave-in): let the fallback
    -- pick any diggable frontier block.
    return nil, "fallback"
end

function A:is_rush_mode()
    if self.cfg.horizontalMining == false then
        return false
    end
    local position = mining.cur_position or {}
    local depth = tonumber(position.y) or 0
    local start_depth = tonumber(self.cfg.horizontalStartDepth) or 150
    if depth >= start_depth then
        self.horizontal_started = true
    end
    return self.horizontal_started == true
end

function A:is_descent_mode()
    if self.cfg.horizontalMining == false then
        return false
    end
    return not self:is_rush_mode()
end

function A:choose_descent_frontier(options)
    local position = mining.cur_position or {}
    local current_x = tonumber(position.x) or 0
    local current_y = tonumber(position.y) or 0
    local best = nil
    local best_score = nil
    for index = 1, #options do
        local option = options[index]
        local dx = option.x - current_x
        local dy = option.y - current_y
        local score
        if dy > 0 then
            -- Prefer straight down; allow digging through a blocking gem while
            -- descending so the selected start depth remains reachable.
            score = math.abs(dx) * 10 + option.time * 0.01
        elseif dy == 0 then
            score = 500 + math.abs(dx) * 30 + option.time * 0.01
        else
            score = 1000 + math.abs(dy) * 120 + math.abs(dx) * 30 + option.time * 0.01
        end
        if not best_score or score < best_score then
            best_score = score
            best = option
        end
    end
    return best
end

function A:choose_rush_frontier(options)
    local position = mining.cur_position or {}
    local current_x = tonumber(position.x) or 0
    local current_y = tonumber(position.y) or 0
    local best = nil
    local best_score = nil
    for index = 1, #options do
        local option = options[index]
        if not (self.cfg.rushAvoidGems and is_gem_block(option.block_type)) then
            local dx = option.x - current_x
            local dy = option.y - current_y
            local score
            if dx > 0 then
                score = 1000 + math.abs(dy) * 80 - math.min(dx, 12) * 6
            elseif dx == 0 then
                score = 2800 + math.abs(dy) * 40
            else
                score = 7000 + math.abs(dx) * 40 + math.abs(dy) * 40
            end
            score = score + option.time * 0.01
            if not best_score or score < best_score then
                best_score = score
                best = option
            end
        end
    end
    return best
end

function A:note_rush_reached()
    self.rush_reached_logged = true
    self.layer_target = nil
    self.last_tunnel_y = nil
end

function A:mine_frontier(target)
    local options = self:frontier()
    if #options == 0 then
        self.status = "没有可挖的方块"
        self.detail = "可能被长矿/不可破坏地形包围，请手动处理或刷新矿区"
        return false
    end

    if not target then
        local x = tonumber(mining.cur_position.x) or 0
        if self:is_descent_mode() then
            local depth = tonumber(mining.cur_position.y) or 0
            local start_depth = tonumber(self.cfg.horizontalStartDepth) or 150
            local down = self:choose_descent_frontier(options)
            if not down then
                self.status = "寻找下挖通道"
                self.detail = "目标深度 " .. tostring(start_depth) .. " · 当前 " .. tostring(depth)
                return false
            end
            self.status = "下挖至深度 " .. tostring(start_depth) .. " · 当前 " .. tostring(depth)
            self.detail = "到达后开始持续横向挖掘"
            if not self.last_descent_log or self.now - self.last_descent_log > 15 then
                self.last_descent_log = self.now
                self:log("向下挖至深度 " .. tostring(start_depth) .. "，当前深度 " .. tostring(depth))
            end
            return self:dig(down.x, down.y, "下挖")
        end
        if self:is_rush_mode() then
            if not self.horizontal_transition_logged then
                self.horizontal_transition_logged = true
                self:log("已到达深度 " .. tostring(self.cfg.horizontalStartDepth or 150) .. "，开始持续横向挖掘")
            end
            self.rush_reached_logged = false
            local rush = self:choose_rush_frontier(options)
            if not rush then
                self.status = "宝石阻挡横向通道"
                self.detail = "等待绕行 · x=" .. tostring(x)
                return false
            end
            self.status = "横向挖掘 x=" .. tostring(x)
            self.detail = "避开宝石 · 深度 " .. tostring(mining.cur_position.y)
            if not self.last_rush_log or self.now - self.last_rush_log > 15 then
                self.last_rush_log = self.now
                self:log("持续横向挖掘 x=" .. tostring(x) .. "，避开宝石")
            end
            return self:dig(rush.x, rush.y, "横向推进")
        end
        self:note_rush_reached()
        self:ensure_layer_target()
        local y = tonumber(mining.cur_position.y) or 0
        local best, phase = self:choose_layer_frontier(options)
        if not best and phase == "blocked" then
            self.status = "上一条矿道太近，寻找下潜通道"
            self.detail = "至少间隔 " .. tostring(self.cfg.layerStep or 10) .. " 层"
            return false
        end
        if not best then
            -- The straight layer and the downward step are both blocked;
            -- take any frontier block that opens the most unknown map.
            local fallback_score = nil
            local current_y = tonumber(mining.cur_position.y) or 0
            local max_depth = tonumber(self.cfg.layerMaxDepth) or 60
            local max_allowed = math.max(current_y, max_depth)
            for index = 1, #options do
                local option = options[index]
                if option.y <= max_allowed then
                    local unknown = unknown_neighbor_count(mining.block_list, option.x, option.y)
                    local distance = mining.cur_position
                        and (math.abs(option.x - mining.cur_position.x)
                            + math.abs(option.y - mining.cur_position.y)) or 0
                    local score = -(unknown * 100) + distance * 0.2 + option.time * 0.01
                    if not fallback_score or score < fallback_score then
                        fallback_score = score
                        best = option
                    end
                end
            end
            phase = "fallback"
        end
        if not best then
            self.status = "横向到尽头，已达最大深度"
            self.detail = "深度 " .. tostring(y) .. " · 不再向下"
            return false
        end

        local target_depth = tonumber(self.layer_target) or y
        if phase == "horizontal" then
            self.status = "深度 " .. tostring(y) .. " 横向挖掘"
            self.detail = "寻找魔像 · 目标层 20-60"
            if not self.last_layer_log or self.now - self.last_layer_log > 15 then
                self.last_layer_log = self.now
                self:log("在深度 " .. tostring(y) .. " 横向挖掘，寻找魔像")
            end
        elseif phase == "descend" then
            self.status = "下潜到目标深度"
            self.detail = "当前 " .. tostring(y) .. " · 目标 " .. tostring(target_depth)
        elseif phase == "descend_next" then
            self.status = "横向到尽头，向下换层"
            self.detail = "下一层 " .. tostring(target_depth)
            if not self.last_layer_log or self.now - self.last_layer_log > 10 then
                self.last_layer_log = self.now
                self:log("横向到尽头，下潜到深度 " .. tostring(target_depth))
            end
        elseif phase == "access_descend" then
            self.status = "打通下潜通道"
            self.detail = "目标深度 " .. tostring(target_depth)
        else
            self.status = "寻找可挖方向"
            self.detail = "向未探索区域开拓"
        end
        local dug = self:dig(best.x, best.y, "横向/向下开拓")
        if dug and phase == "horizontal" then
            self.last_tunnel_y = y
        end
        return dug
    end

    local best = nil
    local best_score = nil
    local avoid_gems = self:is_rush_mode() and not (target.boss or target.chest)
    for index = 1, #options do
        local option = options[index]
        if not (avoid_gems and is_gem_block(option.block_type)) then
            local distance = math.abs(option.x - target.x) + math.abs(option.y - target.y)
            local score = distance * 10 + option.time
            if not best_score or score < best_score then
                best_score = score
                best = option
            end
        end
    end

    if not best then
        self.status = "宝石挡住前往目标的通道"
        self.detail = tostring(target.label or "目标")
        return false
    end
    return self:dig(best.x, best.y, "前往" .. target.label)
end

-- ------------------------------------------------------------
-- 动作
-- ------------------------------------------------------------
function A:pending_dig_resolved()
    local pending = self.pending_dig
    if not pending then
        return true
    end
    if self.inBattle or self.fight or mining.__mw_pending_monster then
        self.pending_dig = nil
        return true
    end
    local block_type = mining:GetBlockType(pending.x, pending.y)
    local endtime = tonumber(mining.dig_endtime) or 0
    if block_type ~= pending.block_type
        or endtime > (pending.game_at or 0) + 0.001 then
        self.pending_dig = nil
        return true
    end
    -- Unconfirmed mutations must not be resent just because a timer expired.
    if self.now - (pending.at or 0) > 20 then
        self.cfg.enabled=false
        self.status="挖掘响应超时，已暂停"
        self:log("未收到挖掘确认，自动暂停；检查网络后重新加载游戏再启动")
    end
    return false
end

function A:can_act(interval)
    if self.now < (self.blocked_until or 0) then
        return false
    end
    if not self:pending_dig_resolved() then
        return false
    end
    -- DigOrCollectBlock silently rejects another dig while the previous
    -- terrain timer is still running. Wait only for that timer, not a long
    -- fixed interval.
    local game_now = time_logic:Now()
    if (tonumber(mining.dig_endtime) or 0) > game_now + 0.01 then
        return false
    end
    local wait = math.max(tonumber(interval) or 0, tonumber(self.cfg.actionInterval) or 0.03)
    if self.now - (self.last_action_at or -999) < wait then
        return false
    end
    return true
end

function A:mark_action()
    self.last_action_at = self.now
end

function A:effective_dig_count()
    local base = tonumber(mining.dig_count) or 0
    local boost = tonumber(self.bag_boost) or 0
    if boost > 0 and self.bag_boost_at and self.now - self.bag_boost_at > 6 then
        self.bag_boost = nil
        self.bag_boost_at = nil
        boost = 0
    end
    return base + boost
end

function A:try_use_mining_bag()
    local waiting=self:task_barrier()
    if waiting then self.status=waiting.message;return true end
    if self.fast_refill then
        local r=self:refill_step()
        if r and r.refill_done and r.error then
            self.cfg.enabled=false;self.status=r.error
        end
        return true
    end
    if not self.cfg.autoUseMiningBag and self.cfg.autoUseEarthHeart==false then return false end
    local needed=(tonumber(mining.golem_num) or 0)+1
    if self:effective_dig_count()>=needed then return false end
    -- Refill to capacity once instead of returning to the bag every dig.
    needed=math.max(needed,tonumber(mining.dig_max_count) or needed)
    self:start_refill("mine",needed)
    return true
end

function A:mining_on_response(name,msg)
    local p=self.pending_dig
    if name~="dig_block_ret" or not p then return end
    local pos=msg.cur_position
    if msg.result=="success" and pos and pos.x==p.x and pos.y==p.y then
        self.pending_dig=nil
    elseif msg.result and msg.result~="success" then
        self.pending_dig=nil;self.blocked_until=self.now+1
        self:log("挖掘失败："..tostring(msg.result).."，稍后重试")
    end
end

function A:dig(x, y, hint)
    if not self:can_act() then
        return false
    end
    if not mining:CheckConnected(x, y) then
        return false
    end
    local before_type = mining:GetBlockType(x, y)
    self:set_mining_route(self.mining_route_target,{x=x,y=y,blast=self._mine_blast~=nil or nil},hint)
    local pending={x=x,y=y,at=self.now,game_at=time_logic:Now(),block_type=before_type}
    self.pending_dig=pending
    local result = mining:DigOrCollectBlock(x, y)
    if result~="success" and result~=true and self.pending_dig==pending then self.pending_dig=nil end
    if result == "lack_count" then
        self.bag_boost = nil
        self.bag_boost_at = nil
        self.next_bag_at = 0
        self.blocked_until = self.now + 1.5
        self.status = self.cfg.autoUseMiningBag and "矿镐不足，准备使用矿工包" or "矿镐不足"
        self.detail = "等待矿镐恢复；需要 " .. tostring((mining.golem_num or 0) + 1) .. " 点"
        return false
    elseif result == "pending_event" then
        self.status = "随机事件处理中"
        return false
    elseif result~="success" and result~=true then
        self.status="矿格暂未接受挖掘，重新核对可挖状态"
        self.blocked_until=self.now+0.1
        return false
    end
    self:mark_action()
    self.stats.digs = self.stats.digs + 1
    self.status = (hint or "挖矿") .. " (" .. tostring(x) .. "," .. tostring(y) .. ")"
    self.detail = ""
    return true
end

function A:open_treasure(target)
    if not self:can_act(1.0) then
        return false
    end
    if self.treasure_open_at and self.now - self.treasure_open_at < 3 then
        self.status = "等待藏宝箱开启"
        self.detail = "(" .. tostring(target.x) .. "," .. tostring(target.y) .. ")"
        return true
    end
    if not mining:CheckConnected(target.x, target.y) then
        return false
    end
    self.treasure_open_at = self.now
    local ok, opened = pcall(function()
        return mining:CheckTreasureHuntEvent(target.x, target.y)
    end)
    if ok and opened then
        self:mark_action()
        self.status = "开启藏宝箱"
        self.detail = "(" .. tostring(target.x) .. "," .. tostring(target.y) .. ")"
        self:log("开启藏宝箱 (" .. tostring(target.x) .. "," .. tostring(target.y) .. ")")
        self.close_reward_until = self.now + 6
        return true
    end
    self:blacklist(target.x, target.y, 20, "藏宝箱暂时无法开启")
    self.status = "藏宝箱暂时无法开启"
    return false
end

function A:start_target(target)
    if target.treasure then
        return self:open_treasure(target)
    end
    if not self:can_act() then
        return false
    end
    local x, y = target.x, target.y
    if not mining:CheckConnected(x, y) then
        return false
    end
    mining.cur_event_id = nil
    mining.cur_event_position = nil
    self:install_task_hooks()
    self:install_request_pipeline()
    self.fight = {
        x = x,
        y = y,
        block_type = target.block_type,
        label = target.label,
        boss = target.boss,
        chest = target.chest,
        golem = target.golem,
        free_passage = target.free_passage,
        phase = "query",
        at = self.now,
        wait_started = self.now,
        solved_at = nil,
        transport = {event="query_golem_info_ret",kind="mining_target",sent_at=self.now},
    }
    self:mark_action()
    self.status = "挑战" .. target.label
    self:set_mining_route(self.mining_route_target or target,target)
    self.detail = "(" .. tostring(x) .. "," .. tostring(y) .. ")"
    self:log("发起挑战：" .. target.label .. " (" .. tostring(x) .. "," .. tostring(y) .. ")")
    local result = mining:DigOrCollectBlock(x, y)
    if result == "pending_event" or result == "lack_count" then
        self.fight = nil
        self.status = "暂时无法挑战"
        self.detail = "随机事件或矿镐不足"
        return false
    end
    return true
end

function A:hide_panel(name)
    local scene = cc.Director:getInstance():getRunningScene()
    local panel = scene and scene.sub_panels and scene.sub_panels[name]
    if not panel or not panel:IsVisible() then
        return
    end
    pcall(function()
        graphic:DispatchEvent("hide_world_sub_panel", name)
    end)
end

function A:update_fight()
    local fight = self.fight
    if not fight then
        return false
    end
    local transport=fight.transport
    if transport and transport.queued and self:request_channel_available() then
        network:Send(transport._transport_message or transport.message)
    end

    if fight.phase == "query" then
        local position = mining.cur_event_position
        local event_id = mining.cur_event_id
        if position and event_id and position.x == fight.x and position.y == fight.y then
            if fight.free_passage then
                local conf=(config_manager.event_config or {})[event_id]
                local costly=not conf or (tonumber(conf.need_item_id) or 0)>0
                for value in string.gmatch(conf and conf.need_num or "","%d+")do if tonumber(value)>0 then costly=true end end
                if costly then
                    self:blacklist(fight.x,fight.y,2,"开口箱回包包含费用，暂缓通路清理")
                    self.fight=nil;self.status="免费通路费用未确认，暂缓开箱";return true
                end
            end
            if fight.chest and self.cfg.autoChest44 ~= true
                and self:is_one_demon_key_event(event_id) then
                self:hide_panel("confirm_msgbox")
                self:hide_panel("event_panel")
                self.fight = nil
                self:blacklist(fight.x, fight.y, 300, "跳过只消耗 1 个恶魔钥匙的宝箱")
                self.status = "跳过一个恶魔钥匙宝箱"
                self.detail = "不消耗恶魔钥匙"
                self:log("跳过只消耗 1 个恶魔钥匙的宝箱 (" .. tostring(fight.x) .. "," .. tostring(fight.y) .. ")")
                return true
            end
            fight.phase = "solve"
            fight.event_id = event_id
            fight.solved_at = self.now
            fight.transport={event="dig_block_ret",kind="mining_target",sent_at=self.now}
            local ok = mining:SolveEvent(event_id)
            self:hide_panel("event_panel")
            if fight.chest then
                self:hide_panel("confirm_msgbox")
            end
            if ok then
                self.status = "战斗准备中"
                self.detail = fight.label
                self:log("进入战斗：" .. fight.label)
            else
                self.fight = nil
                self:blacklist(fight.x, fight.y, 2, "无法开始战斗，等待背包、奇遇或资源就绪")
                self.status = "无法开始战斗"
            end
            return true
        end
        local sent=transport and transport._sent
        local query_at=sent and transport.sent_at or fight.at
        -- Reads can be retried; resource-consuming writes still await settlement.
        if not (transport and transport.queued) and self.now-query_at>(fight.query_backoff or 0.75) then
            if self:request_channel_available() then
                fight.query_backoff=math.min(3,(fight.query_backoff or 0.75)*2)
                fight.at=self.now
                fight.transport={event="query_golem_info_ret",kind="mining_target",sent_at=self.now}
                network:Send({query_golem_info={x=fight.x,y=fight.y}})
            end
            self.status = "重新读取"..fight.label.."，暂缓普通开挖"
            self.detail = "只重试目标查询，不重复挑战"
            return true
        end
        self.status = "查询" .. fight.label
        return true
    end

    -- solve / chest 阶段：等待战斗播放或开箱结算
    if self.inBattle then
        self.status = "战斗播放中"
        self.detail = fight.label
        return true
    end
    if mining.__mw_pending_monster then
        self.status = "战斗结算中"
        return true
    end
    if fight.rejection then
        self:blacklist(fight.x,fight.y,2,"挑战暂未接受："..fight.rejection)
        self.status="挑战暂未接受："..fight.rejection;self.fight=nil;self.last_action_at=self.now
        return true
    end
    local current = mining:GetBlockType(fight.x, fight.y)
    local finished = current == EMPTY
    if fight.boss and current == BT.rock then
        finished = true
    end

    if current==nil or (not finished and not fight.settlement_received) then
        self.status="等待"..fight.label.."实际结算";self.detail="不重复挑战或开箱"
        if fight.solved_at and self.now-fight.solved_at>6 and self.now>=(fight.next_read or 0) then
            fight.next_read=self.now+3
            network:Send({query_mining_info={}})
            pcall(function()mining:QueryBlockInfo()end)
        end
        return true
    end

    if finished then
        if fight.chest then
            self.stats.chests = self.stats.chests + 1
            self.status = "宝箱已开启"
        else
            self.stats.wins = self.stats.wins + 1
            if fight.boss then
                self.stats.bosses = self.stats.bosses + 1
            else
                self.stats.golems = self.stats.golems + 1
            end
            self.status = "战斗胜利：" .. fight.label
        end
        self:log("战斗胜利：" .. fight.label)
        self.failed[tostring(fight.x) .. ":" .. tostring(fight.y)] = nil
    else
        self.stats.losses = self.stats.losses + 1
        local cooldown = self.cfg.lossCooldown
        if fight.golem or (not fight.boss and not fight.chest) then
            cooldown = tonumber(self.cfg.nightmareCooldown) or 120
        end
        self:blacklist(fight.x, fight.y, cooldown, "战斗失败：" .. fight.label)
        self.status = "战斗失败：" .. fight.label
        self.detail = "该格已冷却 " .. tostring(math.floor(cooldown / 60)) .. " 分钟，稍后自动再战"
    end

    -- 挖矿战报（1.16）：战胜/战败/开箱各记一条，并附上战斗奖励
    if type(self.mining_stats) == "table" then
        self.mining_stats.fight_until = self.now + 6
        if self.mining_stats.active then
            local report_text
            if finished and fight.chest then
                report_text = "开启宝箱"
            elseif finished then
                report_text = "战胜 " .. tostring(fight.label)
            else
                report_text = "战败 " .. tostring(fight.label)
            end
            local rewards = tostring(self.mining_stats.last_rewards or "")
            self.mining_stats.last_rewards = ""
            self:mining_stats_report(report_text, rewards)
        end
    end

    self.fight = nil
    self.close_reward_until = self.now + 6
    self.last_action_at = self.now
    return true
end

function A:try_collect()
    local position = mining.cur_position
    if not position then
        return false
    end
    local block_type = mining:GetBlockType(position.x, position.y)
    if not block_type or block_type == EMPTY then
        return false
    end
    local cfg = block_cfg(block_type)
    if not cfg or (cfg.collect_type or 0) ~= CT.resource then
        return false
    end
    if (mining.dig_endtime or 0) > time_logic:Now() then
        return false
    end
    if not self:can_act(1.1) then
        return false
    end
    mining:DigOrCollectBlock(position.x, position.y)
    self:mark_action()
    self.stats.collects = self.stats.collects + 1
    self.status = "收取资源"
    self.detail = "(" .. tostring(position.x) .. "," .. tostring(position.y) .. ")"
    return true
end

function A:try_cave_boss()
    if not self.cfg.autoCaveBoss then
        return false
    end
    if self.inBattle or self.fight then
        return false
    end
    if self.now < (self.next_cave_boss or 0) then
        return false
    end
    if mining.cave_boss_lv == nil then
        if self.now >= (self.next_cave_query or 0) then
            self.next_cave_query = self.now + 30
            pcall(function()
                mining:RefreshRandomEvents()
            end)
        end
        return false
    end
    local level = tonumber(mining.cave_boss_lv) or 0
    local bp = tonumber(mining.cave_boss_bp) or 0
    local game_now = time_logic:Now()
    local end_time = tonumber(mining.cave_boss_end_time) or 0
    if level <= 0 or bp <= 0 then
        return false
    end
    if end_time > 0 and end_time <= game_now then
        return false
    end
    local medal_type = constants.RESOURCE_TYPE and constants.RESOURCE_TYPE.demon_medal
    local medal_cost = constants.CAVE_BOSS_CHALLANGE_SUB or 2
    if medal_type then
        local ok, medal_count = pcall(function()
            return resource_logic:GetResourceNum(medal_type)
        end)
        if not ok or type(medal_count) ~= "number" then
            return false
        end
        if medal_count < medal_cost then
            self.next_cave_boss = self.now + 60
            self.status = "恶魔勋章不足"
            self.detail = "挑战副本BOSS需要 " .. tostring(medal_cost) .. " 枚"
            return false
        end
    end
    local ok_bag, bag_space = pcall(function()
        return bag_logic:GetSpaceCount()
    end)
    if not ok_bag or type(bag_space) ~= "number" then
        return false
    end
    if bag_space < (constants.EVENT_MAX_BAG_SPACE_COUNT or 1) then
        self.next_cave_boss = self.now + 60
        self.status = "背包空间不足"
        self.detail = "请先清理背包再挑战副本BOSS"
        return false
    end
    if not self:can_act(2) then
        return false
    end
    self.next_cave_boss = self.now + (tonumber(self.cfg.caveBossInterval) or 120)
    local ok, err = pcall(function()
        mining:ChallengeCaveBoss()
    end)
    if ok then
        self:mark_action()
        self.stats.caveBoss = self.stats.caveBoss + 1
        self.status = "挑战矿区副本BOSS"
        self.detail = "等级 " .. tostring(level)
        self:log("挑战矿区副本BOSS，等级 " .. tostring(level))
        self.close_reward_until = self.now + 8
        self.pause_until = self.now + 6
        return true
    end
    self:log("副本BOSS挑战失败：" .. tostring(err))
    return false
end

function A:try_refuse_event()
    local event = mining.pending_random_event
    if not mining:IsPendingRandomEvent(event) then
        return false
    end

    -- Only events that consume both time and pickaxe count are refused.
    -- Occupancy events have pick_cost > 0; resource and treasure events are
    -- free of pickaxe cost and are accepted normally.
    local pick_cost = tonumber(event.pick_cost) or 0
    local costly = event.event_type == RANDOM_TYPE.occupancy or pick_cost > 0

    if not costly then
        if not self:can_act(1.2) then
            return true
        end
        mining:AcceptRandomEvent("accept")
        self:mark_action()
        if event.event_type == RANDOM_TYPE.treasure_hunt then
            self.status = "接受藏宝箱事件"
            self.detail = "接受后将自动寻路并开启"
            self:log("接受藏宝箱随机事件，准备前往开启")
        else
            self.status = "接受资源随机事件"
            self.detail = "该类事件不消耗挖矿次数"
            self:log("接受资源随机事件，不消耗挖矿次数")
        end
        return true
    end

    if not self.cfg.refuseEvents then
        self.status = "请手动处理耗时事件"
        self.detail = "该事件需要时间和挖矿次数"
        return true
    end
    if not self:can_act(1.2) then
        return true
    end
    mining:AcceptRandomEvent("refuse")
    self:mark_action()
    self.status = "拒绝耗时随机事件"
    self.detail = "避免消耗时间和挖矿次数"
    self:log("拒绝需要时间和挖矿次数的事件")
    return true
end

function A:try_close_reward()
    if self.now < (self.close_reward_until or 0) then
        self:hide_panel("reward_panel")
    end
end

function A:try_enter_mining()
    if not self.cfg.autoEnter then
        return false
    end
    if self.now - (self.last_enter_at or -999) < 4 then
        return false
    end
    if not mining.cur_position then
        return false
    end
    self.last_enter_at = self.now
    pcall(function()
        mining:QueryBlockInfo()
    end)
    self.status = "正在进入矿区"
    self.detail = "加载地图中…"
    return true
end

function A:read_context()
    local scene_name = scene_manager:GetCurrentSceneName()
    if scene_name ~= "world" then
        return nil, "请先登录并进入游戏"
    end
    local scene = cc.Director:getInstance():getRunningScene()
    if not scene or type(scene.GetCurSubScene) ~= "function" then
        return nil, "等待游戏场景加载"
    end
    local sub_scene = scene:GetCurSubScene()
    local sub_name = sub_scene and sub_scene:GetName() or ""
    return {
        scene = scene,
        sub_scene = sub_scene,
        sub_name = sub_name,
    }, nil
end

function A:mining_ready()
    return type(mining.block_list) == "table"
        and mining.cur_position ~= nil
        and mining.cur_position.x ~= nil
end

function A:check_account()
    local uid = user_logic and user_logic.user_id or nil
    local previous_account = self._account
    local account_changed = uid ~= nil and previous_account ~= nil and uid ~= previous_account
    if uid ~= previous_account then
        self._account = uid
        self.fight = nil
        self.pending_dig = nil
        self._mine_sync=nil;self.mine_refill=nil;self._mine_event=nil;self._mine_event_done=nil
        if account_changed then self._mine_blast=nil end
        self.pause_until = nil
        self.expand_origin = nil
        -- A transient nil uid during a battle or map refresh must not erase
        -- the tunnel spacing state, otherwise the next tunnel can be too close.
        if account_changed then
            self.layer_target = nil
            self.layer_dir = 1
            self.last_tunnel_y = nil
            self.last_layer_log = nil
        end
        self.failed = {}
        for key in pairs(self.stats) do
            self.stats[key] = 0
        end
        if uid then
            self:log("账号已就绪：" .. tostring(uid))
        end
    end

    local generation = tonumber(mining.stream_generation)
    if generation and generation ~= self._stream_generation then
        self._stream_generation = generation
        self.fight = nil
        self.pending_dig = nil
        self._mine_sync=nil
        self.pause_until = nil
        self.expand_origin = nil
        -- Keep layer_target/last_tunnel_y across stream snapshots. Resetting
        -- them here used to open a new tunnel too close to the previous one.
    end
end

function A:step()
    local context, context_error = self:read_context()
    if not context then
        self.context = context_error or "等待游戏"
        self.status = context_error or "等待游戏"
        return
    end
    self.context = context.sub_name or ""
    self:check_account()

    if not self:mining_ready() then
        if context.sub_name == "main_sub_scene" then
            if not self.cfg.autoEnter then
                self.status = "请先进入矿区"
                self.detail = "自动进矿区已关闭"
            else
                self:try_enter_mining()
            end
        else
            self.status = "等待矿区数据"
            self.detail = self.context
        end
        return
    end

    if next(mining.block_list) == nil and context.sub_name == "mining_district_sub_scene" then
        self.status = "矿区地图加载中"
        self.detail = "等待地块数据"
        return
    end

    if self.bag_boost and self.bag_base_count
        and (tonumber(mining.dig_count) or 0) > self.bag_base_count then
        self.bag_boost = nil
        self.bag_boost_at = nil
        self.bag_base_count = nil
    end

    if self.now < (self.contract_capture_hold or 0) then return end
    self:try_close_reward()
    self:prune_failed()

    if self.inBattle then
        return
    end
    self.battle_seen_at = nil
    self.battle_skip_attempts = 0

    if self:mine_blast_step() then return end

    if not self:pending_dig_resolved() then
        self.status=self._mine_sync and "正在同步矿区，核对挖掘状态" or "等待挖掘响应"
        return
    end

    if self:try_refuse_event() then
        return
    end

    if self:try_use_mining_bag() then return end

    if self.fight then
        self:update_fight()
        return
    end

    if self.pause_until and self.now < self.pause_until then
        self.status = "等待战斗开始"
        self.detail = "副本BOSS挑战请求已发送"
        return
    end

    if not self:pending_dig_resolved() then
        self.status="等待挖掘响应"
        return
    end

    local remain, total = mining:GetDigTime()
    if remain and remain > 0.05 then
        if self.cfg.useTntOnLong and total and total > 60
            and (mining:GetDigTime() > 30) then
            local cfg = block_cfg(mining.cur_block_type)
            if cfg and (cfg.tnt_count or 0) > 0 and self:can_act(3) then
                mining:UseTNT()
                self:mark_action()
                self.status = "使用雷管跳过长矿"
                return
            end
        end
        self.status = "挖矿中 " .. tostring(math.ceil(remain)) .. " 秒"
        if total and total > 60 then
            self.detail = "长矿：可手动用雷管，或在面板开启“长矿用雷管”"
        else
            self.detail = total and ("总时长 " .. tostring(math.ceil(total)) .. " 秒") or ""
        end
        return
    end

    if context.sub_name ~= "mining_district_sub_scene" then
        if context.sub_name == "main_sub_scene" or context.sub_name == "" then
            self:try_enter_mining()
        else
            self.status = "请先进入矿区"
            self.detail = "当前界面：" .. tostring(context.sub_name)
        end
        return
    end

    if self:try_collect() then
        return
    end

    local targets = self:scan_targets()
    self:note_golems(targets)
    local best,planning = self:plan_mining_target_budgeted(targets)
    if planning then self.status="规划目标通路";self.detail="核对目标附近的可通行地块";return end

    self.mining_route_target=best
    if best then self:set_mining_route(best,best.path and best.path[1],best.label)
    else self:set_mining_route(nil,nil) end

    if best then
        if best.needs_snapshot then
            self.status="同步可见魔像地形，暂缓普通开挖"
            if self.now>=(self._golem_region_at or 0) then
                self._golem_region_at=self.now+1
                pcall(function()
                    if type(mining.RequestMiningRegion)=="function" then mining:RequestMiningRegion({x=best.x,y=best.y},6,6,true)
                    else mining:QueryBlockInfo() end
                end)
            end
            return
        end
        if best.defer_until and best.defer_until>self.now then
            self.status="等待可见魔像重试，暂缓普通开挖"
            local info=self.failed_info and self.failed_info[tostring(best.x)..":"..tostring(best.y)]
            self.detail=(info and info.reason or "目标冷却").." · "..tostring(math.ceil(best.defer_until-self.now)).."秒"
            return
        end
        if best.path and #best.path == 0 then
            if self:start_target(best) then
                return
            end
            self.status = "等待挑战" .. best.label
            return
        elseif best.path and #best.path > 0 then
            local step = best.path[1]
            local tnt_id=constants.RESOURCE_TYPE and constants.RESOURCE_TYPE.tnt
            if not step.free_chest and best.route_tnt>0 and (not tnt_id or (tonumber(resource_logic:GetResourceNum(tnt_id)) or 0)<best.route_tnt) then
                self.status="目标通路需要雷管";self.detail="雷管不足，保留矿镐，等待补充后打通";return
            end
            if step.unknown then
                self:load_target_route(best,step)
            elseif step.free_chest then
                if self:is_failed(step.x,step.y) then self.status="等待免费开口箱就绪，保留矿镐";return end
                self:start_target({x=step.x,y=step.y,block_type=mining:GetBlockType(step.x,step.y),chest=true,
                    label="开口的箱子",free_passage=true})
            elseif step.blast then self:blast_target_path(step,best)
            else self:dig(step.x,step.y,"前往"..best.label) end
            return
        end
        -- No real path exists in the loaded map. Expanding around this target
        -- cannot bypass an enclosed TNT barrier and used to waste many picks.
        self:load_target_route(best,nil)
        return
    end

    if self:try_cave_boss() then return end

    -- Descending is the last choice, including the automatic shallow start.
    if self.cfg.enabled and self.cfg.autoMine and not (self.daily_state and self.daily_state.running)
        and not self:automatic_mining_depth_ready() then
        local down=self:choose_descent_frontier(self:frontier())
        local selected=self:automatic_mining_start_depth()
        if down then self:dig(down.x,down.y,"自动下潜至第"..selected.."层")
        else
            self.status="自动下潜：等待可挖掘的通路"
            self.detail="当前深度 "..tostring(mining.cur_position.y).."；达到第"..selected.."层后继续自动挖掘"
        end
        return
    end

    if self.cfg.enabled and not self.cfg.horizontalMining and self.last_action_at
        and self:automatic_mining_depth_ready()
        and self.now - self.last_action_at > 12
        and (tonumber(mining.dig_endtime) or 0) <= time_logic:Now()
        and self.now - (self.last_stall_at or 0) > 12 then
        local y = tonumber(mining.cur_position and mining.cur_position.y) or 0
        local max_depth = tonumber(self.cfg.layerMaxDepth) or 60
        local step = tonumber(self.cfg.layerStep) or 10
        self.last_stall_at = self.now
        if y < max_depth then
            self.layer_target = math.min(max_depth, y + step)
            self.blocked_until = self.now + 2
            self.status = "疑似卡住，尝试向下换层"
            self.detail = "下一层 " .. tostring(self.layer_target)
            self:log("检测到长时间无动作，尝试下潜到深度 " .. tostring(self.layer_target))
            pcall(function()
                mining:QueryBlockInfo()
            end)
            return
        end
        self.status = "已达最大挖掘深度"
        self.detail = "等待可横向挖掘的方块"
        return
    end

    local virtual_target = self:seek_virtual_target()
    if virtual_target and self:mine_frontier(virtual_target) then
        return
    end

    if self.cfg.autoMine then
        if self:mine_frontier(nil) then
            return
        end
    end

    if not self.cfg.autoMine and #targets == 0 then
        self.status = "待机"
        self.detail = "自动挖矿与自动战斗均已关闭"
    end
end

function A:start_daily()
    self.daily_state = {}
    return true
end

-- One safe daily step. Only free actions are used here: check-in and
-- liveness/activity rewards. Alchemy and prayer are intentionally skipped
-- because they consume blood diamonds.
function A:daily_step()
    local daily = require("logic.daily")
    if not self.daily_state then
        self.daily_state = {}
    end
    if scene_manager:GetCurrentSceneName() ~= "world" then
        return { wait = true, message = "等待进入游戏" }
    end
    self:hide_panel("reward_panel")

    if not daily.already_request_daily then
        daily:RequestDaily()
        return { wait = true, message = "查询日常数据" }
    end

    local state = self.daily_state
    if not state.checkin_checked then
        state.checkin_checked = true
        if not daily:AlreadyCheckin() then
            daily:TakeCheckIn()
            return { action = "签到", message = "已领取今日签到" }
        end
    end

    if daily.liveness_list == nil then
        daily:GetActivityList()
        return { wait = true, message = "查询活跃度" }
    end

    for index = 1, #daily.liveness_list do
        local item = daily.liveness_list[index]
        if item and not item.is_reward then
            local complete = daily:CheckActivityComplet(item.liveness_id, item.completion_count)
            if complete then
                daily:GetActivityReward(item.liveness_id)
                state.activity_claimed = (state.activity_claimed or 0) + 1
                return {
                    action = "活跃度奖励",
                    message = "领取活跃度奖励 " .. tostring(item.liveness_id),
                }
            end
        end
    end

    local rewards = daily.activity_receive_list or {}
    if #rewards > 0 then
        local reward_index = rewards[1]
        daily:GetActivityAllReward(reward_index)
        state.milestone_claimed = (state.milestone_claimed or 0) + 1
        return {
            action = "活跃度宝箱",
            message = "领取活跃度宝箱 " .. tostring(reward_index),
        }
    end

    state.done = true
    return {
        done = true,
        message = "日常处理完成：签到/活跃度奖励已处理，未使用血钻",
        checkin = state.checkin_checked and "已检查" or "无需",
        activity = state.activity_claimed or 0,
        milestone = state.milestone_claimed or 0,
    }
end

function A:start_arena(target)
    self.arena_state = {
        target = math.max(1, tonumber(target) or 5),
        count = 0,
        last_challenge_at = 0,
    }
    return true
end

function A:arena_step()
    if scene_manager:GetCurrentSceneName() ~= "world" then
        return { wait = true, message = "等待进入游戏" }
    end
    local arena = require("logic.arena")
    local state = self.arena_state
    if not state then
        state = { target = 1, count = 0, last_challenge_at = 0 }
        self.arena_state = state
    end
    if not arena.is_first_query or #(arena.rival_list or {}) == 0 then
        arena:Query()
        return { wait = true, message = "查询竞技场对手" }
    end
    if state.count >= state.target then
        return { done = true, message = "竞技场已完成 " .. tostring(state.count) .. " 次挑战" }
    end
    if (tonumber(arena.challenge_num) or 0) <= 0 then
        return { done = true, message = "竞技场挑战次数不足" }
    end
    if self.now - (state.last_challenge_at or 0) < 3 then
        return { wait = true, message = "等待竞技场战斗结算" }
    end

    local best_pos = nil
    local best_bp = nil
    for pos = 1, #arena.rival_list do
        local rival = arena.rival_list[pos]
        if rival and not rival.state then
            local bp = tonumber(rival.battle_point or rival.bp or rival.combat_power) or 0
            if not best_bp or bp < best_bp then
                best_bp = bp
                best_pos = pos
            end
        end
    end

    if not best_pos then
        if time_logic:Now() >= (tonumber(arena.refresh_time) or 0) then
            arena:Query()
            state.last_challenge_at = self.now
            return { action = "刷新竞技场", message = "免费刷新竞技场对手" }
        end
        if self.cfg.arenaAllowBloodRefresh then
            arena:RefreshRival()
            state.last_challenge_at = self.now
            return { action = "刷新竞技场", message = "使用血钻刷新对手" }
        end
        return { done = true, message = "竞技场对手已打完；未开启血钻刷新" }
    end

    arena:ChallengeRival(best_pos)
    state.count = state.count + 1
    state.last_challenge_at = self.now
    return {
        action = "竞技场挑战",
        message = "挑战竞技场第 " .. tostring(best_pos) .. " 位（第 " .. tostring(state.count) .. " 次）",
    }
end

function A:start_void()
    self.void_state = {
        team_for = nil,
        last_team_at = 0,
        last_fight_at = 0,
        attempts = 0,
    }
    return true
end

function A:void_step()
    if scene_manager:GetCurrentSceneName() ~= "world" then
        return { wait = true, message = "等待进入游戏" }
    end
    local troop = require("logic.troop")
    local state = self.void_state
    if not state then
        state = { team_for = nil, last_team_at = 0, last_fight_at = 0, attempts = 0 }
        self.void_state = state
    end
    if not troop.vanity_mercenarys_list then
        troop:QueryVantiyToopInfo()
        return { wait = true, message = "查询虚空佣兵" }
    end
    if not troop.vanity_maze_state_list then
        troop:GetVanityMazeList()
        return { wait = true, message = "查询虚空关卡" }
    end

    local max_maze = tonumber(constants.VANITY_MAX_MAZE_ID) or 6
    local states = troop.vanity_maze_state_list
    local maze_state = constants.VANITY_MAZE_STATE

    -- Claim a cleared stage first; this unlocks the next stage and grants mercenaries.
    for maze = 1, max_maze do
        if states[maze] == maze_state.challenge_success then
            troop:GetVanityMercenaryByMazeId(maze)
            state.team_for = nil
            state.attempts = 0
            return { action = "领取虚空奖励", message = "领取第 " .. tostring(maze) .. " 关奖励" }
        end
    end

    if #troop.vanity_mercenarys_list == 0 then
        return { done = true, message = "没有可上阵的虚空佣兵" }
    end

    local maze = nil
    for index = 1, max_maze do
        if states[index] == maze_state.challenge_able then
            maze = index
            break
        end
    end
    if not maze then
        return { done = true, message = "虚空裂隙当前没有可挑战关卡" }
    end
    if state.attempts >= 3 then
        return { done = true, message = "虚空裂隙连续挑战未成功，请检查阵容" }
    end

    if state.team_for ~= maze then
        local mercs = {}
        for index = 1, #troop.vanity_mercenarys_list do
            local entry = troop.vanity_mercenarys_list[index]
            if entry and (tonumber(entry.battle_num) or 0) > 0 then
                local info = troop:GetVanityMercenarys(entry.instance_id) or entry
                local bp = tonumber(info.battle_point or entry.battle_point or 0) or 0
                mercs[#mercs + 1] = { instance_id = entry.instance_id, bp = bp }
            end
        end
        table.sort(mercs, function(a, b) return a.bp > b.bp end)
        if #mercs == 0 then
            return { done = true, message = "没有可上阵的虚空佣兵" }
        end
        local formation = {}
        for index = 1, #troop.vanity_troop do
            formation[index] = mercs[index] and mercs[index].instance_id or 0
        end
        local network = require("util.network")
        network:Send({ vanity_sync_troop = { troop = formation } })
        state.team_for = maze
        state.last_team_at = self.now
        return {
            action = "虚空上阵",
            message = "按攻略优先上阵 " .. tostring(math.min(#mercs, #troop.vanity_troop)) .. " 名高战力佣兵",
        }
    end

    if self.now - (state.last_team_at or 0) < 3 then
        return { wait = true, message = "等待虚空阵容同步" }
    end
    if self.now - (state.last_fight_at or 0) < 4 then
        return { wait = true, message = "等待虚空战斗结算" }
    end

    state.attempts = state.attempts + 1
    state.last_fight_at = self.now
    troop:FightingByMazeId(maze)
    return {
        action = "虚空挑战",
        message = "挑战虚空裂隙第 " .. tostring(maze) .. " 关 BOSS（第 " .. tostring(state.attempts) .. " 次）",
    }
end

function A:try_skip_battle()
    if not self.inBattle then
        self.battle_seen_at = nil
        self.battle_skip_attempts = 0
        return false
    end
    if not self.cfg.autoSkipBattle then
        self.status = "战斗播放中"
        return true
    end
    if not self.battle_seen_at then
        self.battle_seen_at = self.now
        self.battle_skip_attempts = 0
        self:log("进入战斗，准备自动跳过")
    end
    local delay = 0
    if self.now < (self.next_battle_skip_at or 0) then return true end
    if self.now - self.battle_seen_at >= delay then
        self.battle_skip_attempts = (self.battle_skip_attempts or 0) + 1
        self.next_battle_skip_at = self.now + 0.05
        pcall(function()
            graphic:DispatchEvent("hide_battle_room")
        end)
        self.status = "自动跳过战斗"
        self.detail = "第 " .. tostring(self.battle_skip_attempts) .. " 次跳过尝试"
        if self.battle_skip_attempts == 1 then
            self:log("已请求跳过战斗动画")
        end
        if self.battle_skip_attempts >= 3 then
            self.battle_seen_at = self.now
        end
    else
        self.status = "准备跳过战斗"
        self.detail = "战斗动画即将跳过"
    end
    return true
end

-- ------------------------------------------------------------
-- 挖矿统计（1.16）
-- 统计「本次自动挖矿」从开始到结束的矿镐消耗与全部资源产出：
--   * 矿镐：取 mining.dig_count 的下降量（含巨魔额外消耗，排除自然恢复与矿工包补充）；
--   * 资源：挂在 resource_logic.UpdateResource 上，只统计自动挖矿运行期间的获得。
-- 计数器在每次开始自动挖矿时归零（本次会话）；C# 侧按差值累计到「今日」并落盘，
-- 页面重载后从 0 重新开始也不会重复计数（差值变负即视为重新开始）。
-- ------------------------------------------------------------
-- 资源分类：与界面上的「矿石 / 宝石 / 合成材料 / 金币 / 血钻 / 契约石 / 经验 / 钥匙」一致。
local STATS_CAT_ORE = { [7] = true, [8] = true, [9] = true, [10] = true, [11] = true, [12] = true, [13] = true }
local STATS_CAT_GEM = { [14] = true, [15] = true, [16] = true, [17] = true }
function A:mining_stats_category(id)
    local key = tonumber(id) or 0
    if key == 1 then return "gold" end
    if key == 2 then return "blood" end
    if key == 35 then return "contract" end
    if key == 36 then return "exp" end
    if STATS_CAT_ORE[key] then return "ore" end
    if STATS_CAT_GEM[key] then return "gem" end
    if key >= 38 and key <= 40 then return "key" end
    if key >= 60 and key <= 89 then return "material" end
    return "other"
end

function A:mining_stats_day()
    local seconds
    local ok, value = pcall(function() return tonumber(time_logic:Now()) end)
    seconds = ok and value or nil
    if not seconds then
        ok, value = pcall(os.time)
        seconds = ok and value or 0
    end
    local offset = 28800
    if type(time_logic) == "table" then offset = tonumber(time_logic.time_zone_offset) or offset end
    return math.floor((seconds + offset) / 86400)
end

function A:mining_stats_add_resource(id, num)
    local stats = self.mining_stats
    if type(stats) ~= "table" then return end
    local key = tonumber(id)
    if not key then return end
    stats.res[tostring(key)] = (tonumber(stats.res[tostring(key)]) or 0) + (tonumber(num) or 0)
end

function A:mining_stats_add_extra(key, amount)
    local stats = self.mining_stats
    if type(stats) ~= "table" or not key then return end
    stats.extra = stats.extra or {}
    stats.extra[key] = (tonumber(stats.extra[key]) or 0) + (tonumber(amount) or 0)
end

-- 消耗量：金币净增与「钥匙净消耗」都靠它（UpdateResource 的负数）。
function A:mining_stats_add_spent(id, num)
    local stats = self.mining_stats
    if type(stats) ~= "table" then return end
    local key = tonumber(id)
    if not key then return end
    stats.spent = stats.spent or {}
    stats.spent[tostring(key)] = (tonumber(stats.spent[tostring(key)]) or 0) + (tonumber(num) or 0)
end

-- 挖矿战报：与游戏「托管挖矿」的战报一致，每场战斗一条，最新在前。
function A:mining_stats_report(text, rewards)
    local stats = self.mining_stats
    if type(stats) ~= "table" then return end
    stats.report = stats.report or {}
    local ok, stamp = pcall(os.date, "%H:%M:%S")
    table.insert(stats.report, 1, {
        t = ok and stamp or "",
        text = tostring(text),
        rewards = tostring(rewards or ""),
        at = tonumber(self.now) or 0,
    })
    if #stats.report > 200 then table.remove(stats.report) end
end

-- 战斗奖励快照：只在战斗期间/战斗刚结束的窗口内捕获，避免混入挖矿采集。
function A:mining_stats_capture_rewards(message)
    local stats = self.mining_stats
    if type(stats) ~= "table" or not stats.active then return end
    local now = tonumber(self.now) or 0
    if self.fight == nil and now >= (tonumber(stats.fight_until) or 0) then return end
    local list = type(message) == "table" and message.reward_info_list
    if type(list) ~= "table" then return end
    local parts = {}
    for _, info in ipairs(list) do
        if type(info) == "table" then
            local kind = tonumber(info.id) or tonumber(info.reward_type) or 0
            local p1 = tonumber(info.param1) or 0
            local p2 = tonumber(info.param2) or 0
            if kind == 2 and p1 > 0 and p2 > 0 then
                local conf = (config_manager.resource_config or {})[p1]
                parts[#parts + 1] = ((conf and tostring(conf.name)) or ("资源" .. p1)) .. "×" .. p2
            elseif kind == 6 and p1 > 0 then
                parts[#parts + 1] = "主角战力×" .. p1
            elseif kind == 8 and p1 > 0 then
                local conf = (config_manager.mercenary_config or {})[p1]
                parts[#parts + 1] = ((conf and tostring(conf.name)) or ("佣兵" .. p1)) .. "×" .. math.max(1, p2)
            elseif kind == 1 and p1 > 0 and p2 > 0 then
                local conf = (config_manager.item_config or {})[p1]
                parts[#parts + 1] = ((conf and tostring(conf.name)) or ("道具" .. p1)) .. "×" .. p2
            elseif kind == 3 and math.max(p1, p2) > 0 then
                parts[#parts + 1] = "矿镐×" .. math.max(p1, p2)
            end
        end
    end
    if #parts > 0 then
        stats.last_rewards = table.concat(parts, " ")
    end
end

-- 力量点数：挖矿每挖开一格由成就统计累加，是挖矿本身的产出。
function A:install_mining_stats_strength_hook()
    local ok, achievement = pcall(require, "logic.achievement")
    if not ok or type(achievement) ~= "table" or type(achievement.UpdateStatisticValue) ~= "function" then
        return false
    end
    if achievement.UpdateStatisticValue == achievement._mwa_stats then return true end
    local original = achievement.UpdateStatisticValue
    local strength_type = constants.ACHIEVEMENT_TYPE and constants.ACHIEVEMENT_TYPE.strength_pt
    local wrapped = function(module, stat_type, value)
        local result = original(module, stat_type, value)
        local active = __MWA
        if active and strength_type and stat_type == strength_type
            and type(active.mining_stats) == "table" and active.mining_stats.active then
            pcall(function() active:mining_stats_add_extra("strength", tonumber(value) or 0) end)
        end
        return result
    end
    achievement.UpdateStatisticValue = wrapped
    achievement._mwa_stats = wrapped
    return true
end

-- 战斗力（以及道具/佣兵）走 reward_info_list：奖励类型 6 = 提升战斗力。
-- 资源已由 UpdateResource 统计，这里只补非资源类型，避免重复计数。
local REWARD_TYPE_BP = 6
function A:mining_stats_scan_rewards(message)
    if type(message) ~= "table" then return end
    local list = message.reward_info_list
    if type(list) ~= "table" then return end
    for _, info in ipairs(list) do
        if type(info) == "table" then
            local kind = tonumber(info.id or info.reward_type) or 0
            if kind == REWARD_TYPE_BP then
                local amount = tonumber(info.param1 or info.param2) or 0
                if amount > 0 then self:mining_stats_add_extra("bp", amount) end
            end
        end
    end
end

function A:install_mining_stats_reward_hook()
    local listener = type(network) == "table" and network.event_listener or nil
    if type(listener) ~= "table" then return false end
    if listener.reward_info_list == self._stats_reward_hook then return true end
    local original = listener.reward_info_list
    if type(original) ~= "function" then return false end
    local wrapped = function(message, ...)
        original(message, ...)
        local active = __MWA
        if active then pcall(function() active:cart_reward_receipt(message) end) end
        if active and type(active.mining_stats) == "table" and active.mining_stats.active then
            pcall(function() active:mining_stats_scan_rewards(message) end)
            pcall(function() active:mining_stats_capture_rewards(message) end)
        end
    end
    listener.reward_info_list = wrapped
    self._stats_reward_hook = wrapped
    return true
end

-- 幂等安装：resource:UpdateResource 是所有资源增减的唯一入口。
function A:install_mining_stats_hook()
    if type(resource_logic) ~= "table" or type(resource_logic.UpdateResource) ~= "function" then
        return false
    end
    if resource_logic.UpdateResource == resource_logic._mwa_stats then return true end
    local original = resource_logic.UpdateResource
    local wrapped = function(module, resource_type, num)
        local result = original(module, resource_type, num)
        local active = __MWA
        if active and type(active.mining_stats) == "table" and active.mining_stats.active then
            local amount = tonumber(num) or 0
            if resource_type then
                if amount > 0 then
                    pcall(function() active:mining_stats_add_resource(resource_type, amount) end)
                    if tonumber(resource_type)==35 and active.cfg.enabled and active.cfg.autoMine then
                        active:queue_contract_capture(amount)
                    end
                elseif amount < 0 then
                    pcall(function() active:mining_stats_add_spent(resource_type, -amount) end)
                end
            end
        end
        return result
    end
    resource_logic.UpdateResource = wrapped
    resource_logic._mwa_stats = wrapped
    return true
end

function A:mining_stats_poll()
    local stats = self.mining_stats
    if type(stats) ~= "table" then
        stats = { picks = 0, res = {}, active = false, prev = 0 }
        self.mining_stats = stats
    end
    local on = self.cfg.enabled and self.cfg.autoMine
    local count = 0
    if type(mining) == "table" then count = tonumber(mining.dig_count) or 0 end
    if not on then
        stats.active = false
        stats.prev = count
        return
    end
    if not stats.active then
        -- 新的一次自动挖矿：本次计数归零，从当前矿镐数开始。
        stats.active = true
        stats.picks = 0
        stats.res = {}
        stats.spent = {}
        stats.extra = {}
        stats.report = {}
        stats.last_rewards = ""
        stats.prev = count
        stats.day = self:mining_stats_day()
        -- 会话号：C# 靠它判断「本次」已重新开始，避免把上一次的累计当成本次。
        stats.session = (tonumber(stats.session) or 0) + 1
        self:log("挖矿统计：开始记录本次矿镐消耗与资源产出")
        return
    end
    local prev = tonumber(stats.prev) or count
    if count < prev then
        stats.picks = (tonumber(stats.picks) or 0) + (prev - count)
    end
    stats.prev = count
    -- 迟到的战斗奖励：8 秒内归并到最新一条还没有奖励的战报
    if stats.last_rewards and stats.last_rewards ~= ""
        and type(stats.report) == "table" and stats.report[1] and stats.report[1].rewards == ""
        and (tonumber(self.now) or 0) - (tonumber(stats.report[1].at) or 0) < 8 then
        stats.report[1].rewards = stats.last_rewards
        stats.last_rewards = ""
    end
end

function A:mining_stats_snapshot()
    local stats = self.mining_stats or {}
    local list = {}
    for id, amount in pairs(stats.res or {}) do
        local rid = tonumber(id) or 0
        local conf = nil
        if type(config_manager) == "table" and type(config_manager.resource_config) == "table" then
            conf = config_manager.resource_config[rid]
        end
        list[#list + 1] = {
            id = rid,
            name = (conf and tostring(conf.name)) or ("资源" .. tostring(rid)),
            num = tonumber(amount) or 0,
            cat = self:mining_stats_category(rid),
        }
    end
    table.sort(list, function(a, b)
        if a.num ~= b.num then return a.num > b.num end
        return a.id < b.id
    end)
    local spent = {}
    for id, amount in pairs(stats.spent or {}) do
        local rid = tonumber(id)
        if rid and (tonumber(amount) or 0) > 0 then spent[tostring(rid)] = tonumber(amount) end
    end
    local report = {}
    for i, entry in ipairs(stats.report or {}) do
        if i > 60 then break end
        report[i] = { t = entry.t, text = entry.text, rewards = entry.rewards }
    end
    return {
        picks = tonumber(stats.picks) or 0,
        active = stats.active == true,
        day = self:mining_stats_day(),
        session = tonumber(stats.session) or 0,
        strength = tonumber((stats.extra or {}).strength) or 0,
        bp = tonumber((stats.extra or {}).bp) or 0,
        list = list,
        spent = spent,
        report = report,
    }
end

function A:tick(now, in_battle, light)
    self.now = tonumber(now) or 0
    self.inBattle = in_battle == true
    if self.now < (self._last_tick or -999) then
        self._last_tick = self.now
    end
    self._last_tick = self.now
    pcall(function() self:install_fast_graphics() end)
    if self.now>=(self._next_task_hook_check or 0) then
        self._next_task_hook_check=self.now+2
        pcall(function()self:install_task_hooks()end)
    end
    pcall(function() self:drain_fast_settlements() end)
    pcall(function() self:install_audit_hook() end)
    pcall(function() self:install_mining_stats_hook() end)
    pcall(function() self:install_mining_stats_strength_hook() end)
    pcall(function() self:install_mining_stats_reward_hook() end)
    pcall(function() self:mining_stats_poll() end)

    -- Battle skipping must work for arena, vanity and mining battles even
    -- when the mining loop itself is paused/stopped.
    if self:try_skip_battle() then
        if not light then return self:snapshot() end
        return
    end

    if self.fast_refill and self.fast_refill.owner=="mine" then
        local r=self:refill_step()
        if r and r.refill_done and r.error then self.cfg.enabled=false;self.status=r.error end
        if not light then return self:snapshot() end
        return
    end
    if not self.cfg.enabled then
        if self._route_overlay then self:update_mining_route_overlay(nil) end
        self.mining_route_preview=nil
        self.mining_route_target=nil
        self.status = "已暂停"
        self.detail = ""
        if not light then return self:snapshot() end
        return
    end

    local ok, err = xpcall(function()
        self:step()
    end, debug.traceback)
    pcall(function()self:follow_mining_camera()end)
    if not ok then
        self.stats.errors = self.stats.errors + 1
        self._errCount = (self._errCount or 0) + 1
        self.status = "运行错误"
        self.detail = tostring(err):sub(1, 200)
        if self.now - (self._last_error_log or -999) > 5 then
            self._last_error_log = self.now
            self:log("运行错误：" .. tostring(err):sub(1, 160))
        end
        if self._errCount >= 12 then
            self.cfg.enabled = false
            self.status = "已自动暂停：连续错误"
            self:log("连续错误过多，已自动暂停")
        end
    else
        self._errCount = 0
    end
    if not light then return self:snapshot() end
        return
end

-- 暴露给测试/诊断使用（不参与自动逻辑）
A._test = {
    find_path = find_path,
    can_dig = can_dig,
    dig_time = dig_time,
    block_cfg = block_cfg,
}

function A:queue_contract_capture(amount)
    self.contract_capture_seq=(self.contract_capture_seq or 0)+1
    self.contract_captures=self.contract_captures or {}
    local pos=mining.cur_position or {}
    self.contract_captures[#self.contract_captures+1]={id=self.contract_capture_seq,amount=amount,x=pos.x or 0,y=pos.y or 0}
    self.contract_capture_hold=(tonumber(self.now) or 0)+0.7
    self:log("获得契约石×"..tostring(amount).."，正在保存截图")
end
function A:pending_contract_captures() return self.contract_captures or {} end
function A:ack_contract_capture(id)
    for i=#(self.contract_captures or {}),1,-1 do
        if self.contract_captures[i].id==id then table.remove(self.contract_captures,i) end
    end
end

-- @TASKS@
return A


