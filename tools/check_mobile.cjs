// 手机轻量版核心自检：语法编译 + 关键接口存在性 + 无挖矿断言。
const fs = require('fs');
const path = require('path');
global.window = global;
global.HTMLDocument = class {};
global.document = { querySelectorAll: () => [], addEventListener: () => {}, readyState: 'loading' };
global.MutationObserver = class { observe(){} };
const fengari = require('../tests/vendor/fengari-web.js');
const { lua, lauxlib, lualib, to_luastring, to_jsstring } = fengari;
const L = lauxlib.luaL_newstate();
lualib.luaL_openlibs(L);
function run(s, label) {
  if (lauxlib.luaL_loadstring(L, to_luastring(s)) !== lua.LUA_OK || lua.lua_pcall(L, 0, 0, 0) !== lua.LUA_OK)
    throw Error((label || '') + ' :: ' + to_jsstring(lua.lua_tolstring(L, -1)));
}
function assert(cond, msg) { if (!cond) throw Error('assert failed: ' + msg); }

const root = path.resolve(__dirname, '..');
const js = fs.readFileSync(path.join(root, 'dist/aandm-assistant-mobile.js'), 'utf8');
const m = js.match(/var LUA_CORE = (".*");/s);
if (!m) throw Error('LUA_CORE not embedded');
const luaCore = JSON.parse(m[1]);

run(`
network = { event_listener = {}, Send = function() return true end,
  RegisterEvent = function(self, name, fn) self.event_listener[name] = fn end }
graphic = { DispatchEvent = function() end }
config_manager = { resource_config = {}, mercenary_config = {}, item_config = {},
  activity_config = {}, mining_quarry_config = {} }
constants = { RESOURCE_TYPE = { blood_diamond = 1 }, ACHIEVEMENT_TYPE = {},
  CAVE_DAILY_BUY_PRICE = { [3] = { 20 } }, CAVE_DAILY_BUY_CHALLENGE_NUM = { [3] = 1 },
  ARENA_REFRESH_COST_BLOOD_DIAMOND = 60, PRAYER_CONFIG = {}, ALCHEMY_CONFIG = {},
  MAX_LEVEL = 100, CAN_WAKEUP_LEVEL = 30, MAX_WEAPON_LV = 30, MAX_RUNE_BAG_MUILT_SELECT_NUM = 10,
  RECRUIT_COST = { friendship_door = 150 }, FREE_REFRESH_TRAMCAR_TIMES = 3,
  ESCORT_STATUS = { READY = 1, FINISH = 3 }, OPEN_CAVE_BOSS_DEMON_MEDAL = 1,
  CAVE_BOSS_CHALLANGE_SUB = 1, EVENT_MAX_BAG_SPACE_COUNT = 5, VANITY_MAX_MAZE_ID = 6,
  VANITY_MAZE_STATE = { unlock = 1, challenge_able = 2, challenge_success = 3 },
  MERCHANT_TYPE = {}, VIP_STATE = { unbuy = 0, buy = 1, daily_reward = 2 } }
time_logic = { Now = function() return 1000 end, time_zone_offset = 28800,
  GetDateInfo = function(self) return { wday = 7 } end }
utils = { getWDay = function() return 1 end, time_zone = 28800 }
scene_manager = { GetCurrentSceneName = function() return 'world' end }
cc = { Director = { getInstance = function()
  return { getRunningScene = function() return { sub_panels = {} } end } end } }
bag_logic = { GetSpaceCount = function() return 50 end, item_list = {} }
resource_logic = { GetResourceNum = function() return 100 end,
  CheckResourceNum = function() return true end, UpdateResource = function() end }
troop = { GetTroopBP = function() return 1e7 end, CheckMercenaryNum = function() return true end,
  CheckForgeWeaponResource = function() return true end, mercenary_list = {},
  forge_weapon_inflight = nil }
mining = { dig_count = 100, dig_depth = 150, dig_max_count = 120, project_list = {},
  event_count = 5, cave_boss_lv = 1, cave_config_info = { [7] = { 1 } },
  cave_levels = { [1] = 3 }, cave_challenge_nums = { [1] = 5 },
  max_project_count = 2, cave_buy_challenge_nums = {} }
mine_logic = { mine_info_list = {} }
cultivation_logic = { cultivation_info = {} }
package.loaded['util.network'] = network
package.loaded['logic.graphic'] = graphic
package.loaded['logic.config_manager'] = config_manager
package.loaded['util.constants'] = constants
package.loaded['logic.time'] = time_logic
package.loaded['scene.scene_manager'] = scene_manager
package.loaded['logic.bag'] = bag_logic
package.loaded['logic.resource'] = resource_logic
package.loaded['logic.troop'] = troop
package.loaded['logic.mining'] = mining
package.loaded['logic.mine'] = mine_logic
package.loaded['logic.cultivation'] = cultivation_logic
package.loaded['logic.arena'] = { rival_list = {}, challenge_num = 0, refresh_time = 0 }
package.loaded['logic.ladder'] = { challenge_num = 0, rival_list = {} }
package.loaded['logic.escort'] = { escort_info = {}, escort_times = {}, rob_target_list = {} }
package.loaded['logic.rune'] = { rune_list = {} }
package.loaded['logic.social'] = { unsend_gift_list = {} }
package.loaded['logic.vip'] = { vip_list = {} }
package.loaded['logic.daily'] = { liveness_list = {}, activity_receive_list = {},
  prayer_info = { mark = 1 }, alchemy_info = { mark = 1 },
  AlreadyCheckin = function() return true end, CheckActivityComplet = function() return true end }
package.loaded['util.utils'] = { getWDay = function() return 1 end, time_zone = 28800 }
package.loaded['util.client_constants'] = { MINE_STATE = { lock = 0, ready = 1, mining = 2, finish = 3 } }
package.loaded['logic.destiny_weapon'] = { weapon_star_info_list = {} }
package.loaded['logic.achievement'] = { UpdateStatisticValue = function() end }
`);

