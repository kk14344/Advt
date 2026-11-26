#!/usr/bin/env node
/**
 * Full-auto, attach-first muted formation deployer for 冒险与挖矿 (test-server).
 *
 * Usage:
 *   node f:\antigra\pvp-simulator\deploy_formation.mjs <preset_key> [--fid 5] [--full-auto] [--dry-run] [--no-runes] [--keep-open]
 *
 * Architecture:
 * 1. Attach-first to existing CDP 127.0.0.1:19341 (0.1s connect, zero cold boot!);
 *    only launches muted helper (--mute-audio) if not already running.
 * 2. Injects lua_auto_deploy.lua ONCE into the client Lua VM (zero external CDP sleep loops).
 * 3. Tier 1 (maxed exact-wash) > Tier 2 (idle non-F1..F4) > Tier 3 (LibraryRecruit),
 *    diff-based formation update + 1-shot AdjustMercenaryPosition + weapon equip.
 * 4. With --full-auto: drives Lv30 / Wake5 / Weapon30 (max extra_chance) / Artifact15 /
 *    Contract2 / Force35 / per-merc exact mixed wash + 200x store auto-restock +
 *    SAFE rune setup (DrawRune auto_compose=false + ExchangeRuneProperty ONLY, NEVER UpgradeRune!).
 * 5. Verifies F1..F4 bit-for-bit untouched + 0/0/0/0 exact stat parity vs SimulatorEngine.
 */

import { spawn, spawnSync, execSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const PRESETS_PATH = path.join(HERE, 'presets.json');
const LUA_PIPELINE_PATH = path.join(HERE, 'lua_auto_deploy.lua');
const HELPER_EXE = 'F:\\helper-adventure\\冒险与挖矿助手-测试服1.41\\冒险与挖矿自动助手-测试服1.41.exe';
const HELPER_DIR = 'F:\\helper-adventure\\冒险与挖矿助手-测试服1.41';
const CDP_URL = 'http://127.0.0.1:19341/json/list';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));

function parseArgs(argv) {
    const args = {
        preset: null,
        fid: 5,
        reuse: true,
        reuseIdle: true,
        fullAuto: false,
        dryRun: false,
        setupRunes: true,
        keepOpen: false,
    };
    for (let i = 0; i < argv.length; i++) {
        const a = argv[i];
        if (a === '--fid' && argv[i + 1]) args.fid = Number(argv[++i]);
        else if (a === '--no-reuse') { args.reuse = false; args.reuseIdle = false; }
        else if (a === '--strict-reuse') { args.reuse = true; args.reuseIdle = false; }
        else if (a === '--reuse') { args.reuse = true; args.reuseIdle = true; }
        else if (a === '--full-auto') args.fullAuto = true;
        else if (a === '--dry-run') args.dryRun = true;
        else if (a === '--no-runes') args.setupRunes = false;
        else if (a === '--keep-open') args.keepOpen = true;
        else if (!a.startsWith('--') && !args.preset) args.preset = a;
    }
    if (!args.preset) {
        console.error('Usage: node deploy_formation.mjs <preset_key> [--fid 5] [--full-auto] [--dry-run] [--no-runes] [--keep-open]');
        process.exit(2);
    }
    return args;
}

function killHelper() {
    try {
        execSync('powershell -NoProfile -Command "Get-Process | Where-Object { $_.ProcessName -like \'*冒险与挖矿自动助手*\' } | Stop-Process -Force -ErrorAction SilentlyContinue"', { stdio: 'ignore' });
    } catch {}
}

async function findGamePage() {
    try {
        const pages = await (await fetch(CDP_URL)).json();
        return pages.find(p => p.type === 'page' && p.url.startsWith('https://47.101.70.170/')) || null;
    } catch {
        return null;
    }
}

const toArr = (v) => Array.isArray(v) ? v : Object.values(v || {});

function toLuaVal(v) {
    if (v === null || v === undefined) return 'nil';
    if (typeof v === 'boolean') return v ? 'true' : 'false';
    if (typeof v === 'number') return String(v);
    if (typeof v === 'string') return JSON.stringify(v);
    if (Array.isArray(v)) return '{ ' + v.map(toLuaVal).join(', ') + ' }';
    return '{ ' + Object.entries(v).map(([k, val]) => `${k} = ${toLuaVal(val)}`).join(', ') + ' }';
}

