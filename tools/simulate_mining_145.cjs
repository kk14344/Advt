// node tools/simulate_mining_145.cjs BASELINE.lua [seed count] [pickaxes] [first seed index] [TNT]
const fs = require('fs');
const path = require('path');
const { performance } = require('perf_hooks');
global.window = global;
global.HTMLDocument = class {};
global.document = { querySelectorAll: () => [], addEventListener: () => {}, readyState: 'loading' };
const { lua, lauxlib, lualib, to_luastring, to_jsstring } = require('../tests/vendor/fengari-web.js');

const [baseline, seedArg = '4', budgetArg = '180', firstArg = '1', tntArg = '12'] = process.argv.slice(2);
if (!baseline) throw Error('Pass the generated baseline Lua as the first argument');
const seedCount = Number(seedArg), firstSeed = Number(firstArg), budget = Number(budgetArg), tnt = Number(tntArg);
if (![seedCount, firstSeed, budget, tnt].every(Number.isSafeInteger) ||
    seedCount < 1 || firstSeed < 1 || budget < 1 || tnt < 0) throw Error('Invalid benchmark settings');
const root = path.resolve(__dirname, '..');
const fixture = fs.readFileSync(path.join(root, 'tests/fixtures/mining-simulation.lua'), 'utf8');
const divider = '\n-- @LOAD_ASSISTANT@\n';
const [setup, model] = fixture.split(divider);
const generated = path.join(root, 'dist/automation.generated.lua');

function makeRunner(filename) {
  const L = lauxlib.luaL_newstate();
  lualib.luaL_openlibs(L);
  const latencies = [];
  lua.lua_pushcfunction(L, state => {
    lua.lua_pushnumber(state, performance.now());
    return 1;
  });
  lua.lua_setglobal(L, to_luastring('bench_clock'));
  lua.lua_pushcfunction(L, state => {
    latencies.push(lua.lua_tonumber(state, 1));
    return 0;
  });
  lua.lua_setglobal(L, to_luastring('bench_tick'));
  function evaluate(script, returns = 0) {
    if (lauxlib.luaL_loadstring(L, to_luastring(script)) !== lua.LUA_OK ||
        lua.lua_pcall(L, 0, returns, 0) !== lua.LUA_OK) {
      throw Error(to_jsstring(lua.lua_tolstring(L, -1)));
    }
  }
  evaluate(setup);
  evaluate(fs.readFileSync(filename, 'utf8'));
  evaluate(model);
  return (seed, vision, mode, spawning) => {
    latencies.length = 0;
    evaluate('return bench_run(' + [seed, vision, mode, spawning, budget, tnt].join(',') + ')', 1);
    const metrics = to_jsstring(lua.lua_tolstring(L, -1)).split(' ').map(Number);
    lua.lua_pop(L, 1);
    return { metrics, latencies: [...latencies] };
  };
}

const before = makeRunner(baseline);
const after = makeRunner(generated);
const keys = ['spent', 'golems', 'nightmares', 'revealed', 'penalty', 'noVision', 'tnt', 'actions', 'ticks',
  'stalled', 'shallowPicks', 'workshopReady'];
function report(runs) {
  const sums = Object.fromEntries(keys.map((key, i) => [key, runs.reduce((sum, run) => sum + run.metrics[i], 0)]));
  const timings = runs.flatMap(run => run.latencies).sort((a, b) => a - b);
  const percentile = p => +timings[Math.floor((timings.length - 1) * p)].toFixed(3);
  const rounded = value => +value.toFixed(3);
  return {
    ...sums,
    killsPer100: rounded((sums.golems + sums.nightmares) * 100 / sums.spent),
    revealedPerPick: rounded(sums.revealed / sums.spent),
    actionsPer100: rounded(sums.actions * 100 / sums.spent),
    ticksPerKill: rounded(sums.ticks / (sums.golems + sums.nightmares)),
    cpuMsPer100: rounded(timings.reduce((a, b) => a + b, 0) * 100 / sums.spent),
    tickMedianMs: percentile(0.5),
    tickP95Ms: percentile(0.95),
  };
}

// Warm up both isolates before timing, then alternate order within each paired map.
before(917, 2, 2, 1);
after(917, 2, 2, 1);
const scenarios = [];
const comparisons = [];
for (let spawning = 1; spawning <= 2; spawning++) {
  for (let vision = 1; vision <= 3; vision++) {
    for (let mode = 1; mode <= 3; mode++) {
      const group = [];
      for (let pair = 0; pair < seedCount; pair++) {
        const seed = 193 + (firstSeed + pair) * 8669;
        const args = [seed, vision, mode, spawning];
        let old, updated;
        if ((vision + mode + pair) % 2) {
          old = before(...args);
          updated = after(...args);
        } else {
          updated = after(...args);
          old = before(...args);
        }
        group.push([old, updated]);
      }
      comparisons.push(...group);
      scenarios.push({ spawning, vision, mode, before: report(group.map(pair => pair[0])),
        after: report(group.map(pair => pair[1])) });
    }
  }
}
console.log(JSON.stringify({
  mapPairs: comparisons.length,
  firstSeedIndex: firstSeed,
  seedCount,
  tntPerMap: tnt,
  picksPerMap: budget,
  before: report(comparisons.map(pair => pair[0])),
  after: report(comparisons.map(pair => pair[1])),
  scenarios,
  pairs: comparisons.map(([old, updated], i) => ({
    seedIndex: firstSeed + i % seedCount,
    scenarioIndex: Math.floor(i / seedCount),
    before: Object.fromEntries(keys.map((key, j) => [key, old.metrics[j]])),
    after: Object.fromEntries(keys.map((key, j) => [key, updated.metrics[j]])),
  })),
}, null, 2));