run(luaCore, 'mobile core');
run(`
assert(__MWA and __MWA.VERSION == '2.0.0', 'core version')
-- 无挖矿断言：核心不得包含挖矿循环 / 掉落统计
assert(__MWA.step == nil, 'mining step must be removed')
assert(__MWA.dig == nil, 'mining dig must be removed')
assert(__MWA.update_fight == nil, 'mining fight must be removed')
assert(__MWA.mining_stats == nil, 'mining drop stats must be removed')
assert(__MWA.mining_stats_snapshot == nil, 'mining stats snapshot must be removed')
assert(__MWA.scan_targets == nil, 'mining scan must be removed')
-- 任务接口齐全
local need = { 'start_daily','daily_step','start_arena','arena_step','start_void','void_step',
  'start_mine_collect','mine_step','start_workshop','workshop_step','start_guild_boss','guild_boss_step',
  'start_rune','rune_step','start_destiny','destiny_step','start_earth_heart','earth_heart_step',
  'start_void_exchange','void_exchange_step','cancel_task','task_request','task_barrier',
  'hide_panel','try_skip_battle','tick','snapshot','set','log','compat_report','match_guide','guide_week' }
for _, name in ipairs(need) do
  assert(type(__MWA[name]) == 'function', 'missing __MWA:' .. name)
end
-- 手机版禁用矿区奇遇日常（不启动完整日常，直接验证 id 11 被拦截）
assert(__MWA.cfg.mobileNoMining == true, 'mobileNoMining default')
__MWA.daily_state = { running = true, loaded = {}, skipped = {}, attempts = {}, claims = 0,
  actions = 0, needs_sync = false, selected = {} }
for id = 1, 15 do __MWA.daily_state.selected[id] = true end
local r = __MWA:daily_action(11, { active_title = '矿区奇遇', condition_count = 1 }, { completion_count = 0 })
assert(r and r.message and tostring(r.message):find('手机版') ~= nil,
  'daily id 11 must be skipped on mobile, got ' .. tostring(r and r.message))
__MWA.daily_state = nil
-- tick 与快照
local snap = __MWA:tick(1.5, false)
assert(type(snap) == 'table' and snap.version == '2.0.0')
assert(snap.status ~= nil and snap.logs ~= nil)
assert(__MWA:set('workshopMinutes', 60))
`);
console.log('MOBILE CORE OK: no-mining assertions passed, all task interfaces present');
