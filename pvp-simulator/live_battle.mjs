#!/usr/bin/env node
/**
 * Strict in_flight=1 live friend-battle verifier for 冒险与挖矿 (test-server).
 *
 * Permanent rules enforced:
 *   - STRICT in_flight = 1 (0ms event-driven on challenge_friend_ret, NEVER >1!)
 *   - Swallows battle UI playback (~100 battles in ~11s, 4x80 in ~36s, 0 dropped packets)
 *   - Default counts: 100 battles for single opponent, 80 battles each for multiple opponents
 *   - Outputs standardized JSON report consumable directly by `sim.py export --live-report <json>`
 *
 * Usage:
 *   node f:\antigra\pvp-simulator\live_battle.mjs --fid 5 --opps 3YPN0:100 --sim 3YPN0:99.85 --out report.json
 *   node f:\antigra\pvp-simulator\live_battle.mjs --fid 5 --opps 73ID6:80,GE0Z2:80,3YPN0:80,NZVHY:80 --target 70 --out report.json
 */

import fs from 'node:fs';
import path from 'node:path';

const CDP_URL = 'http://127.0.0.1:19341/json/list';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));

const LABELS = {
    '73ID6': '测试02',
    'GE0Z2': '测试03',
    '3YPN0': '测试15',
    'NZVHY': '测试16',
    'DAFKJ': '测试04',
    '70SXF': '测试08',
    'P0PLM': '测试18',
};

function parseArgs(argv) {
    const out = { fid: 5, opps: [], simMap: {}, target: 70, outPath: null };
    for (let i = 0; i < argv.length; i++) {
        const a = argv[i];
        if (a === '--fid' && argv[i + 1]) out.fid = Number(argv[++i]);
        else if (a === '--opps' && argv[i + 1]) {
            const raw = argv[++i].split(',').map(s => s.trim()).filter(Boolean);
            const defN = raw.length === 1 ? 100 : 80;
            for (const item of raw) {
                const [uid, nStr] = item.split(':');
                out.opps.push({ uid: uid.trim(), n: nStr ? Number(nStr) : defN });
            }
        } else if (a === '--sim' && argv[i + 1]) {
            for (const item of argv[++i].split(',').map(s => s.trim()).filter(Boolean)) {
                const [uid, v] = item.split(':');
                if (uid && v !== undefined) out.simMap[uid.trim()] = Number(v);
            }
        } else if (a === '--target' && argv[i + 1]) out.target = Number(argv[++i]);
        else if (a === '--out' && argv[i + 1]) out.outPath = argv[++i];
    }
    if (!out.opps.length) {
        console.error('Usage: node live_battle.mjs --fid 5 --opps UID[:N],... [--sim UID:PCT,...] [--target 70] [--out file.json]');
        process.exit(2);
    }
    return out;
}

function parseOppSummary(bpStr) {
    if (typeof bpStr !== 'string' || !bpStr.includes('|')) return null;
    try {
        const [statsPart, , foeRest] = bpStr.split('|');
        const foeRunesRaw = (foeRest || '').split('&')[0] || '';
        const nums = foeRunesRaw.split(',').map(Number).filter(n => !Number.isNaN(n) && n > 0);
        const runes = [];
        for (let i = 0; i + 1 < nums.length; i += 2) runes.push(nums[i]);
        const segs = (statsPart.split('#')[3] || '').split(';');
        const base = (segs[3] || '').split(':').map(Number);
        return { base: `${base[0]}/${base[1]}/${base[2]}/${base[3]}`, runes };
    } catch {
        return null;
    }
}

