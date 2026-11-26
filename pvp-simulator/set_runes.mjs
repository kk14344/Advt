#!/usr/bin/env node
/**
 * Safe, lossless 5-rune switcher for 冒险与挖矿 (test-server).
 * Supports Chinese shorthand & IDs:
 *   node set_runes.mjs 5x防衰
 *   node set_runes.mjs 3x王衰+2x闪王
 *   node set_runes.mjs 59,59,59,59,59
 *
 * Hard rails: auto_compose=false, NEVER calls UpgradeRune, never touches any formation.
 */

const CDP_URL = 'http://127.0.0.1:19341/json/list';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));

const RUNE_ALIASES = {
    '59': 59, '防衰': 59, '防御衰弱': 59, '防御衰减': 59,
    '60': 60, '闪衰': 60, '闪避衰弱': 60, '闪避衰减': 60,
    '61': 61, '先衰': 61, '先攻衰弱': 61, '先攻衰减': 61,
    '62': 62, '王成': 62, '王者': 62, '王者成长': 62,
    '63': 63, '王衰': 63, '王者衰弱': 63, '王者衰减': 63,
    '64': 64, '先王': 64, '先攻王者': 64,
    '65': 65, '闪王成': 65, '闪王成长': 65,
    '66': 66, '防王': 66, '防御王者': 66,
    '67': 67, '防王衰': 67, '防王衰弱': 67, '防御王者衰弱': 67,
    '68': 68, '先王衰': 68, '先王衰弱': 68,
    '69': 69, '闪王': 69, '闪王衰': 69, '闪王衰弱': 69, '闪王衰减': 69,
    '75': 75, '先防': 75, '先攻防御': 75,
};

export function parseRuneSpec(spec) {
    const out = [];
    const s = String(spec || '').trim().replace(/×/g, 'x').replace(/\*/g, 'x');
    if (!s) return out;
    for (const part of s.split(/[+,，]+/).map(x => x.trim()).filter(Boolean)) {
        const m = part.match(/^(\d+)\s*x\s*(.+)$/i) || part.match(/^(.+?)\s*x\s*(\d+)$/i);
        if (m) {
            const isFirstNum = /^\d+$/.test(m[1]) && Number(m[1]) <= 5;
            const cnt = Number(isFirstNum ? m[1] : m[2]);
            const key = (isFirstNum ? m[2] : m[1]).trim();
            const tid = RUNE_ALIASES[key] || Number(key);
            if (!tid || Number.isNaN(tid)) throw new Error(`Unknown rune alias: ${key}`);
            for (let i = 0; i < cnt; i++) out.push(tid);
        } else {
            const tid = RUNE_ALIASES[part] || Number(part);
            if (!tid || Number.isNaN(tid)) throw new Error(`Unknown rune alias: ${part}`);
            out.push(tid);
        }
    }
    return out;
}

