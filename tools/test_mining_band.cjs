// 挖矿逻辑测试（1.13 = 还原参考 exe 的 1.0 挖矿逻辑）：
// 下探到 150 层锁存横向模式；横向同层向前优先；受阻向下换层、绕行、最后回头；
// 目标（魔像/BOSS/宝箱/藏宝图）按偏移优先级选择。
const fs = require('fs');
const path = require('path');
global.window = global;
global.HTMLDocument = class {};
global.document = { querySelectorAll: () => [], addEventListener: () => {}, readyState: 'loading' };
global.MutationObserver = class { observe(){} };
const fengari = require('../tests/vendor/fengari-web.js');
const root = path.resolve(__dirname, '..');

module.exports = () => {
  const lua = fengari.lua, lauxlib = fengari.lauxlib, lualib = fengari.lualib;
  const to_luastring = fengari.to_luastring, to_jsstring = fengari.to_jsstring;
  const L = lauxlib.luaL_newstate(); lualib.luaL_openlibs(L);
  function run(s, label, returns = false) {
    if (lauxlib.luaL_loadstring(L, to_luastring(s)) !== lua.LUA_OK || lua.lua_pcall(L, 0, returns ? 1 : 0, 0) !== lua.LUA_OK)
      throw Error((label || '') + ' :: ' + to_jsstring(lua.lua_tolstring(L, -1)));
    if (returns) { const value=lua.lua_tonumber(L,-1);lua.lua_pop(L,1);return value; }
  }
  let passed = 0;
  function test(name, s) { run('A.pending_dig=nil;'+s, name); passed++; console.log('PASS ' + name); }

  run(`
    MINE = {
      block_list = {}, cur_position = { x = 5, y = 100 }, dig_endtime = 0, pickaxe_id = 1,
      dig_count = 100, dig_max_count = 100, project_list = {}, bag_boost = 0,
      bot_poison = nil, cur_block_type = 0,
    }
    function MINE:CheckConnected(x, y) return true end
    function MINE:GetBlockType(x, y) return self.block_list[y] and self.block_list[y][x] end
    function MINE:DigOrCollectBlock(x, y)
      self.dig_endtime = time_logic:Now() + 1
      self.cur_position = { x = x, y = y }
      return "success"
    end
    function MINE:GetDigTime() return 0, 0 end
    function MINE:UseTNT() end
    function MINE:QueryBlockInfo() end
    function MINE:IsPendingRandomEvent() return false end
    package.loaded['logic.mining'] = MINE
    time_logic = { Now = function() return 1000 end, time_zone_offset = 28800 }
    package.loaded['logic.graphic'] = { DispatchEvent = function() end }
    package.loaded['logic.bag'] = { item_list = {}, capacity = 20 }
    package.loaded['logic.resource'] = {
      GetResourceNum = function() return 0 end,
      CheckResourceNum = function() return false end,
    }
    package.loaded['logic.user'] = {}
    package.loaded['logic.time'] = time_logic
    package.loaded['scene.scene_manager'] = { GetCurrentSceneName = function() return 'world' end }
    constants = {
      BLOCK_TYPE = { empty = 0, rock = 1, rock_purple_gold = 60, chest1 = 40, chest2 = 41, chest3 = 42, chest4 = 43, chest5 = 44, chest6 = 45, chest7 = 48 },
      BLOCK_COLLECT_TYPE = { golem = 2, chest = 5 },
      MINING_BOSS_MAP = { [50] = true }, RANDOM_EVENT_TYPE = {},
      RESOURCE_TYPE = { blood_diamond = 1 },
    }
    config_manager = {
      mining_dig_info_config = {
        [1] = { ID = 1, collect_type = 0, destroy_level = 1, need_time1 = 1 },
        [2] = { ID = 2, collect_type = 0, destroy_level = 0, need_time1 = 1 },
        [3] = { ID = 3, collect_type = 2, destroy_level = 1, need_time1 = 1 },
        [40] = { ID = 40, collect_type = 5, destroy_level = 1, need_time1 = 1 },
        [41] = { ID = 41, collect_type = 5, destroy_level = 1, need_time1 = 1 },
        [50] = { ID = 50, collect_type = 2, destroy_level = 1, need_time1 = 1 },
      },
      mining_pickaxe_config = { [1] = { destroy_level = 9, efficiency = 1 } },
      event_config = {},
    }
    client_constants = {}
    cc = { Director = { getInstance = function()
      return { getRunningScene = function()
        return { GetCurSubScene = function()
          return { GetName = function() return 'mining_district_sub_scene' end }
        end }
      end }
    end } }
    package.loaded['logic.config_manager'] = config_manager
    package.loaded['util.constants'] = constants
    package.loaded['util.client_constants'] = client_constants
    local network = { event_listener = {}, Send = function() return true end,
      RegisterEvent = function(self, name, fn) self.event_listener[name] = fn end }
    package.loaded['util.network'] = network
    package.loaded['util.utils'] = { getWDay = function() return 1 end }
    setmetatable(package.loaded, { __index = function(t, k)
      local stub = setmetatable({}, { __index = function() return function() end end })
      rawset(t, k, stub)
      return stub
    end })
  `, 'stubs');

  run(fs.readFileSync(path.join(root, 'dist/automation.generated.lua'), 'utf8'), 'automation');
  run(`A = __MWA; A.cfg.actionInterval = 0; A.cfg.maxDigTime = 100; A.now = 100`, 'cfg');

  test('descent mode before reaching 150; rush latches after passing it', `
    MINE.cur_position = { x = 5, y = 100 }
    assert(A:is_descent_mode() == true and A:is_rush_mode() == false, 'must descend before 150')
    MINE.cur_position = { x = 5, y = 151 }
    assert(A:is_rush_mode() == true, 'must latch horizontal after passing 150')
    MINE.cur_position = { x = 5, y = 100 }
    assert(A:is_rush_mode() == true, 'latch must persist even back at shallow depth')
    A.horizontal_started = nil
    MINE.cur_position = { x = 5, y = 100 }
    assert(A:is_descent_mode() == true, 'fresh state must descend again')
  `);

  test('descent prefers straight down, then sidestep, up is last resort', `
    MINE.block_list = {}
    MINE.block_list[100] = { [3] = 1, [5] = 0, [6] = 1 }
    MINE.block_list[101] = { [5] = 1 }
    MINE.cur_position = { x = 5, y = 100 }
    local best = A:choose_descent_frontier(A:frontier())
    assert(best and best.y == 101 and best.x == 5, 'straight down must win, got ('
      ..tostring(best and best.x)..','..tostring(best and best.y)..')')
    MINE.block_list[101] = { [4] = 1, [5] = 2, [6] = 1 }
    best = A:choose_descent_frontier(A:frontier())
    assert(best and best.y == 100 and best.x == 6, 'blocked down must sidestep, got ('
      ..tostring(best and best.x)..','..tostring(best and best.y)..')')
    MINE.block_list[100] = { [4] = 2, [5] = 0, [6] = 2 }
    MINE.block_list[99] = { [5] = 1 }
    best = A:choose_descent_frontier(A:frontier())
    assert(best and best.y == 99, 'up is allowed as last resort, got ('
      ..tostring(best and best.x)..','..tostring(best and best.y)..')')
  `);

  test('rush digs same-row forward first, then down, back is last resort', `
    MINE.cur_position = { x = 5, y = 160 }
    A.horizontal_started = true
    MINE.block_list = {}
    MINE.block_list[160] = { [5] = 0, [4] = 1, [6] = 1, [7] = 1 }
    MINE.block_list[161] = { [5] = 1, [6] = 1 }
    MINE.block_list[159] = { [5] = 1, [6] = 1 }
    local best = A:choose_rush_frontier(A:frontier())
    assert(best and best.y == 160 and best.x > 5, 'forward same row must win, got ('
      ..tostring(best and best.x)..','..tostring(best and best.y)..')')
    MINE.block_list[160] = { [4] = 1, [5] = 0, [6] = 2, [7] = 1 }
    best = A:choose_rush_frontier(A:frontier())
    assert(best and best.y == 161 and best.x == 5, 'blocked forward must go down first, got ('
      ..tostring(best and best.x)..','..tostring(best and best.y)..')')
    MINE.block_list[161] = { [5] = 2, [6] = 2 }
    MINE.block_list[159] = { [5] = 2 }
    best = A:choose_rush_frontier(A:frontier())
    assert(best and best.y == 160 and best.x == 4, 'down and up blocked must backtrack, got ('
      ..tostring(best and best.x)..','..tostring(best and best.y)..')')
  `);

  test('targets are labelled and golems rank ahead of boss and chest', `
    -- 魔像 (8,130)、副本BOSS (9,131)、宝箱 (9,129) 同时在场：全部可扫描
    MINE.block_list = {}
    MINE.block_list[130] = { [5] = 0, [6] = 1, [7] = 1, [8] = 3, [9] = 50 }
    MINE.block_list[131] = { [8] = 1, [9] = 41 }
    MINE.block_list[129] = { [8] = 1, [9] = 40 }
    MINE.cur_position = { x = 5, y = 130 }
    A.now = 1000
    A.cfg.autoGolem = true
    A.cfg.autoBoss = true
    A.cfg.autoChest = true
    A.cfg.autoChest41 = true
    A.failed = {}
    local targets = A:scan_targets()
    local byLabel = {}
    for i = 1, #targets do byLabel[targets[i].label] = targets[i] end
    assert(byLabel['魔像'] and byLabel['魔像'].golem, 'golem must be scanned')
    assert(byLabel['矿区BOSS'] and byLabel['矿区BOSS'].boss, 'boss must be scanned')
    assert(byLabel['宝箱'] and byLabel['宝箱'].chest, 'chest must be scanned')
    -- 优先级：魔像(偏移0) > BOSS(3000) > 宝箱(5000) —— 在 step 的评分中
    -- 魔像与 BOSS 同时可达时先挑战魔像。用 mine_frontier 分别验证可朝目标推进。
    local ok = A:mine_frontier(byLabel['魔像'])
    assert(ok and MINE.cur_position.x > 5, 'must advance toward the golem')
  `);

  test('mine_frontier digs toward the requested target', `
    -- 目标魔像在 (9,130)，与玩家同行，中间可挖：应朝它推进
    MINE.block_list = {}
    MINE.block_list[130] = { [3] = 0, [4] = 0, [5] = 0, [6] = 1, [7] = 1, [8] = 1, [9] = 3 }
    MINE.block_list[129] = { [3] = 0, [4] = 0, [5] = 0 }
    MINE.cur_position = { x = 5, y = 130 }
    MINE.dig_endtime = 0
    A.now = 1200
    local target
    for _, t in ipairs(A:scan_targets()) do if t.golem then target = t end end
    assert(target, 'golem target must exist')
    local ok = A:mine_frontier(target)
    assert(ok == true, 'must dig toward the golem')
    assert(MINE.cur_position.x > 5, 'must advance toward the golem, got x='
      ..tostring(MINE.cur_position.x))
  `);

  function setupFight(y, blockType, solvedAgo, cellAfter) {
    return `
      MINE.block_list = {}
      MINE.block_list[${y}] = { [6] = ${cellAfter} }
      MINE.cur_position = { x = 5, y = ${y} }
      MINE.__mw_pending_monster = nil
      A.inBattle = false
      A.now = 1000 + ${solvedAgo}
      A.failed = {}
      A.nightmare_wait_until = nil
      A.nightmare_wait_key = nil
      A.cfg.nightmareCooldown = 120
      A.cfg.lossCooldown = 300
      A.cfg.waitNightmareCooldown = false
      A.fight = { x = 6, y = ${y}, block_type = ${blockType}, label = '魔像', boss = false,
        chest = false, golem = true, phase = 'solve', at = 1000, solved_at = 1000, settlement_received=true }
    `;
  }

  test('a cleared (empty) cell counts as a win', setupFight(130, 3, 20, 0) + `
      local wins = A.stats.wins
      A:update_fight()
      assert(A.stats.wins == wins + 1, 'must count as a win, status=' .. tostring(A.status))
      assert(A.fight == nil, 'fight must be finished')
  `);

  test('a lost golem fight blacklists the cell without any global wait', setupFight(130, 3, 20, 3) + `
      A.stats.losses = 0
      A:update_fight()
      assert(A.stats.losses == 1, 'defeat must count as a loss')
      assert(A.failed['6:130'] ~= nil, 'defeated cell must be blacklisted')
      assert(A.nightmare_wait_until == nil, 'no global nightmare wait may be set')
  `);

  test('mining continues elsewhere right after a loss', setupFight(130, 3, 20, 3) + `
      A.failed['6:130'] = A.now + 120
      A.fight = nil
      A.status = ''
      A:step()
      assert(tostring(A.status):find('魇魔') == nil, 'must never show a nightmare wait')
  `);

  // ------------------------------------------------------------
  // 挖矿统计（1.16）：矿镐消耗取 dig_count 下降量，资源取 UpdateResource。
  // ------------------------------------------------------------
  test('mining stats count pickaxe consumption only while auto mining runs', `
      A.cfg.enabled = false; A.cfg.autoMine = true
      A.mining_stats = { picks = 0, res = {}, active = false, prev = 0, session = 0 }
      MINE.dig_count = 100
      A:tick(10, false)
      assert(A.mining_stats.active == false and A.mining_stats.picks == 0, 'paused must not start a session')
      MINE.dig_count = 90
      A:tick(11, false)
      assert(A.mining_stats.picks == 0, 'pickaxe loss while paused must not be counted')
      A.cfg.enabled = true
      A:tick(12, false)
      assert(A.mining_stats.session == 1 and A.mining_stats.picks == 0, 'a new session starts at zero')
      MINE.dig_count = 87
      A:tick(13, false)
      assert(A.mining_stats.picks == 3, 'pickaxe consumption must be counted, got ' .. tostring(A.mining_stats.picks))
      MINE.dig_count = 92
      A:tick(14, false)
      assert(A.mining_stats.picks == 3, 'pickaxe refill must not subtract from consumption')
      MINE.dig_count = 90
      A:tick(15, false)
      assert(A.mining_stats.picks == 5, 'consumption after a refill continues, got ' .. tostring(A.mining_stats.picks))
  `);

  test('mining stats count every resource gained during the session', `
      A.cfg.enabled = true; A.cfg.autoMine = true
      A.mining_stats = { picks = 0, res = {}, active = true, prev = MINE.dig_count, session = 1 }
      local rl = package.loaded['logic.resource']
      rl.amounts = {}
      rl.UpdateResource = function(self, id, num)
        self.amounts[id] = (self.amounts[id] or 0) + num
      end
      A:install_mining_stats_hook()
      rl:UpdateResource(7, 120)
      rl:UpdateResource(8, 40)
      rl:UpdateResource(7, 30)
      rl:UpdateResource(9, -25)
      rl:UpdateResource(28, -12)
      assert(A.mining_stats.res['7'] == 150, 'must accumulate repeated gains')
      assert(A.mining_stats.res['8'] == 40)
      assert(A.mining_stats.res['9'] == nil, 'losses must not count as gains')
      assert(A.mining_stats.spent['9'] == 25 and A.mining_stats.spent['28'] == 12,
        'spending must be recorded for net calculations')
      A.mining_stats.active = false
      rl:UpdateResource(7, 500)
      rl:UpdateResource(28, -5)
      assert(A.mining_stats.res['7'] == 150, 'gains outside auto mining must be ignored')
      assert(A.mining_stats.spent['28'] == 12, 'spending outside auto mining must be ignored')
  `);

  test('mining stats snapshot exposes spending for net product lines', `
      A.mining_stats = { picks = 0, res = { ['1'] = 9000, ['2'] = 30 }, spent = { ['1'] = 2000, ['38'] = 3 },
        active = true, prev = 0, session = 2, extra = {} }
      local snap = A:mining_stats_snapshot()
      assert(snap.spent['1'] == 2000 and snap.spent['38'] == 3, 'snapshot must expose spent map')
      assert(snap.list[1].id == 1 or snap.list[1].id == 2)
  `);

  test('mining stats capture battle rewards only while a fight is pending', `
      A.cfg.enabled = true; A.cfg.autoMine = true
      A.mining_stats = { picks = 0, res = {}, spent = {}, active = true, prev = MINE.dig_count, session = 1,
        report = {}, last_rewards = '', fight_until = 0 }
      A.fight = { x = 3, y = 4, label = '魇魔', phase = 'solve' }
      A.now = 100
      A:mining_stats_capture_rewards({ reward_info_list = {
        { id = 2, param1 = 23, param2 = 3 }, { id = 6, param1 = 300 } } })
      assert(A.mining_stats.last_rewards == '资源23×3 主角战力×300',
        'fight rewards must be captured, got ' .. tostring(A.mining_stats.last_rewards))
      -- 战斗结束后 6 秒窗口内仍可捕获（迟到的奖励；窗口由战斗结算设置）
      A.fight = nil
      A.mining_stats.fight_until = 106
      A.now = 105
      A:mining_stats_capture_rewards({ reward_info_list = { { id = 6, param1 = 500 } } })
      assert(A.mining_stats.last_rewards == '主角战力×500')
      -- 窗口之外（挖矿采集）不捕获
      A.now = 200
      A:mining_stats_capture_rewards({ reward_info_list = { { id = 2, param1 = 7, param2 = 99 } } })
      assert(A.mining_stats.last_rewards == '主角战力×500', 'ore collects must not enter the report')
  `);

  test('mining stats report records fights with rewards and latest first', `
      A.cfg.enabled = true; A.cfg.autoMine = true
      A.mining_stats = { picks = 0, res = {}, spent = {}, active = true, prev = MINE.dig_count, session = 1,
        report = {}, last_rewards = '地狱硫磺×3 主角战力×300', fight_until = 0 }
      A.now = 300
      A:mining_stats_report('战胜 魇魔', A.mining_stats.last_rewards)
      A.mining_stats.last_rewards = ''
      A.now = 301
      A:mining_stats_report('战胜 巨魔杂兵', '巨魔雕像×1')
      local snap = A:mining_stats_snapshot()
      assert(snap.report and #snap.report == 2, 'two entries expected')
      assert(snap.report[1].text == '战胜 巨魔杂兵' and snap.report[1].rewards == '巨魔雕像×1',
        'newest entry must come first')
      assert(snap.report[2].rewards == '地狱硫磺×3 主角战力×300')
      assert(snap.report[1].t ~= nil and snap.report[1].t ~= '')
  `);

  test('mining stats snapshot reports a sorted, named resource list',`
      config_manager.resource_config = { [7] = { name = '铜矿' }, [8] = { name = '铁矿' } }
      A.mining_stats = { picks = 12, res = { ['8'] = 40, ['7'] = 150 }, active = true, prev = 0, session = 3,
        day = 20000, extra = { strength = 42, bp = 300 } }
      local snap = A:mining_stats_snapshot()
      assert(snap.picks == 12 and snap.session == 3 and snap.active == true)
      assert(snap.strength == 42 and snap.bp == 300, 'strength and battle power must be reported')
      assert(snap.list[1].id == 7 and snap.list[1].name == '铜矿' and snap.list[1].num == 150)
      assert(snap.list[1].cat == 'ore', 'ore must be categorised, got ' .. tostring(snap.list[1].cat))
      assert(snap.list[2].id == 8 and snap.list[2].name == '铁矿')
      local full = A:snapshot()
      assert(full.miningStats and full.miningStats.list[1].id == 7, 'snapshot must expose miningStats')
  `);

  test('mining stats categorise ores, gems, materials, currency, keys', `
      local cases = {
        { 1, 'gold' }, { 2, 'blood' }, { 35, 'contract' }, { 36, 'exp' },
        { 7, 'ore' }, { 13, 'ore' }, { 14, 'gem' }, { 17, 'gem' },
        { 39, 'key' }, { 60, 'material' }, { 89, 'material' }, { 18, 'other' },
      }
      for _, case in ipairs(cases) do
        local got = A:mining_stats_category(case[1])
        assert(got == case[2], 'resource ' .. tostring(case[1]) .. ' expected ' .. case[2] .. ', got ' .. tostring(got))
      end
  `);

  test('mining stats count strength points and battle power rewards', `
      A.cfg.enabled = true; A.cfg.autoMine = true
      A.mining_stats = { picks = 0, res = {}, active = true, prev = MINE.dig_count, session = 1, extra = {} }
      constants.ACHIEVEMENT_TYPE = { strength_pt = 77 }
      local ach = { calls = 0 }
      ach.UpdateStatisticValue = function(self, stat_type, value)
        self.calls = self.calls + 1
        self.last_type = stat_type
      end
      package.loaded['logic.achievement'] = ach
      A:install_mining_stats_strength_hook()
      ach:UpdateStatisticValue(77, 3)
      ach:UpdateStatisticValue(77, 2)
      ach:UpdateStatisticValue(11, 999)
      assert(A.mining_stats.extra.strength == 5, 'strength must accumulate, got ' .. tostring(A.mining_stats.extra.strength))
      assert(ach.calls == 3, 'the original statistic handler must still run')
      A.mining_stats.active = false
      ach:UpdateStatisticValue(77, 100)
      assert(A.mining_stats.extra.strength == 5, 'strength outside mining must be ignored')
  `);

  test('mining stats count battle power from reward_info_list', `
      A.cfg.enabled = true; A.cfg.autoMine = true
      A.mining_stats = { picks = 0, res = {}, active = true, prev = MINE.dig_count, session = 1, extra = {} }
      local listener = { reward_info_list = function() end }
      package.loaded['util.network'].event_listener = listener
      local net_ref = package.loaded['util.network']
      A:install_mining_stats_reward_hook()
      assert(listener.reward_info_list ~= nil)
      listener.reward_info_list({ reward_info_list = {
        { id = 6, param1 = 1500 }, { id = 2, param1 = 7, param2 = 500 } } })
      assert(A.mining_stats.extra.bp == 1500, 'battle power reward must be counted, got ' .. tostring(A.mining_stats.extra.bp))
      assert(A.mining_stats.res['7'] == nil, 'resources are counted by UpdateResource only')
  `);

  test('mining stats restart a fresh session when mining is restarted', `
      A.cfg.enabled = true; A.cfg.autoMine = true
      MINE.dig_count = 50
      A.mining_stats = { picks = 999, res = { ['7'] = 999 }, active = false, prev = 50, session = 4 }
      A:tick(20, false)
      assert(A.mining_stats.session == 5, 'restarting must bump the session id')
      assert(A.mining_stats.picks == 0 and next(A.mining_stats.res) == nil, 'restarting must clear this session')
  `);

  test('fast mining waits for a matching acknowledgement before another dig', `
      MINE.dig_endtime=0;MINE.block_list={[130]={[6]=1,[7]=1}};A.inBattle=false;A.fight=nil
      A.blocked_until=0;A.now=100;A.cfg.enabled=true;A.cfg.actionInterval=0.03;A.last_action_at=0
      local old=MINE.DigOrCollectBlock;local calls=0
      MINE.DigOrCollectBlock=function()calls=calls+1;return 'success' end
      assert(A:dig(6,130));A.now=100.05
      assert(not A:dig(7,130) and calls==1)
      A:mining_on_response('dig_block_ret',{result='success',cur_position={x=99,y=130}})
      assert(A.pending_dig and not A:dig(7,130))
      A:mining_on_response('dig_block_ret',{result='success',cur_position={x=6,y=130}})
      assert(A:dig(7,130) and calls==2)
      MINE.DigOrCollectBlock=old
  `);
  test('fast mining respects the server terrain timer after acknowledgement', `
      MINE.dig_endtime=1001;A.now=100;A.last_action_at=0;A.blocked_until=0
      assert(not A:can_act())
      MINE.dig_endtime=1000;assert(A:can_act())
  `);
  test('missing mining acknowledgement reads state without blind resend or automatic shutdown', `
      MINE.dig_endtime=0;MINE.block_list={[130]={[6]=1}};MINE.__mw_pending_monster=nil
      MINE.cur_position={x=5,y=130};A._mine_sync=nil;A._task_pending=nil
      A.inBattle=false;A.fight=nil;A.cfg.enabled=true;A.now=1
      A.pending_dig={x=6,y=130,block_type=1,game_at=1000,at=1}
      A.now=2;assert(not A:pending_dig_resolved())
      A.now=22;assert(not A:pending_dig_resolved() and A.cfg.enabled and A.pending_dig and A._mine_sync)
  `);
  test('a server dig rejection backs off before retrying', `
      A.now=100;A.last_action_at=0;A.blocked_until=0;MINE.dig_endtime=0
      A.pending_dig={x=6,y=130,block_type=1,game_at=1000,at=100}
      A:mining_on_response('dig_block_ret',{result='not_enough_count',cur_position={x=5,y=130}})
      assert(not A.pending_dig and not A:can_act())
      A.now=101.01;assert(A:can_act())
  `);
  test('pausing mining cancels a refill chain and preserves the uncertain request barrier', `
      A.cfg.enabled=true;A.fast_refill={owner='mine'}
      A._task_pending={event='use_item_ret',kind='fast_refill',sent_at=100}
      A:set('enabled',false)
      assert(not A.fast_refill and not A._task_pending and A._cancelled_task_pending)
      A._cancelled_task_pending=nil
  `);
  run(`
    function runtime_fixture(count,refuse)
      A:cancel_task();A._route_job=nil;A._cancelled_task_pending=nil;A.pending_dig=nil;A._mine_sync=nil;A.mine_refill=nil
      A._mine_event=nil;A._mine_event_done=nil;A.daily_state=nil;A.now=100;A.last_action_at=0;A.blocked_until=0
      A.cfg.enabled=true;A.cfg.autoUseMiningBag=true;A.cfg.refuseEvents=refuse~=false
      A.cfg.horizontalStartDepth=121 -- Legacy event/refill fixtures use their original depth band.
      A.cfg.autoUseEarthHeart=true;A.cfg.earthUseAll=true;A.inBattle=false;A.fight=nil;A.pause_until=nil;A.cfg.actionInterval=0.03
      A._mine_blast=nil;A.failed={};MINE.cur_position={x=5,y=130};MINE.dig_endtime=0;MINE.dig_count=count or 0;MINE.dig_max_count=1000
      MINE.golem_num=0;MINE.__mw_pending_monster=nil;MINE.pending_random_event=nil;MINE.show_random_event=nil
      MINE.block_list={[130]={[6]=1,[7]=1}}
      constants.RESOURCE_TYPE.ultimate_tool=29;constants.RANDOM_EVENT_TYPE.occupancy=1;constants.RANDOM_EVENT_TYPE.resource=2
      RTNET=package.loaded['util.network'];RTSENT={};RTSTOCK=100
      RTNET.Send=function(net,msg)RTSENT[#RTSENT+1]=msg;return true end
      RTNET._mwa_audit=nil;A._task_hooks={}
      package.loaded['logic.resource'].GetResourceNum=function(self,id)return id==29 and RTSTOCK or 0 end
      MINE.IsPendingRandomEvent=function(self,ev)return type(ev)=='table' and ev.event_type~=nil end
      MINE.AcceptRandomEvent=function(self,op)RTNET:Send({accept_random_event={accept_opeation=op,random_event=self.show_random_event}})end
      RTNET.event_listener.use_mining_tool_ret=function(msg)
        if msg.result=='success' then RTSTOCK=RTSTOCK-1;MINE.dig_count=MINE.dig_count+(msg.add_dig or 0) end
      end
      RTNET.event_listener.query_mining_info_ret=function(msg)
        if msg.cur_position then MINE.cur_position=msg.cur_position end
        if msg.dig_count then MINE.dig_count=msg.dig_count end
        if msg.dig_endtime then MINE.dig_endtime=msg.dig_endtime end
      end
      RTNET.event_listener.accept_random_event_ret=function(msg)
        if msg.result=='success' then MINE.dig_count=MINE.dig_count-(msg.pick_cost or 0) end
      end
      RTNET.event_listener.dig_block_ret=function()end
      A:install_task_hooks()
    end
    function rt_reply(name,msg)RTNET.event_listener[name](msg)end
    function rt_bag(add)rt_reply('use_mining_tool_ret',{result='success',resource_id=29,num=1,add_dig=add or 5})end
    function rt_event(cost)
      MINE.pending_random_event={event_type=1,occupy_type=1,position={x=5,y=130},end_time=2000,pick_cost=cost or 40}
      return MINE.pending_random_event
    end
  `,'runtime fixtures');
  test('automatic digging uses one bag when short and never starts heart-first batch refill', `
      runtime_fixture(0,true);assert(A:try_use_mining_bag())
      assert(#RTSENT==1 and RTSENT[1].use_mining_tool.num==1 and not A.fast_refill)
      rt_bag();assert(not A.mine_refill and #RTSENT==1 and MINE.dig_count==5)
  `);
  test('refuse mode does not refill forty available pickaxes to capacity', `
      runtime_fixture(40,true);assert(not A:try_use_mining_bag() and #RTSENT==0)
  `);
  test('accept mode requires strictly more than forty pickaxes', `
      runtime_fixture(40,false);assert(A:try_use_mining_bag())
      assert(RTSENT[1].use_mining_tool.num==1);rt_bag()
      assert(MINE.dig_count==45 and not A.mine_refill and #RTSENT==1)
      assert(not A:try_use_mining_bag())
  `);
  test('accept mode rapidly chains nine single bags from zero and stops at forty-five', `
      runtime_fixture(0,false);A:try_use_mining_bag()
      for i=1,9 do
        assert(#RTSENT==i and RTSENT[i].use_mining_tool.num==1)
        rt_bag()
      end
      assert(MINE.dig_count==45 and not A.mine_refill and #RTSENT==9)
  `);
  test('single-bag refill cannot send a second use before acknowledgement', `
      runtime_fixture(0,false);A:try_use_mining_bag()
      for i=1,10 do A.now=100+i*0.05;A:try_use_mining_bag()end
      assert(#RTSENT==1 and A._task_pending.kind=='mine_single_bag')
  `);
  test('single-bag acknowledgement without a local count update reads authoritative state', `
      runtime_fixture(40,false);A:try_use_mining_bag();rt_bag(0)
      assert(#RTSENT==2 and RTSENT[2].query_mining_info)
      rt_reply('query_mining_info_ret',{dig_count=45})
      assert(not A.mine_refill and #RTSENT==2)
  `);
  test('single-bag timeout queries instead of repeating a possibly successful use', `
      runtime_fixture(0,true);A:try_use_mining_bag();A.now=104;A:mine_bag_step()
      assert(#RTSENT==2 and RTSENT[2].query_mining_info)
      rt_reply('query_mining_info_ret',{dig_count=5})
      assert(not A.mine_refill and #RTSENT==2 and A.cfg.enabled)
  `);
  test('a nonincreasing refill snapshot never burns another bag', `
      runtime_fixture(0,true);A:try_use_mining_bag();rt_bag(0)
      for i=1,4 do rt_reply('query_mining_info_ret',{dig_count=0});A.now=A.now+1;A:mine_bag_step()end
      for _,msg in ipairs(RTSENT)do if msg.use_mining_tool then assert(msg==RTSENT[1])end end
      assert(A.cfg.enabled and A.mine_refill)
  `);
  test('checked refusal rejects a costly event immediately even with zero pickaxes', `
      runtime_fixture(0,true);local event=rt_event(40)
      assert(A:try_refuse_event());assert(#RTSENT==1 and RTSENT[1].accept_random_event.accept_opeation=='refuse')
      rt_reply('accept_random_event_ret',{result='success',accept_opeation='refuse',random_event=event,pick_cost=0})
      assert(not A._mine_event and not MINE.pending_random_event and MINE.dig_count==0)
  `);
  test('unchecked refusal refills above forty and then automatically accepts the event', `
      runtime_fixture(36,false);local event=rt_event(40)
      assert(A:try_refuse_event() and RTSENT[1].use_mining_tool.num==1)
      rt_bag();assert(MINE.dig_count==41 and not A.mine_refill)
      A:try_refuse_event();assert(#RTSENT==2 and RTSENT[2].accept_random_event.accept_opeation=='accept')
      rt_reply('accept_random_event_ret',{result='success',accept_opeation='accept',random_event=event,pick_cost=40})
      assert(not A._mine_event and not MINE.pending_random_event)
      assert(A:try_use_mining_bag() and RTSENT[3].use_mining_tool.num==1)
  `);
  test('a random event is never submitted a second time while its response is pending', `
      runtime_fixture(45,false);rt_event();A:try_refuse_event()
      for i=1,10 do A.now=A.now+0.05;A:try_refuse_event()end
      assert(#RTSENT==1)
  `);
  test('an acknowledgement for another event cannot clear the pending event', `
      runtime_fixture(45,false);rt_event();A:try_refuse_event()
      rt_reply('accept_random_event_ret',{result='success',accept_opeation='refuse'})
      assert(A._mine_event and A._task_pending and not A._task_pending.response)
  `);
  test('free resource events are accepted while costly event refusal is enabled', `
      runtime_fixture(5,true);MINE.pending_random_event={event_type=2,occupy_type=0,position={x=5,y=130}}
      A:try_refuse_event();assert(RTSENT[1].accept_random_event.accept_opeation=='accept')
  `);
  test('a nil native dig return never creates a phantom pending dig or increments statistics', `
      runtime_fixture(45,true);local old=MINE.DigOrCollectBlock;MINE.DigOrCollectBlock=function()return nil end
      local before=A.stats.digs
      assert(not A:dig(6,130) and not A.pending_dig and A.stats.digs==before)
      MINE.DigOrCollectBlock=old
  `);
  test('an updated native current position resolves a missed dig callback immediately', `
      runtime_fixture(45,true);A.pending_dig={x=6,y=130,block_type=1,at=90,game_at=1000}
      MINE.cur_position={x=6,y=130}
      assert(A:pending_dig_resolved() and not A.pending_dig and A.cfg.enabled and #RTSENT==0)
  `);
  test('two consistent idle snapshots recover a stalled dig without disabling mining', `
      runtime_fixture(45,true);A.pending_dig={x=6,y=130,block_type=1,at=98,game_at=1000}
      assert(not A:pending_dig_resolved() and #RTSENT==1)
      rt_reply('query_mining_info_ret',{cur_position={x=5,y=130},dig_endtime=0,dig_count=45})
      assert(A.pending_dig);A.now=100.2;A:pending_dig_resolved();assert(#RTSENT==2)
      rt_reply('query_mining_info_ret',{cur_position={x=5,y=130},dig_endtime=0,dig_count=45})
      assert(not A.pending_dig and not A._mine_sync and A.cfg.enabled)
  `);
  test('busy or incomplete snapshots cannot authorize a new dig', `
      runtime_fixture(45,true);A.pending_dig={x=6,y=130,block_type=1,at=98,game_at=1000}
      A:pending_dig_resolved();rt_reply('query_mining_info_ret',{dig_count=45})
      assert(A.pending_dig and not A._mine_sync.idle)
      A.now=100.2;A:pending_dig_resolved()
      rt_reply('query_mining_info_ret',{cur_position={x=5,y=130},dig_endtime=1001,dig_count=45})
      assert(A.pending_dig and not A._mine_sync.idle and not A:can_act())
  `);
  test('stopping automatic digging cancels single-bag continuation', `
      runtime_fixture(0,false);A:try_use_mining_bag();A:set('enabled',false);rt_bag()
      assert(not A.mine_refill and #RTSENT==1 and not A.cfg.enabled)
  `);
  test('a zero-pickaxe free resource event needs no bag while costly events are refused', `
      runtime_fixture(0,true);MINE.pending_random_event={event_type=2,occupy_type=0,position={x=5,y=130}}
      A:try_refuse_event();assert(#RTSENT==1 and RTSENT[1].accept_random_event.accept_opeation=='accept')
  `);
  test('the daily mining path retains its existing batch refill policy', `
      runtime_fixture(0,true);A.daily_state={running=true}
      A:try_use_mining_bag()
      assert(A.fast_refill and not A.mine_refill and RTSENT[1].use_mining_tool.num==5)
  `);
  test('the actual mining loop refills then accepts a costly event without manual input', `
      runtime_fixture(36,false);local event=rt_event(40)
      A:step();assert(#RTSENT==1 and RTSENT[1].use_mining_tool.num==1)
      rt_bag();A:step()
      assert(#RTSENT==2 and RTSENT[2].accept_random_event.accept_opeation=='accept')
      rt_reply('accept_random_event_ret',{result='success',accept_opeation='accept',random_event=event,pick_cost=40})
      A.now=100.05;A:step();assert(RTSENT[3].use_mining_tool.num==1 and A.cfg.enabled)
  `);
  test('the actual mining loop rejects a costly event before using any bag in refusal mode', `
      runtime_fixture(0,true);rt_event(40);A:step()
      assert(#RTSENT==1 and RTSENT[1].accept_random_event.accept_opeation=='refuse' and A.cfg.enabled)
  `);
  test('event acknowledgement accepts the server-assigned finish time', `
      runtime_fixture(45,false);local event=rt_event(40);A:try_refuse_event()
      rt_reply('accept_random_event_ret',{result='success',accept_opeation='accept',random_event={event_type=1,occupy_type=1,position={x=5,y=130},end_time=2300},pick_cost=40})
      assert(not A._mine_event and not A._task_pending and not MINE.pending_random_event)
  `);
  test('optional zero event fields do not prevent acknowledgement matching', `
      runtime_fixture(45,false);MINE.pending_random_event={event_type=2,position={x=5,y=130}}
      A:try_refuse_event()
      rt_reply('accept_random_event_ret',{result='success',accept_opeation=0,random_event={event_type=2,occupy_type=0,position={x=5,y=130},end_time=0}})
      assert(not A._mine_event and not A._task_pending)
  `);
  test('stopping during refill readback retains the uncertain bag-use barrier', `
      runtime_fixture(0,false);A:try_use_mining_bag();A.now=104;A:mine_bag_step()
      assert(A._task_pending.kind=='mine_bag_snapshot')
      A:set('enabled',false)
      assert(not A.mine_refill and A._cancelled_task_pending.event=='use_mining_tool_ret')
      local count=#RTSENT;rt_bag();assert(#RTSENT==count and not A._cancelled_task_pending)
  `);
  run(`
    function descent_fixture(depth)
      runtime_fixture(45,true);A.cfg.autoMine=true;MINE.cur_position={x=5,y=depth};MINE.cur_block_type=0
      MINE.block_list={[depth]={[5]=0,[4]=0},[depth+1]={[5]=1,[4]=1}}
      A.last_stall_at=0;A._camera_position=nil;A.horizontal_started=true
    end
  `,'automatic descent fixtures');
  test('automatic mining at depth 120 handles a golem before descending', `
      descent_fixture(120);MINE.block_list[120][4]=3;A.cfg.autoGolem=true;A:step()
      assert(A.fight and A.fight.x==4 and A.fight.y==120 and A.cfg.enabled,A.status)
      assert(not A.status:find('自动下潜') and #RTSENT==0)
  `);
  test('automatic mining starts descending at all shallower depths without manual input', `
      for _,depth in ipairs({0,1,50,119})do
        descent_fixture(depth);A:step();assert(MINE.cur_position.y==depth+1,A.status)
      end
  `);
  test('shallow descent retains single-bag refill before continuing', `
      descent_fixture(120);MINE.dig_count=0;A:step()
      assert(RTSENT[1].use_mining_tool.num==1 and MINE.cur_position.y==120)
      rt_bag();A:step();assert(MINE.cur_position.y==121)
  `);
  test('shallow descent accepts events automatically after replenishing above forty', `
      descent_fixture(119);A.cfg.refuseEvents=false;MINE.dig_count=40;local ev=rt_event(40)
      A:step();assert(RTSENT[1].use_mining_tool.num==1);rt_bag()
      A:step();assert(RTSENT[2].accept_random_event.accept_opeation=='accept')
      rt_reply('accept_random_event_ret',{result='success',accept_opeation='accept',random_event=ev,pick_cost=0})
      A.now=100.2;A:step();assert(MINE.cur_position.y==120)
  `);
  test('shallow descent waits for native terrain cooldown rather than sending another dig', `
      descent_fixture(100);MINE.dig_endtime=1002;MINE.GetDigTime=function()return 2,2 end
      A:step();assert(MINE.cur_position.y==100 and #RTSENT==0)
      MINE.GetDigTime=function()return 0,0 end;MINE.dig_endtime=0;A:step();assert(MINE.cur_position.y==101)
  `);
  test('a shallow map without a diggable path waits safely and recovers on map update', `
      descent_fixture(100);MINE.block_list={[100]={[5]=0}};A:step()
      assert(MINE.cur_position.y==100 and A.cfg.enabled and A.status:find('通路'))
      MINE.block_list[101]={[5]=1};A:step();assert(MINE.cur_position.y==101)
  `);
  test('reaching depth 121 returns to the established mining planner', `
      descent_fixture(120);A:step();assert(MINE.cur_position.y==121)
      MINE.block_list={[121]={[5]=0},[122]={[5]=1}};MINE.dig_endtime=0;A.pending_dig=nil;A.now=100.5
      local original=A.scan_targets;local scanned=false
      A.scan_targets=function()scanned=true;return {}end
      A:step();A.scan_targets=original;assert(scanned,'regular targets must be considered after arrival')
  `);
  test('disabling automatic excavation does not force shallow descent', `
      descent_fixture(120);A.cfg.autoMine=false;MINE.block_list={[120]={[5]=0},[121]={[5]=1}}
      A:step();assert(MINE.cur_position.y==120 and #RTSENT==0)
      A.cfg.autoMine=true
  `);
  test('automatic mining begins normal processing only at depth 121 or deeper', `
      runtime_fixture(0,false);MINE.cur_position={x=5,y=121};rt_event();A:step()
      assert(#RTSENT==1 and RTSENT[1].use_mining_tool.num==1)
  `);
  test('the automatic mining depth threshold does not block the daily mining path', `
      runtime_fixture(50,true);MINE.cur_position={x=5,y=100};A.daily_state={running=true}
      assert(A:can_act())
  `);
  run(`CAMERA_ORIGINAL_READ=A.read_context
    function camera_fixture()
      runtime_fixture(45,true);A.cfg.followMiningCamera=true;A._camera_failed_scene=nil;A._camera_position=nil;A._camera_next_at=nil;A._camera_resume_at=nil
      CAM={calls=0,MoveTo=function(self,p)self.calls=self.calls+1;self.target=p end}
      A.read_context=function()return {sub_name='mining_district_sub_scene',sub_scene=CAM}end
    end
  `,'camera fixtures');
  test('camera following moves only the native viewport and leaves gameplay requests untouched', `
      camera_fixture();local pending={x=6,y=130};A.pending_dig=pending
      assert(A:follow_mining_camera() and CAM.calls==1 and CAM.target.y==129)
      assert(#RTSENT==0 and A.pending_dig==pending and MINE.cur_position.x==5 and MINE.dig_count==45)
  `);
  test('camera following throttles movement and does not redraw an unchanged position', `
      camera_fixture();A:follow_mining_camera();MINE.cur_position.y=131;A.now=100.05
      assert(not A:follow_mining_camera() and CAM.calls==1)
      A.now=100.16;assert(A:follow_mining_camera() and CAM.calls==2)
      A.now=101;A:follow_mining_camera();assert(CAM.calls==2)
  `);
  test('manual camera dragging postpones following without postponing mining', `
      camera_fixture();CAM.touch_num=1;local blocked=A.blocked_until
      assert(not A:follow_mining_camera());CAM.touch_num=0;A.now=101
      assert(not A:follow_mining_camera() and A.blocked_until==blocked and A:can_act())
      A.now=102.01;assert(A:follow_mining_camera() and CAM.calls==1)
  `);
  test('a camera API error disables only that camera scene and cannot stop mining', `
      camera_fixture();local calls=0;CAM.MoveTo=function()calls=calls+1;error('visual API unavailable')end
      local errors=A.stats.errors
      assert(not A:follow_mining_camera());A.now=101;assert(not A:follow_mining_camera())
      assert(calls==1 and A.cfg.enabled and A.stats.errors==errors and #RTSENT==0)
  `);
  test('camera following remains inactive when disabled paused or in another scene', `
      camera_fixture();A.cfg.followMiningCamera=false;assert(not A:follow_mining_camera())
      A.cfg.followMiningCamera=true;A.cfg.enabled=false;assert(not A:follow_mining_camera())
      A.cfg.enabled=true
      MINE.cur_position.y=130;A.read_context=function()return {sub_name='main_sub_scene',sub_scene=CAM}end
      assert(not A:follow_mining_camera() and CAM.calls==0)
  `);
  test('camera following also follows automatic shallow descent', `
      camera_fixture();MINE.cur_position.y=100;assert(A:follow_mining_camera() and CAM.target.y==99 and #RTSENT==0)
  `);
  run(`A.read_context=CAMERA_ORIGINAL_READ`,'restore camera fixture');
  // Legacy scenarios assert the completed decision. Drain cooperative planner
  // frames at the same game time; new 1.35 cases also exercise each raw frame.
  run(`A._test.step_frame=A.step;function A:step()
    self._test.step_frame(self)
    local guard=0
    while self.status=='规划目标通路' do
      guard=guard+1;assert(guard<1000,'route planner did not finish')
      self._test.step_frame(self)
    end
  end`);
  require('./test_mining_route_125.cjs')({run,test});
  require('./test_mining_126.cjs')({run,test});
  require('./test_mining_127.cjs')({run,test});
  require('./test_mining_135.cjs')({run,test});
  const selectedDepthBefore=passed;
  test('selected depth 200 descends through 199 and immediately mines sideways at 200', `
      descent_fixture(199);A:set('horizontalStartDepth',200);A:step()
      assert(MINE.cur_position.y==200 and A.cfg.enabled)
      A.pending_dig=nil;MINE.dig_endtime=0;A.now=101
      MINE.block_list={[200]={[5]=0,[6]=1},[201]={[5]=1}}
      A:step();assert(MINE.cur_position.x==6 and MINE.cur_position.y==200,A.status)
  `);
  test('selected depths below the old 121 threshold start sideways without forced deeper digging', `
      descent_fixture(60);A:set('horizontalStartDepth',60)
      MINE.block_list={[60]={[5]=0,[6]=1},[61]={[5]=1}}
      A:step();assert(A:automatic_mining_depth_ready() and MINE.cur_position.x==6 and MINE.cur_position.y==60)
  `);
  test('a current depth beyond the selected layer starts horizontal excavation immediately', `
      descent_fixture(250);A:set('horizontalStartDepth',180)
      MINE.block_list={[250]={[5]=0,[6]=1},[251]={[5]=1}}
      A:step();assert(MINE.cur_position.x==6 and MINE.cur_position.y==250)
  `);
  test('raising the selected layer resets the old horizontal latch without clearing an uncertain dig', `
      descent_fixture(170);A:set('horizontalStartDepth',150);assert(A:is_rush_mode())
      local pending={x=6,y=170};A.pending_dig=pending
      A:set('horizontalStartDepth',220)
      assert(A:is_descent_mode() and not A:automatic_mining_depth_ready() and A.pending_dig==pending)
  `);
  test('the exact selected layer is inclusive and the current position controls readiness', `
      descent_fixture(179);A:set('horizontalStartDepth',180);MINE.dig_depth=500
      assert(not A:automatic_mining_depth_ready())
      MINE.cur_position.y=180;assert(A:automatic_mining_depth_ready())
  `);
  test('selected-layer descent keeps a visible golem ahead of moving down', `
      descent_fixture(199);A:set('horizontalStartDepth',200);MINE.block_list[199][4]=3;A.cfg.autoGolem=true
      A:step();assert(A.fight and A.fight.x==4 and A.fight.y==199 and MINE.cur_position.y==199)
  `);
  test('depth settings normalize fractional and out-of-range input without changing paid-task depth', `
      descent_fixture(100);A.cfg.workshopDepth=150
      A:set('horizontalStartDepth',0);assert(A.cfg.horizontalStartDepth==1)
      A:set('horizontalStartDepth',100000);assert(A.cfg.horizontalStartDepth==9999)
      A:set('horizontalStartDepth',99.9);assert(A.cfg.horizontalStartDepth==99 and A.cfg.workshopDepth==150)
      A:set('horizontalStartDepth',150)
  `);
  fs.writeFileSync(path.join(root,'dist/selected-depth-test-results.json'),JSON.stringify({result:'passed',scenarios:passed-selectedDepthBefore},null,2));
  console.log('mining band scenarios: ' + passed);
  test('contract stone receipts queue exactly one capture per positive mining resource delta', `
    local rl=package.loaded['logic.resource'];A.cfg.enabled=true;A.cfg.autoMine=true
    A.mining_stats={active=true,res={},spent={}};A.contract_captures={};A.contract_capture_seq=0;A.now=100
    rl.UpdateResource=function()end;A:install_mining_stats_hook();A:install_mining_stats_hook()
    rl:UpdateResource(35,1);rl:UpdateResource(35,2);rl:UpdateResource(35,-1);rl:UpdateResource(35,0);rl:UpdateResource(7,10)
    local q=A:pending_contract_captures();assert(#q==2 and q[1].amount==1 and q[2].amount==2)
    assert(A.contract_capture_hold==100.7)
    local second=q[2].id;A:ack_contract_capture(q[1].id);assert(#q==1 and q[1].id==second)
    A:ack_contract_capture(second);A:ack_contract_capture(second);assert(#q==0)
    A.cfg.enabled=false;rl:UpdateResource(35,3);assert(#q==0)
    A.cfg.enabled=true;A.mining_stats.active=false;rl:UpdateResource(35,3);assert(#q==0)
    A.cfg.autoMine=false;A.mining_stats.active=true;rl:UpdateResource(35,3);assert(#q==0)
  `);
  return passed;
};