function getExpectedStatsAsync(presetKey) {
    return new Promise((resolve) => {
        try {
            const py = [
                'import json, sys',
                'from pathlib import Path',
                `sys.path.insert(0, r'${HERE}')`,
                'from engine import SimulatorEngine',
                'eng = SimulatorEngine()',
                `presets = json.loads((Path(r'${HERE}') / 'presets.json').read_text(encoding='utf8'))`,
                `info = eng.inspect_team(presets['${presetKey}'], mode='server30')`,
                "print(json.dumps({'base': info['base_stats'], 'after': info['after_rune_stats'], 'bp': info['max_bp']}))",
            ].join('\n');
            const child = spawn('python', ['-c', py], { stdio: ['ignore', 'pipe', 'ignore'] });
            let out = '';
            child.stdout.on('data', (d) => { out += d.toString('utf8'); });
            child.on('close', (code) => {
                if (code !== 0) return resolve(null);
                try { resolve(JSON.parse(out.trim())); } catch { resolve(null); }
            });
            child.on('error', () => resolve(null));
        } catch {
            resolve(null);
        }
    });
}

async function main() {
    const opts = parseArgs(process.argv.slice(2));
    const presets = JSON.parse(fs.readFileSync(PRESETS_PATH, 'utf8'));
    const target = presets[opts.preset];
    if (!target) throw new Error(`Preset not found: ${opts.preset}`);
    const luaSrc = fs.readFileSync(LUA_PIPELINE_PATH, 'utf8');
    const FID = opts.fid;
    // P2: Run Python parity lookup asynchronously in parallel with CDP connect + Lua deploy (0s startup block!)
    const expectedPromise = opts.dryRun ? Promise.resolve(null) : getExpectedStatsAsync(opts.preset);

    const t0 = Date.now();
    let launchedByUs = false;
    let page = await findGamePage();
    if (!page) {
        killHelper();
        await sleep(400);
        const child = spawn(HELPER_EXE, [], {
            cwd: HELPER_DIR,
            env: { ...process.env, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: '--remote-debugging-port=19341 --mute-audio' },
            detached: true,
            stdio: 'ignore',
        });
        child.unref();
        launchedByUs = true;
        for (let i = 0; i < 100; i++) {
            page = await findGamePage();
            if (page) break;
            await sleep(250);
        }
        if (!page) throw new Error('Game page not found on CDP 19341');
    } else {
        console.log(`[${((Date.now() - t0) / 1000).toFixed(2)}s] Hot-attached to CDP 19341 (0s boot!).`);
    }

    let ws = null;
    try {
        ws = new WebSocket(page.webSocketDebuggerUrl);
        await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
        let seq = 1;
        const evalLua = (lua) => new Promise((resolve, reject) => {
            const id = seq++;
            const onMsg = (ev) => {
                const m = JSON.parse(ev.data);
                if (m.id !== id) return;
                ws.removeEventListener('message', onMsg);
                if (m.error || m.result?.exceptionDetails) return reject(new Error(JSON.stringify(m.error || m.result.exceptionDetails)));
                resolve(m.result.result.value);
            };
            ws.addEventListener('message', onMsg);
            ws.send(JSON.stringify({ id, method: 'Runtime.evaluate', params: { expression: `window.__MWA_APP && window.__MWA_APP.ready && window.__MWA_APP.ready() ? window.__MWA_APP.runLua(${JSON.stringify(lua)}) : null`, returnByValue: true } }));
        });

        const waitReady = async (allowReconnectFallback) => {
            for (let t = 0; t < 240; t++) {
                try {
                    const st = await evalLua(`
                        local tl = require "logic.troop"
                        local res = require "logic.resource"
                        local sm = require "scene.scene_manager"
                        local net = require "util.network"
                        local list = tl and tl:GetMercenaryList() or nil
                        local cnt = 0
                        if list then for _ in pairs(list) do cnt = cnt + 1 end end
                        return {
                            cnt = cnt,
                            fid = tl and tl:GetCurFormationId() or 0,
                            has_f = (tl and tl.formations ~= nil) and true or false,
                            has_r = (res and res.resource_list ~= nil) and true or false,
                            connected = (net and net.IsConnected and net:IsConnected()) and true or false,
                            scene = sm and sm.current_scene_name or "",
                        }
                    `);
                    if (st && st.cnt > 0 && st.fid > 0 && st.has_f && st.has_r) {
                        if (st.connected) return 'ready';
                        if (allowReconnectFallback && st.scene === 'world' && t >= 4) return 'disconnected';
                    }
                } catch {}
                await sleep(250);
            }
            return 'timeout';
        };

        let readyState = await waitReady(!launchedByUs);
        if (readyState === 'disconnected') {
            console.log(`[${((Date.now() - t0) / 1000).toFixed(2)}s] Hot-attached client is disconnected; relaunching muted helper cleanly...`);
            try { ws?.close(); } catch {}
            killHelper();
            await sleep(500);
            const child = spawn(HELPER_EXE, [], {
                cwd: HELPER_DIR,
                env: { ...process.env, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: '--remote-debugging-port=19341 --mute-audio' },
                detached: true,
                stdio: 'ignore',
            });
            child.unref();
            launchedByUs = true;
            page = null;
            for (let i = 0; i < 100; i++) {
                page = await findGamePage();
                if (page) break;
                await sleep(250);
            }
            if (!page) throw new Error('Game page not found on CDP 19341 after relaunch');
            ws = new WebSocket(page.webSocketDebuggerUrl);
            await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
            readyState = await waitReady(false);
        }
        if (readyState !== 'ready') throw new Error('Game client did not finish logging in within 60s');
        console.log(`[${((Date.now() - t0) / 1000).toFixed(2)}s] Client ready. Injecting Lua pipeline...`);

        // Pre-flight snapshot of protected formations (all except FID)
        const snapBefore = await evalLua(`
            local tl = require "logic.troop"
            local out = {}
            for fid = 1, 5 do
                local slots = {}
                for i, m in ipairs(tl:GetFormationMercenaryList(fid) or {}) do
                    slots[i] = { inst = m.instance_id, tid = m.template_id, ex_type = m.ex_prop_type or 0, ex_val = m.ex_prop_val or 0 }
                end
                out["f" .. fid] = { slots = slots, weapon = tl:GetFormationWeaponId(fid) }
            end
            return out
        `);

        // Inject Lua pipeline ONCE
        const loaded = await evalLua(luaSrc);
        if (!loaded || !loaded.loaded) throw new Error('Failed to inject lua_auto_deploy.lua');

        const cfg = {
            fid: FID,
            weapon_id: Number(target.weapon_id || 12),
            reuse: opts.reuse,
            reuse_idle: opts.reuseIdle,
            full_auto: opts.fullAuto,
            max_forge_chance: true,
            auto_restock: true,
            setup_runes: opts.fullAuto && opts.setupRunes,
            runes: target.runes,
            slots: target.slots.map(s => ({ id: Number(s.id), name: s.name, wash: s.wash || '' })),
        };

        if (opts.dryRun) {
            const dr = await evalLua(`return _G.__ANTIGRA_AUTO_DEPLOY.dry_run(${toLuaVal(cfg)})`);
            if (!dr || !dr.ok) throw new Error(`Dry-run failed: ${dr?.err}`);
            console.log(`\n=== DRY-RUN PLAN (${opts.preset} -> F${FID}) ===`);
            console.log(`Tier1(ready)=${dr.stats.reused_tier1} | Tier2(idle)=${dr.stats.reused_tier2} | Tier3(recruit)=${dr.stats.recruited_tier3} | rune_bag_loaded=${dr.rune_list_len}`);
            for (const p of toArr(dr.plan)) {
                console.log(`  [${String(p.pos).padStart(2, '0')}] T${p.tier} ${p.name.padEnd(6, ' ')} (${p.tid}) wash=${(p.wash || 'leader').padEnd(9, ' ')} inst=#${p.inst || 'NEW'} (lv${p.lv}/w${p.wake}/wp${p.wp}/art${p.art}/c${p.clv}/f${p.flv}/ex=${p.ex_type}:${p.ex_val})`);
            }
            return;
        }

        // Launch in-client zero-sleep pipeline!
        const st0 = await evalLua(`return _G.__ANTIGRA_AUTO_DEPLOY.start(${toLuaVal(cfg)})`);
        if (!st0 || !st0.ok) throw new Error(`Pipeline failed to start: ${st0?.err}`);

        let lastLogIdx = 0;
        let finalStatus = null;
        for (let poll = 0; poll < 9600; poll++) {
            await sleep(100);
            const st = await evalLua(`return _G.__ANTIGRA_AUTO_DEPLOY.status()`);
            if (!st) continue;
            const logs = toArr(st.logs);
            while (lastLogIdx < logs.length) {
                console.log('  ' + logs[lastLogIdx++]);
            }
            if (st.done) {
                finalStatus = st;
                break;
            }
        }
        await evalLua(`if _G.__ANTIGRA_AUTO_DEPLOY then _G.__ANTIGRA_AUTO_DEPLOY.cleanup() end; return true`);
        if (!finalStatus) throw new Error('Pipeline timed out after 960s');
        if (!finalStatus.ok) throw new Error(`Pipeline error: ${finalStatus.err}`);

        // Verify protected formations F1..F5 (except FID) are 100% untouched (membership, weapon, and washes!)
        const snapAfter = await evalLua(`
            local tl = require "logic.troop"
            local out = {}
            for fid = 1, 5 do
                local slots = {}
                for i, m in ipairs(tl:GetFormationMercenaryList(fid) or {}) do
                    slots[i] = { inst = m.instance_id, tid = m.template_id, ex_type = m.ex_prop_type or 0, ex_val = m.ex_prop_val or 0 }
                end
                out["f" .. fid] = { slots = slots, weapon = tl:GetFormationWeaponId(fid) }
            end
            return out
        `);
        for (let f = 1; f <= 5; f++) {
            if (f === FID) continue;
            const b = JSON.stringify(snapBefore[`f${f}`]);
            const a = JSON.stringify(snapAfter[`f${f}`]);
            if (b !== a) throw new Error(`SAFETY VIOLATION: protected formation F${f} changed!`);
        }

        const res = finalStatus.result;
        const bs = res.base_stats;
        const eqRunes = toArr(res.eq_runes).map(r => `${r.tid}@Lv${r.lv}`).join(', ');
        console.log(`\n=== FULL-AUTO DEPLOYMENT COMPLETE in ${((Date.now() - t0) / 1000).toFixed(2)}s ===`);
        console.log(`F${FID} weapon=${res.weapon} | BP=${res.bp} | Live Base Stats: 先${bs.speed} / 防${bs.defense} / 闪${bs.dodge} / 王${bs.authority}`);
        console.log(`Protected formations unchanged: YES | Equipped Runes: [${eqRunes || 'unchanged'}]`);
        console.log(`Stats: Tier1=${res.stats.reused_tier1}, Tier2(idle)=${res.stats.reused_tier2}, Tier3(new)=${res.stats.recruited_tier3}, forges=${res.stats.forges}, washes=${res.stats.washes}, restocks=${res.stats.restocks}, rune_draws=${res.stats.rune_draws}, rune_exchanges=${res.stats.rune_exchanges}`);

        const expected = await expectedPromise;
        if (expected && opts.fullAuto) {
            const eb = expected.base;
            const match = (Number(bs.speed) === eb.speed && Number(bs.defense) === eb.defense && Number(bs.dodge) === eb.dodge && Number(bs.authority) === eb.authority);
            console.log(`Simulator 0-Error Parity Check: expected 先${eb.speed}/防${eb.defense}/闪${eb.dodge}/王${eb.authority} -> ${match ? 'EXACT 0/0/0/0 MATCH!' : 'DIFF DETECTED!'}`);
        }
    } finally {
        try { ws?.close(); } catch {}
        if (launchedByUs && !opts.keepOpen) killHelper();
    }
}

main().catch(e => { console.error(e); process.exitCode = 1; });