export async function setRunesLive(spec) {
    const t0 = Date.now();
    const target = Array.isArray(spec) ? spec.map(Number) : parseRuneSpec(spec);
    if (target.length !== 5) throw new Error(`Need exactly 5 runes, got ${target.length}: ${JSON.stringify(target)}`);

    const pages = await (await fetch(CDP_URL)).json();
    const page = pages.find(p => p.type === 'page' && p.url.startsWith('https://47.101.70.170/'));
    if (!page) throw new Error('Game page not found on CDP 127.0.0.1:19341 (please open helper first)');

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

    try {
        const conn = await evalLua(`local n = require "util.network"; return n and n.IsConnected and n:IsConnected() or false`);
        if (!conn) throw new Error('Game client is disconnected from server; please reconnect in helper first.');

        const lua = `
        local rl = require "logic.rune"
        local net = require "util.network"
        local target = { ${target.join(', ')} }
        if rl.SetSelectedAutoCompose then rl:SetSelectedAutoCompose(false) end

        if not _G.__ANTIGRA_ORIG_DISPATCH then _G.__ANTIGRA_ORIG_DISPATCH = net.event_listener.Dispatch end
        if rl.QueryRuneLoadouts and not _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS then _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS = rl.QueryRuneLoadouts end
        local orig = _G.__ANTIGRA_ORIG_DISPATCH
        local orig_ql = _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS
        if orig_ql then rl.QueryRuneLoadouts = function(self) self.rune_query_pending = false; self.rune_query_needed = false; return false end end

        local ST = { done = false, ok = false, err = nil, exchanges = 0, equips = 0, retries = 0, inflight = nil, ts = 0, logs = {} }
        _G.__SET_RUNES = ST

        local function restore()
            net.event_listener.Dispatch = orig
            if orig_ql then rl.QueryRuneLoadouts = orig_ql end
        end
        local function finish_ok()
            ST.done = true; ST.ok = true
            restore()
            if orig_ql then pcall(function() rl:QueryRuneLoadouts() end) end
        end
        local function finish_err(msg)
            ST.done = true; ST.ok = false; ST.err = msg
            restore()
        end

        local function step()
            if ST.done or ST.inflight then return end
            if rl.SetSelectedAutoCompose then rl:SetSelectedAutoCompose(false) end
            rl.rune_query_pending = false; rl.rune_query_needed = false; rl.rune_native_pending = 0
            local all_runes = rl:GetRuneList() or {}
            if #all_runes == 0 and not ST.queried then
                ST.queried = true; ST.inflight = "query"; ST.ts = os.clock()
                net:Send({ query_rune_info = {} })
                return
            end
            local eq = rl:GetRuneEquipmentList() or {}
            local need = {}
            for _, tid in ipairs(target) do need[tid] = (need[tid] or 0) + 1 end

            local chosen, used = {}, {}
            for pos = 1, 5 do
                local r = eq[pos]
                if r and (r.level or 0) >= 20 and (need[r.template_id] or 0) > 0 and not used[r.rune_id] then
                    chosen[pos] = r; used[r.rune_id] = true; need[r.template_id] = need[r.template_id] - 1
                end
            end
            local rem = {}
            for _, tid in ipairs(target) do
                if (need[tid] or 0) > 0 then rem[#rem + 1] = tid; need[tid] = need[tid] - 1 end
            end
            local ri, slot_want = 1, {}
            for pos = 1, 5 do
                if not chosen[pos] then slot_want[pos] = rem[ri]; ri = ri + 1 end
            end
            for pos = 1, 5 do
                if not chosen[pos] and slot_want[pos] then
                    for _, r in ipairs(all_runes) do
                        if r.template_id == slot_want[pos] and (r.level or 0) >= 20 and not used[r.rune_id] then
                            chosen[pos] = r; used[r.rune_id] = true; break
                        end
                    end
                end
            end
            for pos = 1, 5 do
                if not chosen[pos] and slot_want[pos] then
                    local want_tid = slot_want[pos]
                    local low = nil
                    for _, r in ipairs(all_runes) do
                        if r.template_id == want_tid and not used[r.rune_id] then
                            if not low or (r.level or 0) > (low.level or 0) then low = r end
                        end
                    end
                    if not low then return finish_err("Bag has insufficient copies of rune tid=" .. tostring(want_tid)) end
                    local donor = nil
                    for _, r in ipairs(all_runes) do
                        if (r.level or 0) >= 20 and not used[r.rune_id] and r.rune_id ~= low.rune_id then
                            if not donor or ((r.equip_pos or 0) == 0 and (donor.equip_pos or 0) ~= 0) then donor = r end
                        end
                    end
                    if not donor then return finish_err("No Lv20 donor rune available for ExchangeRuneProperty") end
                    ST.inflight = "exch"; ST.ts = os.clock(); ST.exchanges = ST.exchanges + 1
                    ST.logs[#ST.logs + 1] = string.format("ExchangeRuneProperty: tid=%d(rid=%s lv%d) <-> donor tid=%d(rid=%s lv%d)",
                        low.template_id, tostring(low.rune_id), low.level or 0, donor.template_id, tostring(donor.rune_id), donor.level or 0)
                    rl:ExchangeRuneProperty(low.rune_id, donor.rune_id)
                    return
                end
            end
            for pos = 1, 5 do
                local want_r = chosen[pos]
                local cur_r = eq[pos]
                if not cur_r or cur_r.rune_id ~= want_r.rune_id then
                    ST.inflight = "equip"; ST.ts = os.clock(); ST.equips = ST.equips + 1
                    rl:EquipRune(pos, want_r)
                    return
                end
            end
            finish_ok()
        end
        _G.__SET_RUNES_STEP = step

        net.event_listener.Dispatch = function(self, ev, msg, ...)
            local r1, r2, r3 = orig(self, ev, msg, ...)
            if not ST.done and (ev == "rune_exchange_ret" or ev == "equip_rune_ret" or ev == "query_rune_info_ret") then
                ST.inflight = nil
                local res = type(msg) == "table" and msg.result or "success"
                if ev ~= "query_rune_info_ret" and res ~= "success" and res ~= "" then
                    finish_err(ev .. " failed: " .. tostring(res))
                else
                    pcall(step)
                end
            end
            return r1, r2, r3
        end
        step()
        return true
        `;

        await evalLua(lua);
        let st = null;
        for (let i = 0; i < 250; i++) {
            await sleep(40);
            st = await evalLua(`
                local st = _G.__SET_RUNES
                if st and not st.done and st.inflight and (os.clock() - st.ts) > 1.2 then
                    st.retries = (st.retries or 0) + 1
                    if st.retries > 4 then
                        st.done = true; st.ok = false; st.err = "Server did not reply to " .. tostring(st.inflight)
                    else
                        st.inflight = nil
                        if _G.__SET_RUNES_STEP then pcall(_G.__SET_RUNES_STEP) end
                    end
                end
                return st
            `);
            if (st && st.done) break;
        }
        await evalLua(`if _G.__ANTIGRA_ORIG_DISPATCH then require("util.network").event_listener.Dispatch = _G.__ANTIGRA_ORIG_DISPATCH end; if _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS then require("logic.rune").QueryRuneLoadouts = _G.__ANTIGRA_ORIG_QUERY_RUNE_LOADOUTS end; return true`);

        const eq = await evalLua(`
            local rl = require "logic.rune"
            local cm = require "logic.config_manager"
            local out = {}
            for pos = 1, 5 do
                local r = (rl:GetRuneEquipmentList() or {})[pos]
                if r then
                    local cfg = cm.rune_config and cm.rune_config[r.template_id]
                    out[pos] = { pos = pos, tid = r.template_id, lv = r.level, name = cfg and cfg.name or tostring(r.template_id) }
                end
            end
            return out
        `);
        if (!st || !st.ok) throw new Error(st?.err || 'Timeout switching runes');
        const sec = ((Date.now() - t0) / 1000).toFixed(2);
        for (const l of Object.values(st.logs || {})) console.log('  ' + l);
        const list = Object.values(eq || {}).map(r => `${r.name}(${r.tid})@Lv${r.lv}`).join(', ');
        console.log(`RUNES UPDATED in ${sec}s (exchanges=${st.exchanges}, equips=${st.equips}): [${list}]`);
        return { sec: Number(sec), eq };
    } finally {
        ws.close();
    }
}

if (import.meta.url === `file:///${process.argv[1].replace(/\\/g, '/')}` || process.argv[1]?.endsWith('set_runes.mjs')) {
    const spec = process.argv.slice(2).join(' ');
    if (!spec) {
        console.error('Usage: node set_runes.mjs <5x防衰 | 3x王衰+2x闪王 | 59,59,59,59,59>');
        process.exit(2);
    }
    setRunesLive(spec).catch(e => { console.error(e.message || e); process.exitCode = 1; });
}
