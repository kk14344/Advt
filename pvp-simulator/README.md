# 冒险与挖矿 · 实服真规则 PvP 模拟器

严格按测试服实测验证通过的规则构建，支持 **命令行 CLI** 与 **本地网页端（无需外网/无需额外前端依赖）**。

## 已对齐的实服核心规则

1. **同名 20 级符文可叠加**：支持 `5× 同名符文`、`3+2` 等任意组合（如 `3×防王 + 2×先王衰弱`）。
2. **合体技优先个人技判定**：合体技排在个人主动技之前，不会被个人 >100% 主动技概率池截断。
3. **封印严格大于与敌方回合计数**：`seal_cond1` 使用严格 `>` 判定（如 `>7%` 不会封印 `7%` 技能）；`seal_turn` 按敌方每次行动递减 1。
4. **GM 新增复合技与结算顺序**：完整执行 `gm_combo_effects`（先段伤害后的二段纯粹/按前段伤害吸血）、`gm_after_attack_total_heal_percent` 战后回血、`reply_after_attack` 及被动转化属性优先于符文加成。
5. **双成长档位切换**：
   - `max`：100 级 + 满觉醒（理论满配天花板，约 7,300 万战力）。
   - `server30`：主角 100 级 8 觉 + 佣兵 30 级 5 觉（对齐当前测试服 `测试09` 及好友实战面板，约 2,700 万战力）。
6. **实服获取上限校验**：自动检测 `雅典娜(22000007)`、`波塞冬(22000014)` 等唯一/限数佣兵上阵人数是否违规。

## 文件说明

* `engine.py`：核心 Lua/Python 战斗模拟与阵容解析引擎（默认从 `F:\codex1\tmp\test-server-pvp` 读取游戏表，可用环境变量 `PVP_DATA_DIR` 覆盖）
* `presets.json`：内置 12 套预设阵容（`my_best_balanced`、`my_best_speed`、`codex_original`、9 位实服强好友）
* `sim.py`：命令行工具（支持 `presets`、`find`、`info`、`vs`、`bench`、`trace`）
* `web_ui.py` + `index.html`：本地交互式可视化网页模拟器
* `test_sim.py`：规则与接口自动化测试

## 快速使用

### 1. 打开网页可视化模拟器
```powershell
cd f:\antigra\pvp-simulator
python web_ui.py
```
浏览器打开 `http://127.0.0.1:18080/`，即可直接选预设、换站位/洗练/符文/武器、切 `满配/实服档`、跑对称胜率、跑全预设基准、看单局逐回合战报或导入导出 JSON。

### 2. 命令行测试示例
```powershell
cd f:\antigra\pvp-simulator

# 列出内置预设
python sim.py presets

# 查看主推阵容在实服档（Lv30 5觉）的面板、合体技与技能池
python sim.py info my_best_balanced --mode server30

# 两套阵容对称模拟 1000 场（500 先手侧 + 500 后手侧）
python sim.py vs my_best_balanced friend_GE0Z2 -n 500

# 查看单局逐回合详细战报
python sim.py trace my_best_balanced friend_GE0Z2 --seed 20261125

# 用主推阵容跑全部 12 个预设基准
python sim.py bench my_best_balanced -n 300

# 搜索佣兵/符文/武器 ID
python sim.py find 大魔王
```