async function main() {
    const tAll = Date.now();
    const args = parseArgs(process.argv.slice(2));
    const pages = await (await fetch(CDP_URL)).json();
    const page = pages.find(p => p.type === 'page' && p.url.startsWith('https://47.101.70.170/'));
    if (!page) throw new Error('Game page not found on CDP 127.0.0.1:19341');

    const ws = new WebSocket(page.webSocketDebuggerUrl);
    await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
    let seq = 1;
    const evalLua = (lua) => new Promise((resolve, reject) => {
        const id = seq++;
        const onMsg = (ev) => {
            const m = JSON.parse(ev.data);
            if (m.id !== id) return;
            ws.removeEventListener('message', onMsg);
            if (m.error || m.result?.exceptionDetails) return reject(new Error(JSON.stringify(m.error || m.result.exceptionDetails)));
            resolve(m.result?.result?.value);
        };
        ws.addEventListener('message', onMsg);
        ws.send(JSON.stringify({ id, method: 'Runtime.evaluate', params: { expression: `window.__MWA_APP.runLua(${JSON.stringify(lua)})`, returnByValue: true } }));
    });

    const results = [];
    try {
        await evalLua(`
            local tl = require "logic.troop"
            if tl:GetCurFormationId() ~= ${args.fid} then tl:ChangeFormation(${args.fid}) end
            return tl:GetCurFormationId()
        `);
        for (let i = 0; i < 40; i++) {
            if (Number(await evalLua(`return require("logic.troop"):GetCurFormationId()`)) === args.fid) break;
            await sleep(80);
        }
        await sleep(150);

        for (const job of args.opps) {
            const t0 = Date.now();
            // STRICT in_flight = 1 event-driven loop
            await evalLua(`
                local net = require "util.network"
                if not _G.__ANTIGRA_ORIG_DISPATCH then _G.__ANTIGRA_ORIG_DISPATCH = net.event_listener.Dispatch end
                local orig = _G.__ANTIGRA_ORIG_DISPATCH
                _G.__LB1 = {
                    active = true, target = ${job.n}, uid = ${JSON.stringify(job.uid)},
                    wins = 0, losses = 0, resolved = 0, last_ts = os.clock(),
                    err = nil, sample_bp = nil, leader_name = nil
                }
                net.event_listener.Dispatch = function(self, ev, msg, ...)
                    if ev == "challenge_friend_ret" and _G.__LB1 and _G.__LB1.active then
                        local st = _G.__LB1
                        if type(msg) == "table" and msg.result == "success" then
                            st.resolved = st.resolved + 1
                            if not st.sample_bp then
                                st.sample_bp = msg.battle_property
                                st.leader_name = msg.leader_name
                            end
                            local w = (msg.is_winner == true or msg.is_winner == 1 or msg.is_win == true or msg.is_win == 1)
                            if w then st.wins = st.wins + 1 else st.losses = st.losses + 1 end
                            if st.resolved < st.target then
                                st.last_ts = os.clock()
                                net:Send({ challenge_friend = { user_id = st.uid } })
                            else
                                st.active = false
                                net.event_listener.Dispatch = orig
                            end
                        else
                            st.err = tostring(type(msg) == "table" and msg.result or "bad_msg")
                            st.active = false
                            net.event_listener.Dispatch = orig
                        end
                        return
                    end
                    return orig(self, ev, msg, ...)
                end
                _G.__LB1.last_ts = os.clock()
                net:Send({ challenge_friend = { user_id = ${JSON.stringify(job.uid)} } })
                return true
            `);

            let st = null;
            for (let tick = 0; tick < 600; tick++) {
                await sleep(120);
                st = await evalLua(`return _G.__LB1`);
                if (!st || !st.active) break;
                const kicked = await evalLua(`
                    local st = _G.__LB1
                    if st and st.active and (os.clock() - st.last_ts) > 3.0 then
                        st.last_ts = os.clock()
                        require("util.network"):Send({ challenge_friend = { user_id = st.uid } })
                        return true
                    end
                    return false
                `);
                if (kicked) console.log(`  [${job.uid}] watchdog resend at ${st.resolved}/${job.n}`);
            }
            await evalLua(`
                if _G.__LB1 then _G.__LB1.active = false end
                if _G.__ANTIGRA_ORIG_DISPATCH then require("util.network").event_listener.Dispatch = _G.__ANTIGRA_ORIG_DISPATCH end
                return true
            `);

            const sec = Number(((Date.now() - t0) / 1000).toFixed(2));
            const wins = Number(st?.wins || 0);
            const losses = Number(st?.losses || 0);
            const resolved = Number(st?.resolved || 0);
            const wr = resolved > 0 ? Number(((wins / resolved) * 100).toFixed(2)) : 0;
            const simPct = args.simMap[job.uid] ?? null;
            const drift = simPct !== null ? Number((wr - simPct).toFixed(2)) : null;
            const oppSum = parseOppSummary(st?.sample_bp);
            const label = st?.leader_name || LABELS[job.uid] || job.uid;
            const rec = {
                fid: args.fid,
                uid: job.uid,
                label,
                n: resolved,
                wins,
                losses,
                winrate_pct: wr,
                sim_pct: simPct,
                drift_pct: drift,
                pass: resolved >= job.n && wr >= args.target,
                opp_base: oppSum?.base || '',
                opp_runes: oppSum?.runes || [],
                sec,
                err: st?.err || null,
            };
            results.push(rec);
            const simTxt = simPct !== null ? ` | sim=${simPct.toFixed(1)}% (diff=${drift >= 0 ? '+' : ''}${drift.toFixed(1)}%)` : '';
            console.log(`  [F${args.fid} vs ${label}(${job.uid})] ${wins}/${resolved} (${wr.toFixed(2)}%${simTxt}) ${rec.pass ? 'PASS' : 'MISS'} in ${sec}s`);
        }

        const fState = await evalLua(`
            local tl = require "logic.troop"
            local rl = require "logic.rune"
            local sp, au, dg, df = tl:GetTroopProperty(${args.fid})
            local eq = {}
            for pos = 1, 5 do
                local r = (rl:GetRuneEquipmentList() or {})[pos]
                if r then eq[pos] = { tid = r.template_id, lv = r.level } end
            end
            return { fid = ${args.fid}, cur_fid = tl:GetCurFormationId(), weapon = tl:GetFormationWeaponId(${args.fid}), bp = tl:GetTroopBP(${args.fid}), base = { speed = sp, defense = df, dodge = dg, authority = au }, runes = eq }
        `);

        const wrs = results.map(r => r.winrate_pct);
        const report = {
            tested_at: new Date().toISOString(),
            in_flight: 1,
            fid: args.fid,
            target_pct: args.target,
            total_sec: Number(((Date.now() - tAll) / 1000).toFixed(2)),
            min_winrate_pct: wrs.length ? Math.min(...wrs) : 0,
            avg_winrate_pct: wrs.length ? Number((wrs.reduce((a, b) => a + b, 0) / wrs.length).toFixed(2)) : 0,
            all_pass: results.length > 0 && results.every(r => r.pass),
            formation_state: fState,
            results,
        };
        if (args.outPath) {
            fs.mkdirSync(path.dirname(path.resolve(args.outPath)), { recursive: true });
            fs.writeFileSync(args.outPath, JSON.stringify(report, null, 2), 'utf8');
        }
        console.log(`\nLIVE VERIFY DONE in ${report.total_sec}s | in_flight=1 | min=${report.min_winrate_pct.toFixed(2)}% avg=${report.avg_winrate_pct.toFixed(2)}% | all_pass=${report.all_pass}`);
    } finally {
        ws.close();
    }
}

main().catch(e => { console.error(e); process.exitCode = 1; });
