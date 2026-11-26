#!/usr/bin/env node
/**
 * 2-second full live state synchronizer for 冒险与挖矿 PvP simulator (test-server).
 *
 * Syncs in one shot over hot-attached CDP 127.0.0.1:19341:
 *   1. /game/mercenary-definitions -> current-mercenary-definitions.json (if revision changed)
 *      + auto-caches any missing gm_* custom portraits into portrait-cache/
 *   2. Live 测试09 rune bag inventory -> live_bag_runes.json (so fast_opt only uses owned runes first!)
 *   3. Optional --opps UID1,UID2,... -> QueryTroopInfo + 1-battle (in_flight=1) capture of TRUE live
 *      template_id_list, weapon, BP, base stats, AND live equipped runes -> auto-syncs presets.json!
 *
 * Usage:
 *   node f:\antigra\pvp-simulator\live_sync.mjs [--opps 73ID6,GE0Z2,3YPN0,NZVHY] [--no-battle-probe]
 */

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const DATA_DIR = process.env.PVP_DATA_DIR || 'F:\\codex1\\tmp\\test-server-pvp';
const DEF_PATH = path.join(DATA_DIR, 'current-mercenary-definitions.json');
const PORTRAIT_DIR = path.join(DATA_DIR, 'portrait-cache');
const PRESETS_PATH = path.join(HERE, 'presets.json');
const BAG_OUT = path.join(HERE, 'live_bag_runes.json');
const CDP_URL = 'http://127.0.0.1:19341/json/list';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));

function parseArgs(argv) {
    const out = { opps: [], battleProbe: true };
    for (let i = 0; i < argv.length; i++) {
        if (argv[i] === '--opps' && argv[i + 1]) {
            out.opps = argv[++i].split(',').map(s => s.trim()).filter(Boolean);
        } else if (argv[i] === '--no-battle-probe') {
            out.battleProbe = false;
        }
    }
    return out;
}

function parseBattleProperty(bpStr) {
    // Format: "myBP:foeBP#...#mySpd:myDef:myDge:myAuth;foeEffSpd:foeEffDef:foeEffDge:foeEffAuth;myBase;foeBase;|myRunes|foeRunes&myW&foeW..."
    if (typeof bpStr !== 'string' || !bpStr.includes('|')) return null;
    try {
        const [statsPart, myR, foeRest] = bpStr.split('|');
        const foeRunesRaw = (foeRest || '').split('&')[0] || '';
        const nums = foeRunesRaw.split(',').map(Number).filter(n => !Number.isNaN(n) && n > 0);
        const foeRunes = [];
        for (let i = 0; i + 1 < nums.length; i += 2) foeRunes.push([nums[i], nums[i + 1]]);
        const parts = statsPart.split('#');
        const bps = (parts[0] || '').split(':').map(Number);
        const segs = (parts[3] || '').split(';');
        const foeEff = (segs[1] || '').split(':').map(Number);
        const foeBase = (segs[3] || '').split(':').map(Number);
        const wParts = (foeRest || '').split('&');
        const foeWeapon = Number(wParts[2] || 12);
        return {
            foe_bp: bps[1] || 0,
            foe_weapon: foeWeapon,
            foe_runes: foeRunes,
            foe_base: { speed: foeBase[0], defense: foeBase[1], dodge: foeBase[2], authority: foeBase[3] },
            foe_eff: { speed: foeEff[0], defense: foeEff[1], dodge: foeEff[2], authority: foeEff[3] },
        };
    } catch {
        return null;
    }
}

