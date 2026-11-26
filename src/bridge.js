(function () {
  'use strict';

  if (window.__MWA_APP && window.__MWA_APP_VERSION === '1.0.0') {
    return;
  }

  var VERSION = '1.0.0';
  var CORE_TICK_MIN_MS = 40;
  var CORE_TICK_INTERVAL_MS = 45;
  var BATTLE_SKIP_INTERVAL_MS = 20;
  var LUA_CORE = /* @LUA_CORE@ */;

  var DEFAULTS = {
    autoMine: true,
    autoGolem: true,
    autoBoss: true,
    autoEnter: true,
    refuseEvents: true,
    autoChest: false,
    autoChest40: true,
    autoChest41: true,
    autoChest42: true,
    autoChest43: true,
    autoChest44: false,
    autoChest45: true,
    autoChest48: true,
    waitNightmareCooldown: false,
    autoCaveBoss: false,
    useTntOnLong: false,
    allowLongOre: false,
    autoSkipBattle: true,
    skipOneDemonKeyChest: true,
    autoUseMiningBag: true,
    autoTreasureEvent: true,
    autoMute: true,
    horizontalMining: true,
    horizontalStartDepth: 150,
    horizontalBandMinY: 120,
    horizontalBandMaxY: 150,
    arenaAllowBloodRefresh: false,
    workshopDepth: 150,
    workshopMinutes: 120,
    guildBossCount: 3,
    spendBudget: -1,
    spendBase: 0,
    spendDay: 0,
    spendAllowCaveBuy: true,
    reserveTnt: 0,
    autoUseEarthHeart: true,
    followMiningCamera: true,
    showMiningRoute: false,
    dailyMineGuards: true,
    earthUseAll: false,
    dailyRefine: true,
    refineReserve: 1000,
    dailyAlchemyTier: 1,
    dailyTrollLevel: 0,
    dailyUndergroundLevel: 0,
    dailyVoidChallenge: true,
    dailyMarketExchange: true,
    cartUpgradeMax: true,
    cartBuyEscort: true,
    cartPaidTargetRefresh: true,
    voidExchangeGoldSoul: false,
    voidExchangePurpleSoul: false,
    runeUseFlashGold: true,
    mineResearch: 0,
    mineResearchMask: -1
  };

  for(var dailyId=1;dailyId<=15;dailyId++) DEFAULTS['dailyTask'+dailyId]=true;
  ['dailyRewards','dailyFreePrayer','dailyFreeAlchemy','dailyIncludeMine',
   'dailyGiftAll','dailyCheckinAll','dailyVipReward','dailyVoidExchange','dailyGuildBoss',
   'dailyEvents','dailyMail','dailyGoldSummon','dailyActivityRewards'].forEach(function(key){DEFAULTS[key]=true;});
  var cfg = {};
  Object.keys(DEFAULTS).forEach(function (key) { cfg[key] = DEFAULTS[key]; });
  var coreVm = null;
  var logVm = null, logEpoch = 0;
  var lastError = '';

  function getVM() {
    try {
      return window.__AANDM && window.__AANDM.vm ? window.__AANDM.vm : null;
    } catch (e) {
      return null;
    }
  }

  var probeVM=null,nextCoreAttempt=0,taskActive=false,lastCoreTick=-1000,lastCapturePoll=-1000,captureOutstanding=false;
  var coreTimer = 0;
  function scheduleCoreTick() {
    clearTimeout(coreTimer);
    coreTimer = setTimeout(function () {
      tickOnce();
      scheduleCoreTick();
    }, cfg.enabled || taskActive || captureOutstanding ? CORE_TICK_INTERVAL_MS : 250);
  }
  function applyOptions(vm,values) {
    var entries=Object.keys(values).map(function(key){return '['+JSON.stringify(key)+']='+JSON.stringify(values[key]);});
    vm.execute('do local options={'+entries.join(',')+'};for key,value in pairs(options)do __MWA:set(key,value)end end');
  }
  function ensureCore() {
    var vm=getVM();if(!vm)return false;if(vm===coreVm)return true;
    var now=performance.now();if(probeVM!==vm){probeVM=vm;nextCoreAttempt=0;}
    if(now<nextCoreAttempt)return false;nextCoreAttempt=now+500;
    try{
      var version=vm.execute('return __MWA and __MWA.VERSION or nil');
      if(version!==VERSION){
        var loaded=vm.execute('local c=require("util.constants");return type(c)=="table" and type(c.BLOCK_TYPE)=="table" and type(require("logic.config_manager"))=="table"');
        if(loaded!==true)return false;
        vm.execute(LUA_CORE);
      }
      applyOptions(vm,cfg);coreVm=vm;lastError='';
      if (logVm !== vm) { logVm = vm; logEpoch++; }
      return true;
    }catch(e){coreVm=null;nextCoreAttempt=now+2000;lastError=String(e&&e.message?e.message:e);return false;}
  }

  function luaCall(code) {
    if (!ensureCore()) throw new Error('游戏客户端尚未加载');
    return coreVm.execute(code);
  }

  var captureSent={}, captureGeneration=Date.now().toString(36)+'-'+Math.random().toString(36).slice(2);
  function pumpCaptures() {
    if (!coreVm||(!cfg.enabled&&!captureOutstanding)) return;
    var stamp=performance.now();if(stamp-lastCapturePoll<150)return;lastCapturePoll=stamp;
    var events=coreVm.execute('return __MWA:pending_contract_captures()')||[];
    captureOutstanding=events.length>0;
    events.forEach(function(event){
      var id=String(event.id), now=Date.now();
      if(captureSent[id] && now-captureSent[id]<5000)return;
      var payload={type:'contractStone',id:event.id,amount:event.amount,x:event.x,y:event.y,generation:captureGeneration};
      if(window.chrome&&window.chrome.webview){window.chrome.webview.postMessage(payload);captureSent[id]=now;}
      else if(typeof window.__MWA_NATIVE_CALL==='function'){
        var result=window.__MWA_NATIVE_CALL({command:'captureContract',value:payload});
        if(result&&result.ok)captureSent[id]=now;
      }
    });
  }
  window.__MWA_CAPTURE_ACK=function(generation,id){
    if(generation!==captureGeneration||!coreVm)return false;
    coreVm.execute('__MWA:ack_contract_capture('+Number(id)+')');delete captureSent[String(id)];return true;
  };

  function tickOnce() {
    if (!ensureCore()) return;
    try {
      var engine = window.__AANDM && window.__AANDM.engine;
      var inBattle = false;
      try {
        inBattle = !!(engine && typeof engine.hasVisibleBattle === 'function' && engine.hasVisibleBattle());
      } catch (e) {
        inBattle = false;
      }
      var stamp=performance.now();
      if(stamp-lastCoreTick<CORE_TICK_MIN_MS)return;
      if(!cfg.enabled&&!taskActive&&!inBattle&&stamp-lastCoreTick<250)return;
      lastCoreTick=stamp;
      var now = performance.now() / 1000;
      coreVm.execute('__MWA:tick(' + now.toFixed(3) + ',' + (inBattle ? 'true' : 'false') + ',true)');
      pumpCaptures();
    } catch (e) {
      lastError = String(e && e.message ? e.message : e);
    }
  }

  function snapshot(cursor, epoch) {
    if (!ensureCore()) {
      return {
        ready: false,
        enabled: false,
        status: '等待游戏加载',
        detail: lastError || '正在载入游戏客户端…',
        stats: {},
        cfg: Object.assign({}, cfg),
        logs: [],
        allLogs: []
      };
    }
    try {
      var incremental = Number.isSafeInteger(cursor) && cursor >= 0;
      var since = epoch === logEpoch ? cursor : 0;
      var snap = luaCall(incremental ? 'return __MWA:snapshot(' + since + ')' :
        'local s=__MWA:snapshot();s.allLogs=__MWA.logs;return s');
      if (!snap || typeof snap !== 'object') {
        return {
          ready: false,
          enabled: false,
          status: '等待游戏数据',
          detail: lastError || '客户端仍在初始化…',
          stats: {},
          cfg: Object.assign({}, cfg),
          logs: [],
          allLogs: []
        };
      }
      snap.ready = true;
      if (incremental && Number.isSafeInteger(snap.logSeq)) {
        snap.logEpoch = logEpoch;
        snap.logs = Array.isArray(snap.logs) ? snap.logs : [];
      } else {
        snap.allLogs = Array.isArray(snap.allLogs) ? snap.allLogs : (Array.isArray(snap.logs) ? snap.logs : []);
      }
      snap.appCfg = Object.assign({}, cfg);
      if (!snap.cfg) snap.cfg = Object.assign({}, cfg);
      return snap;
    } catch (e) {
      coreVm = null;
      lastError = String(e && e.message ? e.message : e);
      return {
        ready: false,
        enabled: false,
        status: '自动助手等待中',
        detail: lastError,
        stats: {},
        cfg: Object.assign({}, cfg),
        logs: [],
        allLogs: []
      };
    }
  }

  function start() {
    if (!ensureCore()) {
      return { ok: false, reason: 'waiting' };
    }
    try {
      coreVm.execute('__MWA:set("enabled",true)');
      cfg.enabled = true;
      tickOnce();
      scheduleCoreTick();
      return { ok: true, enabled: true };
    } catch (e) {
      lastError = String(e && e.message ? e.message : e);
      return { ok: false, reason: lastError };
    }
  }

  function stop() {
    cfg.enabled = false;taskActive=false;
    if (!ensureCore()) {
      return { ok: false, reason: 'waiting' };
    }
    try {
      coreVm.execute('__MWA:set("enabled",false)');
      return { ok: true, enabled: false };
    } catch (e) {
      lastError = String(e && e.message ? e.message : e);
      return { ok: false, reason: lastError };
    }
  }

  function setOptions(values) {
    var changed={};
    Object.keys(values||{}).forEach(function(key){
      if(!Object.prototype.hasOwnProperty.call(DEFAULTS,key))return;
      var value=values[key];
      if(typeof DEFAULTS[key]==='number'){
        value=Number(value);
        if(!isFinite(value))value=DEFAULTS[key];
        if(key==='horizontalStartDepth')value=Math.floor(Math.max(1,Math.min(9999,value)));
      }else value=value===true;
      if(cfg[key]!==value){cfg[key]=value;changed[key]=value;}
    });
    // During loading the values stay in JS until the Lua VM is ready.
    if(coreVm&&Object.keys(changed).length){
      try{applyOptions(coreVm,changed);}
      catch(e){coreVm=null;lastError=String(e&&e.message?e.message:e);}
    }
    return true;
  }

  function setOption(key,value){
    if(!Object.prototype.hasOwnProperty.call(DEFAULTS,key))return false;
    var values={};values[key]=value;
    return setOptions(values);
  }

  function getConfig() {
    return Object.assign({}, cfg);
  }

  window.__MWA_APP = {
    version: VERSION,
    ready: function () { return ensureCore(); },
    snapshot: snapshot,
    backgroundPulse: tickOnce,
    start: start,
    stop: stop,
    setOption: setOption,
    setOptions: setOptions,
    getConfig: getConfig,
    cancelTask: function () {
      taskActive=false;if (ensureCore()) return coreVm.execute('return __MWA:cancel_task()');
      return false;
    },
    runLua: function (code) {
      if (!ensureCore()) return null;
      var engine = window.__AANDM && window.__AANDM.engine;
      var inBattle = !!(engine && typeof engine.hasVisibleBattle === 'function' && engine.hasVisibleBattle());
      coreVm.execute('__MWA.now=' + (performance.now()/1000).toFixed(3) + '; __MWA.inBattle=' + (inBattle ? 'true' : 'false'));
      var result=coreVm.execute(code);
      if(code.indexOf(':start_')>=0) {
        taskActive=result!==false;
        if (taskActive) scheduleCoreTick();
      }
      if(code.indexOf('_step(')>=0&&result&&result.done)taskActive=false;
      return result;
    }
  };
  window.__MWA_APP_VERSION = VERSION;

  // Check visibility without rebuilding a Lua snapshot on this fast path.
  setInterval(function () {
    if (!coreVm || cfg.autoSkipBattle === false) return;
    try {
      var engine=window.__AANDM && window.__AANDM.engine;
      if (engine && typeof engine.hasVisibleBattle==='function' && engine.hasVisibleBattle())
        coreVm.execute('__MWA.now='+(performance.now()/1000).toFixed(3)+';__MWA.inBattle=true;__MWA:try_skip_battle()');
    } catch(e) { lastError=String(e && e.message ? e.message : e); }
  },BATTLE_SKIP_INTERVAL_MS);
  tickOnce();
  scheduleCoreTick();
})();
