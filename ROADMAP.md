# 冒险与挖矿自动助手 · 维护与开发路线图（Roadmap）

本文档记录正式服优化版（`official`）与测试服 PvP 专项版（`testpvp`）的统一维护策略与分阶段迭代规划。

---

## 一、核心原则与产品边界

1. **双线分工、单仓共治**
   - 统一维护在 `main` 主干，通过 `#if TEST_PVP_EDITION` 编译宏隔离差异，独立通道发布（`official-v*` / `testpvp-v*`），互不串台。
   - **正式服优化版**：聚焦长期挂机零卡死、日常/续跑稳定性与低售后成本。
   - **测试服 PvP 专项版**：聚焦一图流拖图换阵、无损秒切符文、80 场实测、配队优化与新佣兵快速适配。
2. **目标定位（亲友 / 公会小圈子实用向）**
   - 保持 .NET Framework 4.6.2 单文件（~6MB）零运行库双击即用的核心优势，不做迁移 .NET 8 / Electron 等会导致体积暴涨的推倒重来。
   - 大文件（`MainForm.cs`、`MainForm.TestPvp.cs`、`automation.lua`、`daily.lua`）随功能迭代增量拆分。
3. **冻结范围**
   - 历史手机版脚本（`assistant-mobile.js`、`automation-mobile.lua`）与低负载版宏（`LOW_LOAD_EDITION`）冻结归档，不再投入新功能维护。
4. **面向 Cocos 引擎切换的轻防御纪律**
   - 后续新增能力严格遵守 **C# 壳 ↔ `window.__MWA_APP` 标准 JS 桥接契约**，避免在 C# 层新增硬编码裸 Lua 片段，为未来引擎切换保留 95%+ 桌面壳复用率。

---

## 二、分阶段里程碑

### P0 · 发布与 CI 地基加固（当前执行）
- [x] 双端版本号统一归 `1.0.0`，接入非打扰检查更新（启动静默写 1 行日志、设置页按需一键更新、SHA-256 强制校验、原地替换重启）。
- [x] 修复 [test_tasks.cjs](tools/test_tasks.cjs) 在 Node 24 下的沙箱兼容问题，恢复全量 Lua 回归测试。
- [x] 加固 [.github/workflows/release.yml](.github/workflows/release.yml)（串行并发锁、多行日志输出、`gh release view` 状态判定、干净回写 `updates.json`、版本校验）。
- [x] 新增 [.github/workflows/ci.yml](.github/workflows/ci.yml)：每次 push/PR 自动在 `windows-latest` 执行双端编译、内嵌资源校验、更新器端到端烟测、Lua 回归与后台保活测试。
- [x] 在 GitHub Actions 手动/重推触发 `v1.0.0` 双端正式 Release 上线验收（`Dual-Edition CI`、`official-v1.0.0`、`testpvp-v1.0.0` 三连绿）。

### P1 · 低售后分发体验升级（v1.0.x）
- [x] **更新镜像列表远程热下发**：允许 `updates.json` 下发并合并最新可用加速镜像列表（并缓存至本地 `cached-mirrors.json`），避免旧版 `.exe` 内置公共代理失效后无法更新。
- [x] **设置页「一键导出脱敏诊断包」**：一键打包助手版本号、冒险日志、启动异常日志、脱敏配置与近期审计摘要为 zip，**严格白名单过滤** `account.dat`、Cookie、WebView2 缓存并脱敏用户名/Token，已通过双端隐私烟测。

### P2 · 双端核心业务能力迭代（v1.1.x）
- **正式服优化版**
  - [ ] 挂机异常自愈加固：断线重连检测、卡弹窗自动清理、WebView2 渲染进程异常崩溃自动恢复续跑（不做配置导入导出）。
- **测试服 PvP 专项版**
  - [ ] 预设阵型库随在线更新同步（支持云端热更 `presets.json` 无需重发整包）。
  - [ ] 多对手批量实测胜率汇总表（一键对多个目标跑 80 场并输出直观胜率矩阵）。
  - [ ] 新版本佣兵/技能上线快速校验与同步工具链。

### P3 · Cocos Creator 3.x 新客户端双引擎适配（待官方放出 H5 网页入口后启动）
> 已通过测试服 APK（`mxywk-v1.0.1-vc2.apk.1`）实证：新客户端为 **Cocos Creator 3.x**（285 个明文 TS 编译模块 `System.register("chunks:///_virtual/*.ts")`），原生内置 `aandm.h5.*` 网页端支持，且后端核心协议（`dig_block`、`equip_rune`、`query_rune`、`resource_recycle`、`escort_`、`guild_boss` 等）与老端高度一致。
- [ ] 基于 `Wire/State` 模块实现独立 `bridge-cocos.js` 与 `background_guard_cocos.js`，对外暴露完全一致的 `window.__MWA_APP` 契约。
- [ ] C# 宿主壳根据网页运行时自动识别老 Lua VM 端与新 Cocos Creator 端，实现零感切换。
