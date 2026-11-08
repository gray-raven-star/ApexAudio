# 官方适配包源码

每个游戏目录保存当前定义和构建配方。单独发布的适配包在表中链接到各自的发布页。早期版本可从 [2026.10.18 发布附件](https://github.com/gray-raven-star/ApexAudio/releases/tag/bundle-2026.10.18) 下载，当前下载列表见 [官方适配包下载目录](https://github.com/gray-raven-star/ApexAudio-GamePacks#readme)。当前版本与已发布版本不同时，旧版配置留在对应包的 `_history/`。

| 游戏 | 目录 | 包 ID | 当前版本 | 已发布版本 | 后端 | 重建路径 |
| --- | --- | --- | --- | --- | --- | --- |
| 007 First Light | [`007-first-light`](007-first-light/README.md) | `local.007-firstlight` | 1.1.1 | 1.1.1 | Wwise | [通用 Wwise](007-first-light/PROVENANCE.md) |
| 赛博朋克 2077 | [`cyberpunk2077`](cyberpunk2077/README.md) | `local.cyberpunk2077` | 1.1.1 | 1.1.1 | Wwise | [通用 Wwise](cyberpunk2077/PROVENANCE.md) |
| 龙之信条 2 | [`dragons-dogma-2`](dragons-dogma-2/README.md) | `local.dragonsdogma2` | 0.2.0 | [0.1.1](dragons-dogma-2/_history/0.1.1/manifest.json) | Wwise | [通用 Wwise](dragons-dogma-2/PROVENANCE.md) |
| 艾尔登法环 | [`elden-ring`](elden-ring/README.md) | `local.elden-ring` | 0.2.0 | [0.1.1](elden-ring/_history/0.1.1/manifest.json) | Wwise | [专用 C# 入口](elden-ring/PROVENANCE.md) |
| 霍格沃茨之遗 | [`hogwarts-legacy`](hogwarts-legacy/README.md) | `local.hogwarts-legacy` | 0.1.0 | [0.1.0](https://github.com/gray-raven-star/ApexAudio-GamePacks/releases/tag/pack/hogwarts-legacy/v0.1.0) | Wwise | [通用 Wwise](hogwarts-legacy/PROVENANCE.md) |
| 夺宝奇兵：古老之圈 | [`indiana-jones-great-circle`](indiana-jones-great-circle/README.md) | `local.indiana-great-circle` | 0.2.1 | [0.1.1](indiana-jones-great-circle/_history/0.1.1/manifest.json) | Wwise | [专用原生采集](indiana-jones-great-circle/PROVENANCE.md) |
| 天国：拯救 2 | [`kingdom-come-deliverance-2`](kingdom-come-deliverance-2/README.md) | `local.kingdomcome2` | 1.1.0 | [1.0.1](kingdom-come-deliverance-2/_history/1.0.1/manifest.json) | Fmod | [通用 FMOD](kingdom-come-deliverance-2/PROVENANCE.md) |
| 怪物猎人：崛起 | [`monster-hunter-rise`](monster-hunter-rise/README.md) | `independent.mhrise.wwise` | 0.1.2 | 0.1.2 | Wwise | [通用 Wwise](monster-hunter-rise/PROVENANCE.md) |
| 怪物猎人：世界 | [`monster-hunter-world`](monster-hunter-world/README.md) | `local.monsterhunterworld` | 1.1.0 | [1.0.2](monster-hunter-world/_history/1.0.2/manifest.json) | Wwise | [通用 Wwise](monster-hunter-world/PROVENANCE.md) |
| 幻兽帕鲁 | [`palworld`](palworld/README.md) | `independent.palworld.wwise` | 0.1.1 | 0.1.1 | Wwise | [通用 Wwise](palworld/PROVENANCE.md) |
| 战锤 40K：行商浪人 | [`rogue-trader`](rogue-trader/README.md) | `apex.wh40k-rogue-trader` | 1.1.1 | 1.1.1 | Wwise | [通用 Wwise](rogue-trader/PROVENANCE.md) |
| 杀戮尖塔 2 | [`slay-the-spire-2`](slay-the-spire-2/README.md) | `apex.slay-the-spire-2` | 1.0.1 | 1.0.1 | Fmod | [通用 FMOD](slay-the-spire-2/PROVENANCE.md) |
| 剑星 | [`stellar-blade`](stellar-blade/README.md) | `local.stellarblade` | 0.2.0 | [0.1.2](stellar-blade/_history/0.1.2/manifest.json) | Unreal | [通用 Unreal](stellar-blade/PROVENANCE.md) |

从仓库根目录按[重建指南](../docs/authoring/build-a-gamepack.md)构建和校验。`_archive/` 收录用于追溯的原始工作区、制作脚本和旧采集源码。

源码归档引入时，表中 12 个当前版本均通过 .NET 8 和 Zig 0.15.2 构建及 DevTools 离线导入校验。该次验证中的游戏连接、采集和手柄反馈均为未测。