async function main() {
    const t0 = Date.now();
    const args = parseArgs(process.argv.slice(2));
    const pages = await (await fetch(CDP_URL)).json();
    const page = pages.find(p => p.type === 'page' && p.url.startsWith('https://47.101.70.170/'));
    if (!page) throw new Error('Game page not found on CDP 127.0.0.1:19341');

    const ws = new WebSocket(page.webSocketDebuggerUrl);
    await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });

    let seq = 1;
    const evalJs = (expression) => new Promise((resolve, reject) => {
        const id = seq++;
        const onMsg = (ev) => {
            const m = JSON.parse(ev.data);
            if (m.id !== id) return;
            ws.removeEventListener('message', onMsg);
            if (m.error || m.result?.exceptionDetails) return reject(new Error(JSON.stringify(m.error || m.result.exceptionDetails)));
            resolve(m.result?.result?.value);
        };
        ws.addEventListener('message', onMsg);
        ws.send(JSON.stringify({ id, method: 'Runtime.evaluate', params: { expression, returnByValue: true, awaitPromise: true } }));
    });
    const evalLua = (lua) => evalJs(`window.__MWA_APP.runLua(${JSON.stringify(lua)})`);

    try {
        // 1. Sync /game/mercenary-definitions + missing gm_* portraits
        const liveDefStr = await evalJs(`fetch('https://47.101.70.170/game/mercenary-definitions').then(r => r.text())`);
        const liveDef = JSON.parse(liveDefStr);
        const oldDef = fs.existsSync(DEF_PATH) ? JSON.parse(fs.readFileSync(DEF_PATH, 'utf8')) : { revision: '' };
        let defUpdated = false;
        if (liveDef.revision !== oldDef.revision) {
            if (fs.existsSync(DEF_PATH)) fs.copyFileSync(DEF_PATH, DEF_PATH + '.bak');
            fs.writeFileSync(DEF_PATH, liveDefStr, 'utf8');
            defUpdated = true;
            console.log(`[1/3] Updated mercenary-definitions.json (rev ${String(oldDef.revision).slice(0,8)} -> ${String(liveDef.revision).slice(0,8)}, mercs=${liveDef.tables?.mercenary?.length})`);
        } else {
            console.log(`[1/3] mercenary-definitions.json already up-to-date (rev ${String(liveDef.revision).slice(0,8)}, mercs=${liveDef.tables?.mercenary?.length})`);
        }

        fs.mkdirSync(PORTRAIT_DIR, { recursive: true });
        let cachedNew = 0;
        for (const m of (liveDef.tables?.mercenary || [])) {
            const sp = String(m.sprite || '');
            if (!sp.startsWith('gm_')) continue;
            const sha = sp.slice(3);
            const dst = path.join(PORTRAIT_DIR, `${sha}.png`);
            if (fs.existsSync(dst) && fs.statSync(dst).size > 100) continue;
            const jsonText = await evalJs(`fetch('https://47.101.70.170/api/game/mercenary-assets/mercenary/${sha}').then(r => r.ok ? r.text() : '')`);
            if (!jsonText) continue;
            try {
                const obj = JSON.parse(jsonText);
                const files = Array.isArray(obj.files) ? obj.files : Object.values(obj.files || {});
                let bestPng = null;
                for (const entry of files) {
                    const b64 = typeof entry === 'string' ? entry : (entry?.data_base64 || entry?.data || '');
                    if (!b64 || b64.length < 100) continue;
                    const buf = Buffer.from(b64.includes('base64,') ? b64.split('base64,')[1] : b64, 'base64');
                    if (buf.subarray(0, 4).toString('hex') === '89504e47') {
                        if (!bestPng || buf.length > bestPng.length) bestPng = buf;
                    }
                }
                if (bestPng) {
                    fs.writeFileSync(dst, bestPng);
                    cachedNew++;
                }
            } catch {}
        }
        if (cachedNew > 0) console.log(`      Cached ${cachedNew} new GM custom portraits.`);

        // 2. Snapshot live rune bag & active formations
        const bagSnap = await evalLua(`
            local rl = require "logic.rune"
            local tl = require "logic.troop"
            local cm = require "logic.config_manager"
            local counts = {}
            local lv20_total = 0
            for _, r in ipairs(rl:GetRuneList() or {}) do
                local k = tostring(r.template_id)
                counts[k] = (counts[k] or 0) + 1
                if (r.level or 0) >= 20 then lv20_total = lv20_total + 1 end
            end
            local eq = {}
            for pos = 1, 5 do
                local r = (rl:GetRuneEquipmentList() or {})[pos]
                if r then eq[pos] = { tid = r.template_id, lv = r.level } end
            end
            local sp, au, dg, df = tl:GetTroopProperty(tl:GetCurFormationId())
            return {
                cur_fid = tl:GetCurFormationId(),
                cur_stats = { speed = sp, defense = df, dodge = dg, authority = au },
                bag_used = #(rl:GetRuneList() or {}),
                bag_cap = rl.GetRuneBagCapacity and rl:GetRuneBagCapacity() or 0,
                lv20_total = lv20_total,
                counts = counts,
                equipped = eq,
            }
        `);
        fs.writeFileSync(BAG_OUT, JSON.stringify({ synced_at: new Date().toISOString(), ...bagSnap }, null, 2), 'utf8');
        console.log(`[2/3] Synced live rune bag (${bagSnap.bag_used}/${bagSnap.bag_cap}, lv20=${bagSnap.lv20_total}) -> live_bag_runes.json`);

        // 3. Optional live opponent sync (strict in_flight=1!)
        if (args.opps.length > 0) {
            const presets = JSON.parse(fs.readFileSync(PRESETS_PATH, 'utf8'));
            const mercMap = Object.fromEntries((liveDef.tables?.mercenary || []).map(m => [m.ID, m.name]));
            const baseMercs = JSON.parse(fs.readFileSync(path.join(DATA_DIR, 'mercenary.json'), 'utf8'));
            const nameOf = (tid) => mercMap[tid] || baseMercs[String(tid)]?.name || (tid >= 99000001 ? '主角' : String(tid));

            await evalLua(`
                local net = require "util.network"
                if not _G.__ANTIGRA_ORIG_DISPATCH then _G.__ANTIGRA_ORIG_DISPATCH = net.event_listener.Dispatch end
                local orig = _G.__ANTIGRA_ORIG_DISPATCH
                _G.__SYNC_TROOP = {}
                _G.__SYNC_BATTLE = {}
                net.event_listener.Dispatch = function(self, ev, msg, ...)
                    if ev == "query_friend_troop_ret" and type(msg) == "table" and msg.user_id then
                        _G.__SYNC_TROOP[msg.user_id] = msg
                    elseif ev == "challenge_friend_ret" and type(msg) == "table" and _G.__SYNC_WANT_UID then
                        _G.__SYNC_BATTLE[_G.__SYNC_WANT_UID] = msg
                        _G.__SYNC_WANT_UID = nil
                        return -- swallow battle playback
                    end
                    return orig(self, ev, msg, ...)
                end
                return true
            `);

            let changedCount = 0;
            for (const uid of args.opps) {
                await evalLua(`require("logic.social"):QueryTroopInfo(${JSON.stringify(uid)}); return true`);
                let troop = null;
                for (let i = 0; i < 40; i++) {
                    await sleep(50);
                    troop = await evalLua(`return _G.__SYNC_TROOP[${JSON.stringify(uid)}]`);
                    if (troop) break;
                }
                let battle = null;
                if (args.battleProbe) {
                    await evalLua(`_G.__SYNC_WANT_UID = ${JSON.stringify(uid)}; require("util.network"):Send({ challenge_friend = { user_id = ${JSON.stringify(uid)} } }); return true`);
                    for (let i = 0; i < 60; i++) {
                        await sleep(60);
                        battle = await evalLua(`return _G.__SYNC_BATTLE[${JSON.stringify(uid)}]`);
                        if (battle) break;
                    }
                }

                const parsed = parseBattleProperty(battle?.battle_property);
                const pk = `friend_${uid}`;
                const existing = presets[pk] || { name: battle?.leader_name || uid, weapon: 12, runes: [], slots: [] };
                const tids = troop?.template_id_list || battle?.template_id_list || [];
                // Preserve existing washes by template_id queue (not by raw index, since live slot order can shift!)
                const washPool = new Map();
                for (const s of (existing.slots || [])) {
                    if (Number(s.id) >= 99000001) continue;
                    const arr = washPool.get(Number(s.id)) || [];
                    arr.push(s.wash || 'speed');
                    washPool.set(Number(s.id), arr);
                }
                const slots = tids.map((tid) => {
                    const idNum = Number(tid);
                    if (idNum >= 99000001) return { id: idNum, name: nameOf(idNum), wash: '' };
                    const arr = washPool.get(idNum);
                    const wash = (arr && arr.length) ? arr.shift() : 'speed';
                    return { id: idNum, name: nameOf(idNum), wash };
                });
                const newObj = {
                    ...existing,
                    name: existing.name || `${battle?.leader_name || uid} (${uid})`,
                    weapon: parsed?.foe_weapon || existing.weapon || 12,
                    runes: (parsed?.foe_runes?.length === 5) ? parsed.foe_runes : existing.runes,
                    stats_override: parsed?.foe_base || (troop ? { speed: troop.speed, defense: troop.defense, dodge: troop.dodge, authority: troop.authority } : existing.stats_override),
                    slots: slots.length === 30 ? slots : existing.slots,
                    note: `${new Date().toISOString().slice(0, 16)} live_sync: BP=${parsed?.foe_bp || troop?.battle_point || 0}, runes=${JSON.stringify(parsed?.foe_runes || existing.runes)}`,
                };
                if (JSON.stringify(presets[pk]) !== JSON.stringify(newObj)) {
                    presets[pk] = newObj;
                    changedCount++;
                }
                const b = newObj.stats_override || {};
                console.log(`  [opp ${uid}] BP=${parsed?.foe_bp || troop?.battle_point} W${newObj.weapon} base=${b.speed}/${b.defense}/${b.dodge}/${b.authority} runes=${JSON.stringify(newObj.runes)}`);
            }

            await evalLua(`
                _G.__SYNC_WANT_UID = nil
                if _G.__ANTIGRA_ORIG_DISPATCH then require("util.network").event_listener.Dispatch = _G.__ANTIGRA_ORIG_DISPATCH end
                return true
            `);
            if (changedCount > 0) {
                fs.writeFileSync(PRESETS_PATH, JSON.stringify(presets, null, 2), 'utf8');
                console.log(`[3/3] Synced ${changedCount}/${args.opps.length} opponents into presets.json.`);
            } else {
                console.log(`[3/3] All ${args.opps.length} opponents already up-to-date.`);
            }
        }
        console.log(`LIVE SYNC COMPLETE in ${((Date.now() - t0) / 1000).toFixed(2)}s (defUpdated=${defUpdated})`);
    } finally {
        ws.close();
    }
}

main().catch(e => { console.error(e); process.exitCode = 1; });
