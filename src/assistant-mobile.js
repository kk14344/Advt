(function () {
  'use strict';

  if (window.__AANDM_ASSISTANT) {
    return;
  }

  // ------------------------------------------------------------------
  // 冒险与挖矿 · 自动助手（手机轻量版）
  // 不含自动挖矿。挂载到游戏页面：悬浮球 + 任务面板 + 无声保活。
  // 依赖游戏页面自带的 Lua VM（window.__AANDM.vm）。
  // ------------------------------------------------------------------

  var LUA_CORE = /* @LUA_CORE@ */;

  var DEFAULTS = {
    spendBudget: 360,
    workshopDepth: 150,
    workshopMinutes: 120,
    guildBossCount: 3,
    arenaRounds: 5,
    autoSkipBattle: true,
    keepAlive: true,
    wakeScreen: false,
    notifyDone: true
  };

  var cfg = {};
  try {
    cfg = JSON.parse(localStorage.getItem('aandmCfg') || '{}') || {};
  } catch (e) { cfg = {}; }
  Object.keys(DEFAULTS).forEach(function (k) {
    if (cfg[k] === undefined) cfg[k] = DEFAULTS[k];
  });
  function saveCfg() {
    try { localStorage.setItem('aandmCfg', JSON.stringify(cfg)); } catch (e) { }
  }

  var state = {
    booted: false,
    booting: false,
    task: null,          // 当前任务 {key,label,step}
    lastMessage: '等待游戏加载…',
    lastError: '',
    pendingSentAt: 0,
    audio: null,
    wakeLock: null,
    notifyBanner: ''     // 最近一条任务完成通知（面板横幅）
  };

  var TASKS = {
    daily: { label: '一键日常', start: '__MWA:start_daily()', step: 'return __MWA:daily_step()' },
    arena: { label: '竞技场', start: 'return __MWA:start_arena(tonumber(__MWA.cfg.arenaRounds) or 5)', step: 'return __MWA:arena_step()' },
    void: { label: '虚空', start: 'return __MWA:start_void()', step: 'return __MWA:void_step()' },
    mine: { label: '采集研究', start: 'return __MWA:start_mine_collect()', step: 'return __MWA:mine_step()' },
    wsCollect: { label: '收取工坊', start: "__MWA:start_workshop('collect'); return true", step: 'return __MWA:workshop_step()' },
    wsProduce: { label: '生产工坊', start: "__MWA:start_workshop('produce'); return true", step: 'return __MWA:workshop_step()' },
    guild: { label: '公会首领', start: 'return __MWA:start_guild_boss(tonumber(__MWA.cfg.guildBossCount) or 3)', step: 'return __MWA:guild_boss_step()' },
    rune: { label: '符文', start: 'return __MWA:start_rune()', step: 'return __MWA:rune_step()' },
    destiny: { label: '宿命武器', start: 'return __MWA:start_destiny()', step: 'return __MWA:destiny_step()' },
    earth: { label: '大地之心', start: 'return __MWA:start_earth_heart()', step: 'return __MWA:earth_heart_step()' },
    exchange: { label: '虚空兑换', start: 'return __MWA:start_void_exchange()', step: 'return __MWA:void_exchange_step()' }
  };

  function gameVm() {
    try {
      return window.__AANDM && window.__AANDM.vm ? window.__AANDM.vm : null;
    } catch (e) { return null; }
  }

  function luaRun(code) {
    var vm = gameVm();
    if (!vm) throw new Error('游戏客户端尚未加载');
    return vm.execute(code);
  }

  function boot() {
    if (state.booted || state.booting) return state.booted;
    var vm = gameVm();
    if (!vm) return false;
    state.booting = true;
    try {
      vm.execute(LUA_CORE);
      // 应用本地设置
      Object.keys(DEFAULTS).forEach(function (k) {
        if (k === 'keepAlive' || k === 'wakeScreen') return;
        try {
          vm.execute('__MWA:set(' + JSON.stringify(k) + ',' + JSON.stringify(cfg[k]) + ')');
        } catch (e) { }
      });
      vm.execute('__MWA:set("enabled",true)');
      state.booted = true;
    } catch (e) {
      state.lastError = '助手核心载入失败：' + (e && e.message || e);
    }
    state.booting = false;
    return state.booted;
  }

  // ---------------- 保活 ----------------
  function ensureKeepAlive() {
    if (!cfg.keepAlive) { releaseKeepAlive(); return; }
    try {
      if (!state.audio) {
        var Ctx = window.AudioContext || window.webkitAudioContext;
        if (Ctx) {
          var ctx = new Ctx();
          var osc = ctx.createOscillator();
          var gain = ctx.createGain();
          osc.frequency.value = 34; // 低频近无声
          gain.gain.value = 0.0006;
          osc.connect(gain); gain.connect(ctx.destination);
          osc.start();
          state.audio = { ctx: ctx, osc: osc };
        }
      } else if (state.audio.ctx.state === 'suspended') {
        state.audio.ctx.resume();
      }
    } catch (e) { }
    try {
      if (cfg.wakeScreen && navigator.wakeLock && !state.wakeLock && document.visibilityState === 'visible') {
        navigator.wakeLock.request('screen').then(function (lock) {
          state.wakeLock = lock;
        }).catch(function () { });
      }
      if (!cfg.wakeScreen && state.wakeLock) {
        state.wakeLock.release().catch(function () { });
        state.wakeLock = null;
      }
    } catch (e) { }
  }

  function releaseKeepAlive() {
    try {
      if (state.audio) { state.audio.osc.stop(); state.audio = null; }
    } catch (e) { }
    try {
      if (state.wakeLock) { state.wakeLock.release().catch(function () { }); state.wakeLock = null; }
    } catch (e) { }
  }

  // ---------------- 任务完成通知 ----------------
  function beepDone() {
    try {
      if (!state.audio) { ensureKeepAlive(); }
      if (!state.audio) return;
      var ctx = state.audio.ctx;
      if (ctx.state === 'suspended') { ctx.resume(); }
      var t0 = ctx.currentTime;
      [660, 990].forEach(function (freq, i) {
        var osc = ctx.createOscillator();
        var gain = ctx.createGain();
        osc.type = 'sine';
        osc.frequency.value = freq;
        gain.gain.setValueAtTime(0.0001, t0 + i * 0.18);
        gain.gain.exponentialRampToValueAtTime(0.18, t0 + i * 0.18 + 0.02);
        gain.gain.exponentialRampToValueAtTime(0.0001, t0 + i * 0.18 + 0.16);
        osc.connect(gain); gain.connect(ctx.destination);
        osc.start(t0 + i * 0.18);
        osc.stop(t0 + i * 0.18 + 0.18);
      });
    } catch (e) { }
  }

  function notifyTaskDone(label, message) {
    if (!cfg.notifyDone) return;
    state.notifyBanner = '✅ ' + label + ' 完成：' + message;
    beepDone();
    try { if (navigator.vibrate) navigator.vibrate([120, 80, 120]); } catch (e) { }
    try {
      if (window.Notification && Notification.permission === 'granted') {
        new Notification('冒挖助手 · ' + label + '完成', { body: message });
      }
    } catch (e) { }
  }

  function requestNotifyPermission() {
    try {
      if (window.Notification && Notification.permission === 'default') {
        Notification.requestPermission();
      }
    } catch (e) { }
  }

  // ---------------- UI ----------------
  var ui = {};
  function buildUi() {
    if (ui.root) return;
    var css = document.createElement('style');
    css.textContent = [
      '#aandm-ball{position:fixed;right:10px;bottom:96px;z-index:2147483000;width:46px;height:46px;border-radius:50%;',
      'background:rgba(63,51,32,.88);color:#eed191;border:2px solid #8d6c3d;display:flex;align-items:center;justify-content:center;',
      'font:bold 13px/1 sans-serif;user-select:none;cursor:pointer;box-shadow:0 2px 8px rgba(0,0,0,.45);}',
      '#aandm-panel{position:fixed;right:8px;bottom:150px;z-index:2147483000;width:min(88vw,330px);max-height:62vh;',
      'overflow:auto;background:rgba(48,38,22,.96);color:#f2e8cd;border-radius:10px;border:1px solid #715b38;',
      'font:12px/1.5 sans-serif;padding:10px;display:none;box-shadow:0 4px 16px rgba(0,0,0,.5);}',
      '#aandm-panel.on{display:block}',
      '#aandm-panel h4{margin:2px 0 6px;font-size:13px;color:#eed191}',
      '#aandm-status{min-height:30px;color:#c6d6b2;white-space:pre-wrap;word-break:break-all;margin-bottom:6px}',
      '#aandm-tasks{display:grid;grid-template-columns:1fr 1fr;gap:6px}',
      '#aandm-tasks button{padding:7px 4px;border-radius:6px;border:1px solid #715b38;background:#5d4a29;color:#f2e8cd;font:12px sans-serif;cursor:pointer}',
      '#aandm-tasks button.stop{background:#8a3c30;border-color:#a44;}',
      '#aandm-tasks button.running{background:#3f6b3a;border-color:#6f9}',
      '#aandm-logs{margin-top:6px;max-height:96px;overflow:auto;color:#9fb18d;white-space:pre-wrap;word-break:break-all;font-size:11px}',
      '#aandm-settings{margin-top:6px;display:none;border-top:1px solid #715b38;padding-top:6px}',
      '#aandm-settings.on{display:block}',
      '#aandm-settings label{display:flex;justify-content:space-between;align-items:center;gap:8px;margin:4px 0;color:#c8b98f}',
      '#aandm-settings input{width:84px;background:#3a2f1c;color:#f2e8cd;border:1px solid #715b38;border-radius:4px;padding:3px 6px;font:12px sans-serif}',
      '#aandm-panel .row{display:flex;gap:6px;margin-top:6px}',
      '#aandm-panel .row button{flex:1;padding:6px;border-radius:6px;border:1px solid #715b38;background:#5d4a29;color:#f2e8cd;font:12px sans-serif;cursor:pointer}'
    ].join('');
    document.documentElement.appendChild(css);

    var ball = document.createElement('div');
    ball.id = 'aandm-ball';
    ball.textContent = '助手';
    document.body.appendChild(ball);

    var panel = document.createElement('div');
    panel.id = 'aandm-panel';
    panel.innerHTML =
      '<h4>冒挖助手 <span style="font-weight:400;color:#9fb18d" id="aandm-ver"></span></h4>' +
      '<div id="aandm-status"></div>' +
      '<div id="aandm-tasks"></div>' +
      '<div class="row">' +
      '<button id="aandm-btn-settings">设置</button>' +
      '<button id="aandm-btn-keep">息屏保活:开</button>' +
      '<button id="aandm-btn-wake">屏幕常亮:关</button>' +
      '</div>' +
      '<div class="row">' +
      '<button id="aandm-btn-notify">完成通知:开</button>' +
      '</div>' +
      '<div id="aandm-banner" style="display:none;margin-top:6px;padding:6px 8px;border-radius:6px;' +
      'background:#2f5233;color:#d6f5c9;font-size:12px"></div>' +
      '<div id="aandm-settings">' +
      '<label>血钻日预算<input id="aandm-in-budget" type="number" min="0" max="99999"></label>' +
      '<label>工坊深度<input id="aandm-in-depth" type="number" min="0" max="999"></label>' +
      '<label>工坊档位(分)<input id="aandm-in-minutes" type="number" min="60" max="240" step="60"></label>' +
      '<label>公会首领次数<input id="aandm-in-guild" type="number" min="1" max="99"></label>' +
      '<label>竞技场轮数<input id="aandm-in-arena" type="number" min="1" max="99"></label>' +
      '<div class="row"><button id="aandm-btn-save">保存设置</button></div>' +
      '</div>' +
      '<div id="aandm-logs"></div>';
    document.body.appendChild(panel);

    ui.ball = ball;
    ui.panel = panel;
    ui.status = panel.querySelector('#aandm-status');
    ui.ver = panel.querySelector('#aandm-ver');
    ui.tasks = panel.querySelector('#aandm-tasks');
    ui.logs = panel.querySelector('#aandm-logs');
    ui.settings = panel.querySelector('#aandm-settings');

    ball.addEventListener('click', function () {
      ensureKeepAlive();
      panel.classList.toggle('on');
    });

    Object.keys(TASKS).forEach(function (key) {
      var btn = document.createElement('button');
      btn.textContent = TASKS[key].label;
      btn.id = 'aandm-task-' + key;
      btn.addEventListener('click', function () {
        ensureKeepAlive();
        toggleTask(key);
      });
      ui.tasks.appendChild(btn);
    });
    var stop = document.createElement('button');
    stop.textContent = '停止当前';
    stop.className = 'stop';
    stop.id = 'aandm-task-stop';
    stop.addEventListener('click', stopTask);
    ui.tasks.appendChild(stop);

    panel.querySelector('#aandm-btn-settings').addEventListener('click', function () {
      ui.settings.classList.toggle('on');
      if (ui.settings.classList.contains('on')) fillSettings();
    });
    panel.querySelector('#aandm-btn-keep').addEventListener('click', function () {
      cfg.keepAlive = !cfg.keepAlive;
      saveCfg();
      ensureKeepAlive();
    });
    panel.querySelector('#aandm-btn-wake').addEventListener('click', function () {
      cfg.wakeScreen = !cfg.wakeScreen;
      saveCfg();
      ensureKeepAlive();
    });
    panel.querySelector('#aandm-btn-notify').addEventListener('click', function () {
      cfg.notifyDone = !cfg.notifyDone;
      saveCfg();
      if (cfg.notifyDone) requestNotifyPermission();
    });
    panel.querySelector('#aandm-btn-save').addEventListener('click', function () {
      ['spendBudget', 'workshopDepth', 'workshopMinutes', 'guildBossCount', 'arenaRounds'].forEach(function (k) {
        var input = panel.querySelector('#aandm-in-' + k.replace('spendBudget', 'budget')
          .replace('workshopDepth', 'depth').replace('workshopMinutes', 'minutes')
          .replace('guildBossCount', 'guild').replace('arenaRounds', 'arena'));
        if (!input) return;
        var v = parseInt(input.value, 10);
        if (isFinite(v)) {
          cfg[k] = v;
          try { luaRun('__MWA:set(' + JSON.stringify(k) + ',' + v + ')'); } catch (e) { }
        }
      });
      saveCfg();
      setMsg('设置已保存');
    });
  }

  function fillSettings() {
    var map = {
      spendBudget: '#aandm-in-budget',
      workshopDepth: '#aandm-in-depth',
      workshopMinutes: '#aandm-in-minutes',
      guildBossCount: '#aandm-in-guild',
      arenaRounds: '#aandm-in-arena'
    };
    Object.keys(map).forEach(function (k) {
      var input = ui.panel.querySelector(map[k]);
      if (input) input.value = cfg[k];
    });
  }

  function setMsg(text) {
    state.lastMessage = text;
  }

  function toggleTask(key) {
    if (!boot()) { setMsg(state.lastError || '等待游戏加载…'); return; }
    if (state.task && state.task.key === key) { stopTask(); return; }
    stopTask(true);
    var task = TASKS[key];
    try {
      luaRun(task.start);
      state.task = { key: key, label: task.label, step: task.step };
      setMsg(task.label + '：已启动');
    } catch (e) {
      setMsg(task.label + ' 启动失败：' + (e && e.message || e));
    }
  }

  function stopTask(silent) {
    if (state.task) {
      try { luaRun('return __MWA:cancel_task()'); } catch (e) { }
      if (!silent) setMsg(state.task.label + '：已停止');
      state.task = null;
    }
  }

  function refreshUi() {
    if (!ui.panel) return;
    var snapshot = null;
    try { snapshot = luaRun('return __MWA:snapshot()'); } catch (e) { }
    var statusText;
    if (snapshot) {
      ui.ver.textContent = 'v' + snapshot.version;
      statusText = (state.task ? ('【' + state.task.label + '】') : '【待机】') +
        (snapshot.status || '') +
        (snapshot.detail ? ' · ' + snapshot.detail : '') +
        (state.lastMessage && state.task ? '\n' + state.lastMessage : '');
    } else {
      statusText = state.lastError || '等待游戏客户端加载…';
    }
    if (ui.status.textContent !== statusText) ui.status.textContent = statusText;

    var logs = snapshot && snapshot.logs ? snapshot.logs : [];
    var tail = logs.slice(0, 6).join('\n');
    if (ui.logs.textContent !== tail) ui.logs.textContent = tail;

    Object.keys(TASKS).forEach(function (key) {
      var btn = ui.panel.querySelector('#aandm-task-' + key);
      if (!btn) return;
      var running = state.task && state.task.key === key;
      btn.className = running ? 'running' : '';
      btn.textContent = TASKS[key].label + (running ? ' ●' : '');
    });
    var keepBtn = ui.panel.querySelector('#aandm-btn-keep');
    if (keepBtn) keepBtn.textContent = '息屏保活:' + (cfg.keepAlive ? '开' : '关');
    var wakeBtn = ui.panel.querySelector('#aandm-btn-wake');
    if (wakeBtn) wakeBtn.textContent = '屏幕常亮:' + (cfg.wakeScreen ? '开' : '关');
    var notifyBtn = ui.panel.querySelector('#aandm-btn-notify');
    if (notifyBtn) notifyBtn.textContent = '完成通知:' + (cfg.notifyDone ? '开' : '关');
    var banner = ui.panel.querySelector('#aandm-banner');
    if (banner) {
      if (state.notifyBanner) {
        banner.style.display = 'block';
        if (banner.textContent !== state.notifyBanner) banner.textContent = state.notifyBanner;
      } else {
        banner.style.display = 'none';
      }
    }
  }

  function driveStep() {
    if (!state.task || !state.booted) return;
    var result = null;
    try {
      result = luaRun(state.task.step);
    } catch (e) {
      setMsg(state.task.label + ' 执行异常：' + (e && e.message || e));
      return;
    }
    if (!result || typeof result !== 'object') return;
    if (result.message) setMsg(String(result.message));
    if (result.done) {
      notifyTaskDone(state.task.label, String(result.message || ''));
      state.task = null;
    }
  }

  function mainLoop() {
    if (!state.booted) {
      boot();
      refreshUi();
      return;
    }
    var inBattle = false;
    try {
      var engine = window.__AANDM && window.__AANDM.engine;
      inBattle = !!(engine && typeof engine.hasVisibleBattle === 'function' && engine.hasVisibleBattle());
    } catch (e) { inBattle = false; }
    try {
      luaRun('__MWA:tick(' + (performance.now() / 1000).toFixed(3) + ',' + (inBattle ? 'true' : 'false') + ')');
    } catch (e) { }
    driveStep();
    refreshUi();
  }

  function init() {
    buildUi();
    setInterval(mainLoop, 850);
    setInterval(function () { if (cfg.keepAlive) ensureKeepAlive(); }, 4000);
    document.addEventListener('visibilitychange', function () {
      if (document.visibilityState === 'visible') ensureKeepAlive();
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  window.__AANDM_ASSISTANT = { version: '2.0.0' };
})();
