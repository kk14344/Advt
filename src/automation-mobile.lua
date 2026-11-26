-- ============================================================
-- 冒险与挖矿 · 自动助手（手机轻量版核心）
-- 不包含自动挖矿与掉落统计：只保留一键日常 / 竞技场 / 虚空 /
-- 矿山采集与研究 / 工坊 / 公会首领 / 符文 / 宿命武器 / 大地之心 / 虚空兑换。
-- 仅调用游戏客户端已有的正常操作接口，不修改服务器数据。
-- ============================================================
if __MWA and __MWA.VERSION == "2.0.0" then
    return __MWA
end

local network = require "util.network"
local graphic = require "logic.graphic"
local config_manager = require "logic.config_manager"
local constants = require "util.constants"
local scene_manager = require "scene.scene_manager"
local time_logic = require "logic.time"

local function now_text()
    local ok, value = pcall(os.date, "%H:%M:%S")
    return ok and value or "--:--:--"
end

local A = {
    VERSION = "2.0.0",
    cfg = {
        enabled = false,
        mobileNoMining = true,
        autoSkipBattle = true,
        battleSkipDelay = 0.7,
        arenaAllowBloodRefresh = false,
        autoUseEarthHeart = true,
        runeUseFlashGold = true,
        workshopDepth = 150,
        workshopMinutes = 120,
        guildBossCount = 3,
        arenaRounds = 5,
        spendBudget = 360,
        spendBase = 0,
        spendDay = 0,
        spendAllowCaveBuy = true,
        reserveTnt = 0,
        mineResearch = 0,
    },
    stats = {
        wins = 0,
        losses = 0,
        errors = 0,
        mineClaims = 0,
        mineStarts = 0,
        researchUpgrades = 0,
    },
    logs = {},
    failed = {},
    audit_seq = 0,
    audit_buf = {},
    spend_session = 0,
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
    self.cfg[key] = value
    if key == "enabled" then
        self.status = value and "就绪" or "已暂停"
        self.detail = ""
        self._errCount = 0
        self:log(value and "自动助手已启动" or "自动助手已暂停")
    end
    return true
end

function A:resetStats()
    for key in pairs(self.stats) do
        self.stats[key] = 0
    end
end

function A:hide_panel(name)
    local ok, scene = pcall(function()
        return cc.Director:getInstance():getRunningScene()
    end)
    if not ok or not scene or not scene.sub_panels then return end
    local panel = scene.sub_panels[name]
    if not panel or not panel:IsVisible() then return end
    pcall(function()
        graphic:DispatchEvent("hide_world_sub_panel", name)
    end)
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
        self:log("进入战斗，自动跳过动画")
    end
    local delay = tonumber(self.cfg.battleSkipDelay) or 0.7
    if self.now - self.battle_seen_at >= delay then
        self.battle_skip_attempts = (self.battle_skip_attempts or 0) + 1
        pcall(function()
            graphic:DispatchEvent("hide_battle_room")
        end)
        self.status = "自动跳过战斗"
        if self.battle_skip_attempts >= 3 then
            self.battle_seen_at = self.now
        end
    else
        self.status = "准备跳过战斗"
    end
    return true
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
    return {
        version = self.VERSION,
        enabled = self.cfg.enabled,
        status = self.status,
        detail = self.detail,
        stats = stats,
        cfg = cfg,
        logs = logs,
        spendTotal = (type(self.spend_total) == "function") and self:spend_total() or 0,
        fight = self.fight and {
            label = self.fight.label,
            phase = self.fight.phase,
        } or nil,
    }
end

function A:tick(now, in_battle)
    self.now = tonumber(now) or 0
    self.inBattle = in_battle == true
    pcall(function() self:install_audit_hook() end)
    -- 战斗跳过对所有任务生效（日常 / 竞技场 / 虚空 / 工坊都会进战斗）
    self:try_skip_battle()
    return self:snapshot()
end

-- @TASKS@
return A
