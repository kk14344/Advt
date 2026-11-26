#!/usr/bin/env node
/**
 * Unified flexible CLI & end-to-end orchestrator for 冒险与挖矿 PvP (test-server).
 *
 * 6 atomic micro-commands + 1 end-to-end auto-correct pipeline:
 *   node maowa.mjs sync   [--opps 02,03,15,16]
 *   node maowa.mjs opt    --opps 15 [--objective floor|avg] [--runes bag|keep|all] [--max-changes 3] [--lock 红美铃] [--save key]
 *   node maowa.mjs deploy <preset | --from-image file.png> [--fid 5] [--no-runes] [--dry-run]
 *   node maowa.mjs runes  <5x防衰 | 3x王衰+2x闪王 | 59,59,59,59,59>
 *   node maowa.mjs test   --fid 5 --opps 02:80,03:80,15:80,16:80 [--target 70] [--out report.json]
 *   node maowa.mjs card   <preset> [--live-report report.json] [-o out.png]
 *   node maowa.mjs run    --opps 02,03,15,16 --target 70 [--fid 5] [--runes bag] [--save key] [--yituliu]
 */

import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { setRunesLive } from './set_runes.mjs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const PRESETS_PATH = path.join(HERE, 'presets.json');

const OPP_TO_UID = {
    '02': '73ID6', '测试02': '73ID6', 'friend_73ID6': '73ID6', '73ID6': '73ID6',
    '03': 'GE0Z2', '测试03': 'GE0Z2', 'friend_GE0Z2': 'GE0Z2', 'GE0Z2': 'GE0Z2',
    '04': 'DAFKJ', '测试04': 'DAFKJ', 'friend_DAFKJ': 'DAFKJ', 'DAFKJ': 'DAFKJ',
    '08': '70SXF', '测试08': '70SXF', 'friend_70SXF': '70SXF', '70SXF': '70SXF',
    '15': '3YPN0', '测试15': '3YPN0', 'friend_3YPN0': '3YPN0', '3YPN0': '3YPN0',
    '16': 'NZVHY', '测试16': 'NZVHY', 'friend_NZVHY': 'NZVHY', 'NZVHY': 'NZVHY',
    '18': 'P0PLM', '测试18': 'P0PLM', 'friend_P0PLM': 'P0PLM', 'P0PLM': 'P0PLM',
};

function toUid(tok) {
    const t = tok.trim();
    return OPP_TO_UID[t] || t.replace(/^friend_/, '');
}

function runCmd(bin, args) {
    const r = spawnSync(bin, args, { stdio: 'inherit', cwd: HERE });
    if (r.status !== 0) process.exit(r.status || 1);
}

/** Extract embedded maowa_formation_v1 JSON from a PNG tEXt chunk in <1ms (zero OCR error!). */
function extractFormationFromPng(pngPath) {
    const buf = fs.readFileSync(pngPath);
    if (buf.subarray(0, 8).toString('hex') !== '89504e470d0a1a0a') {
        throw new Error(`Not a PNG file: ${pngPath}`);
    }
    let pos = 8;
    while (pos + 8 <= buf.length) {
        const len = buf.readUInt32BE(pos);
        const type = buf.subarray(pos + 4, pos + 8).toString('ascii');
        const data = buf.subarray(pos + 8, pos + 8 + len);
        if (type === 'tEXt') {
            const nul = data.indexOf(0);
            if (nul > 0) {
                const key = data.subarray(0, nul).toString('latin1');
                if (key === 'maowa_formation_v1') {
                    const text = data.subarray(nul + 1).toString('utf8');
                    return JSON.parse(text);
                }
            }
        }
        pos += 12 + len;
    }
    throw new Error(`PNG does not contain embedded maowa_formation_v1 metadata: ${pngPath}`);
}

function parseFlags(argv) {
    const pos = [];
    const flags = {};
    for (let i = 0; i < argv.length; i++) {
        const a = argv[i];
        if (a.startsWith('--')) {
            const k = a.slice(2);
            const next = argv[i + 1];
            if (next !== undefined && !next.startsWith('--')) {
                flags[k] = next;
                i++;
            } else {
                flags[k] = true;
            }
        } else if (a === '-o' && argv[i + 1]) {
            flags.output = argv[++i];
        } else {
            pos.push(a);
        }
    }
    return { pos, flags };
}

