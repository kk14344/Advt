from pathlib import Path
import json, re, sys

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


def build():
    guide = json.loads((src / 'guide-plans.json').read_text(encoding='utf-8'))
    tasks = (src / 'tasks.lua').read_text(encoding='utf-8').replace('-- @GUIDE@', lua_value(guide))
    tasks = tasks.replace('-- @DAILY@', (src / 'daily.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @MINE@', (src / 'research_mine.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @WORKSHOP@', (src / 'workshop_guild.lua').read_text(encoding='utf-8'))
    tasks = tasks.replace('-- @GROWTH@', (src / 'growth.lua').read_text(encoding='utf-8'))
    lua = (src / 'automation-mobile.lua').read_text(encoding='utf-8').replace('-- @TASKS@', tasks)

    js = (src / 'assistant-mobile.js').read_text(encoding='utf-8')
    js = js.replace('/* @LUA_CORE@ */', json.dumps(lua, ensure_ascii=False))

    (root / 'dist').mkdir(exist_ok=True)
    out = root / 'dist' / 'aandm-assistant-mobile.js'
    out.write_text(js, encoding='utf-8')

    # 同步产出纯 Lua 核心（供校验与以后打包进壳用）
    (root / 'dist' / 'assistant-mobile.generated.lua').write_text(lua, encoding='utf-8')
    print('Built:', out, out.stat().st_size, 'bytes')
    return lua


if __name__ == '__main__':
    build()
