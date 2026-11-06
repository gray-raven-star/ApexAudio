# 适配包起始目录

这个目录是 Wwise 适配包定义的完整示例，也是 `tools/New-Pack.ps1 -Backend Wwise` 生成新定义时使用的模板。里面的地址、摘要和引擎参数都是占位符，需要用目标游戏的实际调查结果替换。

## 两种用法

直接复制整个目录，按目标游戏改占位符；或者在 ApexAudio 源码根目录运行生成命令：

```powershell
pwsh -File tools/New-Pack.ps1 -Id author.my-game -Name "目标游戏" -Backend Wwise -Directory ../my-game
```

生成命令会把这里的文件复制到指定目录，替换包 ID、名称和版本，并加上面向玩家的 `README.md`。其他引擎（`Fmod`、`Unreal`、`Custom`）的配置来自 `templates/` 下对应的 `game.json`。

## 文件

| 文件 | 内容 |
|---|---|
| `AGENTS.md` | 编码代理在本目录工作时的约定，说明查哪些资料、记录哪些证据。 |
| `manifest.json` | 包 ID、名称、版本、适配器入口、游戏匹配和预设引用。 |
| `game.json` | Wwise 采集配置，包括游戏模块摘要、Bank 版本和分组来源。 |
| `presets/initial.json` | 初始分组、显示文件夹和反馈开关。 |
| `group-evidence.json` | 分组的名称来源、效果槽和证据文件。 |
| `validation-report.json` | 分阶段验证记录，初始状态为未测。 |

字段说明见[包清单](../docs/reference/gpack-format.md)、[预设与录音格式](../docs/reference/presets-recordings.md)、[各引擎配置字段](../docs/reference/engine-config-reference.md)和[分组证据](../docs/reference/group-evidence.md)。

## 边界

这里只有定义和示例，没有构建工具。完整 `.gpack` 会连同采集程序一起打包，所以构建需要 ApexAudio 源码或 DevKit：

```powershell
pwsh -File tools/Build-GamePack.ps1 -DefinitionDirectory ../my-game -Compiler C:/Tools/zig/zig.exe
```

占位符没填完时构建会拒绝。制作流程见[制作适配包](../docs/authoring/build-a-gamepack.md)，采集验证见[验证采集与反馈](../docs/authoring/verify-a-gamepack.md)。