async function main() {
    const [cmd, ...rest] = process.argv.slice(2);
    if (!cmd || cmd === '-h' || cmd === '--help') {
        console.log([
            'Usage: node maowa.mjs <sync|opt|deploy|runes|test|card|matrix|sweep|run> [options]',
            '  sync   [--opps 02,03,15,16]',
            '  opt    --opps 15 [--mode server30|max|fair_eq] [--q7-c2 0] [--objective floor|avg] [--runes bag|keep|all] [--save key]',
            '  deploy <preset | --from-image file.png> [--fid 5] [--no-runes] [--dry-run]',
            '  runes  <5x防衰 | 3x王衰+2x闪王 | 59,59,59,59,59>',
            '  test   --fid 5 --opps 02:80,03:80,15:80,16:80 [--target 70] [--out file.json]',
            '  card   <preset> [--mode server30|fair_eq] [--q7-c2 0] [--live-report file.json] [-o out.png]',
            '  matrix [--mode fair_eq] [--teams a,b,c] [--q7-c2 0] [--equalize-bp] [--no-runes] [-n 250]',
            '  sweep  <team1> [team2...] [--mode fair_eq] [--param q7_c2|artifact_lv|rune_lv|merc_lv|bp_mode] [--values 0,2,4,10]',
            '  run    --opps 02,03,15,16 --target 70 [--fid 5] [--runes bag] [--save key] [--yituliu]',
        ].join('\n'));
        process.exit(0);
    }

    const { pos, flags } = parseFlags(rest);
    const appendFairFlags = (args) => {
        for (const k of ['q7-c2', 'artifact-lv', 'rune-lv', 'merc-lv', 'merc-wake', 'leader-contract', 'mirror-runes']) {
            if (flags[k] !== undefined && flags[k] !== true) args.push(`--${k}`, String(flags[k]));
        }
        if (flags['equalize-bp']) args.push('--equalize-bp');
        if (flags['no-runes']) args.push('--no-runes');
    };

    if (cmd === 'sync') {
        const args = [path.join(HERE, 'live_sync.mjs')];
        if (flags.opps) {
            const uids = flags.opps.split(',').map(toUid).filter(Boolean).join(',');
            args.push('--opps', uids);
        }
        if (flags['no-battle-probe']) args.push('--no-battle-probe');
        return runCmd('node', args);
    }

    if (cmd === 'opt') {
        const args = [path.join(HERE, 'fast_opt.py')];
        for (const k of ['opps', 'mode', 'objective', 'runes', 'base', 'max-changes', 'lock', 'ban', 'target', 'iters', 'workers', 'save', 'title', 'out']) {
            if (flags[k] !== undefined && flags[k] !== true) args.push(`--${k}`, String(flags[k]));
        }
        appendFairFlags(args);
        return runCmd('python', args);
    }

    if (cmd === 'deploy') {
        let presetKey = pos[0];
        if (flags['from-image']) {
            const payload = extractFormationFromPng(flags['from-image']);
            const presets = JSON.parse(fs.readFileSync(PRESETS_PATH, 'utf8'));
            presetKey = flags.save || '_tmp_from_image';
            presets[presetKey] = {
                name: payload.name || path.basename(flags['from-image'], '.png'),
                weapon: payload.weapon,
                weapon_id: payload.weapon,
                runes: payload.runes,
                slots: payload.slots,
                note: `Imported from PNG ${path.basename(flags['from-image'])}`,
            };
            fs.writeFileSync(PRESETS_PATH, JSON.stringify(presets, null, 2), 'utf8');
            console.log(`Parsed exact formation from PNG (<1ms): ${presets[presetKey].name} (W${payload.weapon}, ${payload.slots.length} slots)`);
        }
        if (!presetKey) throw new Error('deploy requires <preset> or --from-image <png>');
        const args = [path.join(HERE, 'deploy_formation.mjs'), presetKey, '--fid', String(flags.fid || 5), '--full-auto', '--keep-open'];
        if (flags['no-runes']) args.push('--no-runes');
        if (flags['dry-run']) args.push('--dry-run');
        return runCmd('node', args);
    }

    if (cmd === 'runes') {
        const spec = pos.join(' ') || flags.spec;
        await setRunesLive(spec);
        return;
    }

    if (cmd === 'test') {
        const oppsNorm = String(flags.opps || '').split(',').map(s => {
            const [tok, n] = s.split(':');
            return n ? `${toUid(tok)}:${n}` : toUid(tok);
        }).filter(Boolean).join(',');
        const args = [path.join(HERE, 'live_battle.mjs'), '--fid', String(flags.fid || 5), '--opps', oppsNorm];
        if (flags.sim) args.push('--sim', flags.sim);
        if (flags.target) args.push('--target', String(flags.target));
        if (flags.out) args.push('--out', flags.out);
        return runCmd('node', args);
    }

    if (cmd === 'card') {
        const preset = pos[0];
        if (!preset) throw new Error('card requires <preset>');
        const args = [path.join(HERE, 'sim.py'), 'export', preset, '--mode', String(flags.mode || 'server30')];
        if (flags['live-report']) args.push('--live-report', flags['live-report']);
        if (flags.output) args.push('-o', flags.output);
        if (flags.subtitle) args.push('--subtitle', flags.subtitle);
        appendFairFlags(args);
        return runCmd('python', args);
    }

    if (cmd === 'matrix') {
        const args = [path.join(HERE, 'sim.py'), 'matrix', '--mode', String(flags.mode || 'fair_eq')];
        for (const k of ['teams', 'group', 'n', 'workers', 'seed', 'out']) {
            if (flags[k] !== undefined && flags[k] !== true) args.push(`--${k}`, String(flags[k]));
        }
        appendFairFlags(args);
        return runCmd('python', args);
    }

    if (cmd === 'sweep') {
        if (!pos.length) throw new Error('sweep requires one or more <team> arguments');
        const args = [path.join(HERE, 'sim.py'), 'sweep', ...pos, '--mode', String(flags.mode || 'fair_eq')];
        for (const k of ['vs', 'param', 'values', 'n', 'workers', 'seed']) {
            if (flags[k] !== undefined && flags[k] !== true) args.push(`--${k}`, String(flags[k]));
        }
        appendFairFlags(args);
        return runCmd('python', args);
    }

    if (cmd === 'run') {
        if (!flags.opps) throw new Error('run requires --opps (e.g. --opps 02,03,15,16 or --opps 15)');
        const uids = flags.opps.split(',').map(toUid).filter(Boolean);
        const fid = Number(flags.fid || 5);
        const target = Number(flags.target || 70);
        const saveKey = flags.save || `auto_${uids.join('_').toLowerCase()}`;
        const nEach = Number(flags.n || (uids.length === 1 ? 100 : 80));
        const optJson = path.join(HERE, 'exports', `${saveKey}_opt.json`);
        const repJson = path.join(HERE, 'exports', `${saveKey}_live_report.json`);
        const outPng = flags.output || path.join(HERE, 'exports', `${saveKey}_yituliu_server30.png`);

        for (let round = 1; round <= 2; round++) {
            console.log(`\n========== MAOWA RUN ROUND ${round}/2 (opps=${uids.join(',')}, target=${target}%, F${fid}) ==========`);
            // 1. Live sync
            runCmd('node', [path.join(HERE, 'live_sync.mjs'), '--opps', uids.join(',')]);
            // 2. Multi-core optimize
            const optArgs = [
                path.join(HERE, 'fast_opt.py'),
                '--opps', uids.join(','),
                '--objective', String(flags.objective || 'floor'),
                '--runes', String(flags.runes || 'bag'),
                '--target', String(target),
                '--save', saveKey,
                '--out', optJson,
            ];
            if (flags.base) optArgs.push('--base', flags.base);
            if (flags['max-changes']) optArgs.push('--max-changes', String(flags['max-changes']));
            if (flags.lock) optArgs.push('--lock', flags.lock);
            runCmd('python', optArgs);

            const optRes = JSON.parse(fs.readFileSync(optJson, 'utf8'));
            const simPairs = uids.map(u => `${u}:${((optRes.sim[`friend_${u}`] || 0) * 100).toFixed(2)}`).join(',');

            // 3. Deploy to target fid
            const depArgs = [path.join(HERE, 'deploy_formation.mjs'), saveKey, '--fid', String(fid), '--full-auto', '--keep-open'];
            if (flags.runes === 'keep' || flags['no-runes']) depArgs.push('--no-runes');
            runCmd('node', depArgs);

            // 4. Live battle verify (strict in_flight=1)
            const oppsBattle = uids.map(u => `${u}:${nEach}`).join(',');
            runCmd('node', [
                path.join(HERE, 'live_battle.mjs'),
                '--fid', String(fid),
                '--opps', oppsBattle,
                '--sim', simPairs,
                '--target', String(target),
                '--out', repJson,
            ]);

            const rep = JSON.parse(fs.readFileSync(repJson, 'utf8'));
            const bigDrift = (rep.results || []).some(r => r.drift_pct !== null && Math.abs(r.drift_pct) > 10);
            if (rep.all_pass && !bigDrift) {
                console.log(`\n[PASS] Round ${round} verified! Exporting 一图流...`);
                break;
            }
            if (round === 1) {
                console.log(`\n[AUTO-CORRECT] Round 1 missed target (${rep.min_winrate_pct}%) or drift>10%; re-syncing & re-optimizing Round 2...`);
            }
        }

        // 5. Export 一图流 PNG
        runCmd('python', [
            path.join(HERE, 'sim.py'), 'export', saveKey,
            '--mode', 'server30',
            '--live-report', repJson,
            '-o', outPng,
        ]);
        return;
    }

    throw new Error(`Unknown subcommand: ${cmd}`);
}

main().catch(e => { console.error(e.message || e); process.exitCode = 1; });
