from pathlib import Path
import json
import subprocess
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
if hasattr(sys.stderr, 'reconfigure'):
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')

root = Path(__file__).resolve().parents[1]
src = root / 'src'


def lua_value(value):
    if isinstance(value, dict):
        return '{' + ','.join('[' + lua_value(int(k) if str(k).isdigit() else k) + ']=' + lua_value(v) for k, v in value.items()) + '}'
    if isinstance(value, list):
        return '{' + ','.join(map(lua_value, value)) + '}'
    if value is True:
        return 'true'
    if value is False:
        return 'false'
    if value is None:
        return 'nil'
    return json.dumps(value, ensure_ascii=False)


def build_edition(edition: str, lua_only: bool = False):
    test_pvp = edition == 'testpvp'
    low_load = edition == 'lowload'

    guide = json.loads((src / 'guide-plans.json').read_text(encoding='utf-8'))
    tasks = (src / 'tasks.lua').read_text(encoding='utf-8').replace('-- @GUIDE@', lua_value(guide))
    tasks = tasks.replace('-- @BATTLE_REPLAY@', (src / 'battle_replay.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @DAILY@', (src / 'daily.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @MINE@', (src / 'research_mine.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @WORKSHOP@', (src / 'workshop_guild.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @GROWTH@', (src / 'growth.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @FAST_TASKS@', (src / 'fast_tasks.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @REFILL@', (src / 'refill.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @MINING_RUNTIME@', (src / 'mining_runtime.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @MINING_RESET@', (src / 'mining_reset.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @MINING_ROUTE@', (src / 'mining_route.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @MINING_CAMERA@', (src / 'mining_camera.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @VOID_PLANNER@', (src / 'void_planner.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @VOID_ROUTE@', (src / 'void_route.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @RUNE_LOOP@', (src / 'rune_loop.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @DAILY_TAIL@', (src / 'daily_tail.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @DAILY_COMPLETION@', (src / 'daily_completion.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @MYSTERY_SHOP@', (src / 'mystery_shop.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @RUNE_RECOVERY@', (src / 'rune_recovery.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @ORE_PACKS@', (src / 'ore_packs.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @ADVENTURE_PUSH@', (src / 'adventure_push.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @DAILY_CART@', (src / 'daily_cart.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @REQUEST_RETRY@', (src / 'request_retry.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @TEST_MERC@', (src / 'test_mercenary.lua').read_text(encoding='utf-8') if test_pvp else '')

    lua = (src / 'automation.lua').read_text(encoding='utf-8').replace('-- @TASKS@', tasks)
    if test_pvp:
        for old, new in [
            ('if __MWA and __MWA.VERSION == "1.1.0" then', 'if __MWA and __MWA.VERSION == "1.1.0-testpvp" then'),
            ('VERSION = "1.1.0",', 'VERSION = "1.1.0-testpvp",'),
        ]:
            if old not in lua:
                raise SystemExit(f'Missing test-pvp Lua patch target: {old}')
            lua = lua.replace(old, new, 1)
    if low_load:
        for old, new in [
            ('actionInterval = 0.03,', 'actionInterval = 0.80,'),
            ('refillInterval = 0,', 'refillInterval = 0.45,'),
        ]:
            if old not in lua:
                raise SystemExit(f'Missing low-load Lua patch target: {old}')
            lua = lua.replace(old, new, 1)

    bridge = (src / 'bridge.js').read_text(encoding='utf-8')
    if test_pvp:
        for old, new in [
            ("window.__MWA_APP_VERSION === '1.1.0'", "window.__MWA_APP_VERSION === '1.1.0-testpvp'"),
            ("var VERSION = '1.1.0';", "var VERSION = '1.1.0-testpvp';"),
            ('var CORE_TICK_MIN_MS = 40;', 'var CORE_TICK_MIN_MS = 10;'),
            ('var CORE_TICK_INTERVAL_MS = 45;', 'var CORE_TICK_INTERVAL_MS = 15;'),
            ('var BATTLE_SKIP_INTERVAL_MS = 20;', 'var BATTLE_SKIP_INTERVAL_MS = 10;'),
        ]:
            if old not in bridge:
                raise SystemExit(f'Missing test-pvp JS patch target: {old}')
            bridge = bridge.replace(old, new, 1)
    elif low_load:
        for old, new in [
            ("window.__MWA_APP_VERSION === '1.1.0'", "window.__MWA_APP_VERSION === '1.1.0-lowload'"),
            ("var VERSION = '1.1.0';", "var VERSION = '1.1.0-lowload';"),
            ('var CORE_TICK_MIN_MS = 40;', 'var CORE_TICK_MIN_MS = 110;'),
            ('var CORE_TICK_INTERVAL_MS = 45;', 'var CORE_TICK_INTERVAL_MS = 120;'),
            ('var BATTLE_SKIP_INTERVAL_MS = 20;', 'var BATTLE_SKIP_INTERVAL_MS = 50;'),
        ]:
            if old not in bridge:
                raise SystemExit(f'Missing low-load JS patch target: {old}')
            bridge = bridge.replace(old, new, 1)

    js = bridge.replace('/* @LUA_CORE@ */', json.dumps(lua, ensure_ascii=False))
    js = (src / 'background_guard.js').read_text(encoding='utf-8') + '\n' + js
    (root / 'dist').mkdir(exist_ok=True)
    if test_pvp:
        inject_path = root / 'dist/AandmAuto.testpvp.inject.js'
        inject_path.write_text(js, encoding='utf-8')
        (root / 'dist/automation.testpvp.generated.lua').write_text(lua, encoding='utf-8')
        (root / 'dist/tasks.testpvp.generated.lua').write_text(tasks, encoding='utf-8')
    elif low_load:
        inject_path = root / 'dist/AandmAuto.lowload.inject.js'
        inject_path.write_text(js, encoding='utf-8')
        (root / 'dist/automation.lowload.generated.lua').write_text(lua, encoding='utf-8')
        (root / 'dist/tasks.lowload.generated.lua').write_text(tasks, encoding='utf-8')
    else:
        inject_path = src / 'AandmAuto.inject.js'
        inject_path.write_text(js, encoding='utf-8')
        (root / 'dist/automation.generated.lua').write_text(lua, encoding='utf-8')
        (root / 'dist/tasks.generated.lua').write_text(tasks, encoding='utf-8')

    if lua_only:
        print(f'Generated Lua only ({edition})')
        return None

    compiler = Path(r'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe')
    out_exe = (
        root / 'dist/冒险与挖矿自动助手-测试服PvP版.exe'
        if test_pvp
        else (root / 'dist/冒险与挖矿自动助手-低负载版.exe' if low_load else root / 'dist/冒险与挖矿自动助手-优化版.exe')
    )
    args = [
        str(compiler), '/nologo', '/codepage:65001', '/target:winexe', '/platform:x64', '/optimize+', '/unsafe',
        '/out:' + str(out_exe),
        '/win32icon:' + str(src / 'app.ico'),
        '/win32manifest:' + str(src / 'app.manifest'),
    ]
    if test_pvp:
        args.append('/define:TEST_PVP_EDITION')
    elif low_load:
        args.append('/define:LOW_LOAD_EDITION')

    args += ['/reference:' + s for s in [
        'System.dll', 'System.Core.dll', 'System.Drawing.dll',
        'System.Windows.Forms.dll', 'System.Web.Extensions.dll', 'System.Security.dll',
        'System.IO.Compression.dll',
    ]]
    args += ['/reference:' + str(root / 'lib' / s) for s in [
        'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll',
    ]]
    args.append('/resource:' + str(inject_path) + ',AandmAuto.inject.js')
    args.append('/resource:' + str(src / 'update-config.json') + ',AandmAuto.update-config.json')
    if test_pvp:
        args.append('/resource:' + str(root / 'pvp-simulator/lua_auto_deploy.lua') + ',AandmAuto.lua_auto_deploy.lua')
        args.append('/resource:' + str(root / 'pvp-simulator/presets.json') + ',AandmAuto.presets.json')
    for name in [
        'AandmAuto.icon.ico', 'AandmAuto.icon.png',
        'AandmAuto.WebView2.Core.dll', 'AandmAuto.WebView2.WinForms.dll', 'AandmAuto.WebView2Loader.dll',
    ]:
        args.append('/resource:' + str(src / name) + ',' + name)
    args += [
        '/resource:' + str(src / 'mine.png') + ',AandmAuto.mine.png',
        '/resource:' + str(src / 'background_guard.js') + ',AandmAuto.background.js',
        '/resource:' + str(src / 'fonts/general.ttf') + ',AandmAuto.fonts.general.ttf',
        '/resource:' + str(root / 'original/void-guide.txt') + ',AandmAuto.guide.txt',
    ]
    for name in [
        'buttonbg_1.png', 'buttonbg_2.png', 'buttonbg_3.png', 'buttonbg_4.png', 'buttonbg_5.png', 'buttonbg_7.png', 'buttonbg_12.png',
        'game-headerbar.png', 'game-maintitle_bg.png', 'game-box_bg.png', 'game-ground.png', 'game-cave-bg.png',
        'game-paper_bg1.png', 'game-paper_bg4_d.png', 'game-paper_bg7_d.png', 'game-top_tab.png',
        'game-mainui_rune.png', 'game-mainui_setting.png', 'game-decide_yes.png', 'game-rewardbtn_bg.png',
    ]:
        args.append('/resource:' + str(src / 'art' / name) + ',AandmAuto.art.' + name)
    for name in [
        'game-buttonbg_1.png', 'game-buttonbg_2.png', 'game-buttonbg_3.png', 'game-buttonbg_4.png',
        'game-buttonbg_5.png', 'game-buttonbg_6.png', 'game-buttonbg_7.png', 'game-buttonbg_8.png',
        'game-buttonbg_9.png', 'game-buttonbg_10.png', 'game-buttonbg_11.png', 'game-buttonbg_12.png',
    ]:
        args.append('/resource:' + str(src / 'art' / name) + ',AandmAuto.art.' + name)
    args += [str(p) for p in sorted((src / 'AandmAuto').glob('*.cs'))]
    args.append(str(src / 'Properties/AssemblyInfo.cs'))
    subprocess.run(args, check=True)
    print('Built:', out_exe)
    return out_exe


def main():
    lua_only = '--lua-only' in sys.argv
    if '--all' in sys.argv:
        build_edition('official', lua_only=lua_only)
        build_edition('testpvp', lua_only=lua_only)
    elif '--test-pvp' in sys.argv:
        build_edition('testpvp', lua_only=lua_only)
    elif '--low-load' in sys.argv:
        build_edition('lowload', lua_only=lua_only)
    else:
        build_edition('official', lua_only=lua_only)


if __name__ == '__main__':
    main()
