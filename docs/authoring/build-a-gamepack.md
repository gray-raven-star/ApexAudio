# 制作适配包

## 先理解边界

`.gpack` 包含独立适配器程序、采集模块、配置和初始预设。主程序管理连接、分组编辑、录音、试听和反馈；包负责采集游戏声音并输出规定格式的数据。

新游戏有三种情况：

1. 现有模块支持目标接口和结构：准备游戏配置、分组和初始预设，复用模块构建。
2. 引擎相同，版本、接口或对象结构有差异：在包内扩展定位或采集代码，再构建。只改版本号或文件摘要无法解决这些差异。
3. 现有模块无法采集：开发并验证新适配器，通过公共协议接入宿主，游戏判断留在包内。

资源名称、银行和分组目录是调查线索，独立声音需通过录音验证。目前没有自动适配任意游戏的流程。

## 源码项目里看哪里

下面的路径均相对源码项目根目录：

| 路径 | 用途 |
|---|---|
| `adapters/Mock/` | 最小可运行协议参考，产生不同测试音轨，不连接游戏 |
| `src/ApexAudio.Contracts/` | C# 适配器接口、消息和参考传输实现 |
| `adapters/Wwise/` | Wwise 适配器及原生模块；配置结构在 `GameDefinition.cs` |
| `adapters/Fmod/` | FMOD 适配器，字段和限制见其 `README.md` |
| `adapters/Unreal/` | Unreal AudioMixer 适配器，字段和限制见其 `README.md` |
| `schemas/` | 清单与预设的 JSON Schema |
| `gamepack-starter/` | 可复制的 Wwise 定义示例，也是 `New-Pack.ps1` 的模板来源 |
| `tools/Pack.ps1` | 为完整包目录生成文件摘要并压成 `.gpack` |

使用 `tools/New-Pack.ps1` 从 `gamepack-starter/` 和 `templates/` 创建新定义，保存到作者自己选定的目录。字段来源见[配置参考](../reference/engine-config-reference.md)，调查方法见[接口识别](identify-audio-system.md)。

制作采集模块需要完整源码，运行版只含应用和文档。构建环境为 Windows x64、.NET 8 SDK；原生构建脚本调用 Zig 的 `cc`，`-Compiler` 填写实际 `zig.exe` 路径。源码不含第三方游戏 SDK。

## 制作步骤

### 1. 准备一个新的定义目录

按调查结果选择 `Wwise`、`Fmod`、`Unreal` 或 `Custom`，先完成[游戏音频接口识别](identify-audio-system.md)，再在一个尚不存在的目录创建定义。示例使用仓库旁边的 `../my-game`：

```powershell
pwsh -File tools/New-Pack.ps1 -Id author.my-game -Name "目标游戏" -Backend Wwise -Directory ../my-game
```

生成目录：

```text
../my-game/
  AGENTS.md
  manifest.json
  game.json
  presets/initial.json
  README.md
  group-evidence.json
  validation-report.json
```

- `AGENTS.md`：本适配包工作目录的编码代理约定。用代理协助制作时，提供这个目录和 ApexAudio 源码或 DevKit 的位置。标准构建会把这份开发指引留在定义目录。
- `manifest.json`：包 ID、名称、版本、适配器入口、游戏匹配和预设引用，格式见 [包清单](../reference/gpack-format.md)。为新游戏分配独立的包 ID。
- `game.json`：适配器配置，包含游戏地址或引擎字段，宿主不解析。自写适配器可使用其他文件名和格式。
- `presets/initial.json`：作者提供的初始命名、显示文件夹和开关，格式见 [预设说明](../reference/presets-recordings.md)。`packId` 必须对应新包 ID，分组键必须匹配适配器返回的稳定 ID。
- `README.md`：支持的游戏版本、可用能力、未验证项目和已知限制。
- `group-evidence.json`：保存分组的名称来源、效果槽和证据文件。Wwise 的填写与构建检查见[分组证据说明](../reference/group-evidence.md)。
- `validation-report.json`：验证阶段、命令、结果和待测项目。各阶段初始为 `not-tested`，完成后填写结果。

`__REQUIRED__` 和空分组需要用调查结果补齐。Custom 还生成 `adapter/YourAdapter.csproj` 与协议骨架，供实现自己的采集器。

声音父子关系由适配器报告，预设文件夹只整理界面，不改变游戏路由。各编号需对应各自的音频来源，不能重复发送同一份总混音。

#### Wwise 定义的格式示例

以下是上述命令生成的初始结构。占位字段和空分组要用目标游戏的实际调查结果补齐，之后才能构建可发布的适配包。

`manifest.json` 描述包身份和运行入口，`presets` 列出要随包分发的预设。打包时才会填入 `files`。

```json
{
  "schemaVersion": 1,
  "id": "author.my-game",
  "name": "目标游戏",
  "version": "0.1.0",
  "author": "__REQUIRED__",
  "source": "__REQUIRED__",
  "description": "Unverified development package",
  "protocolVersion": 1,
  "entryPoint": "ApexAudio.Wwise.exe",
  "demo": false,
  "games": [
    {"executable": "__REQUIRED__.exe", "sha256": "__REQUIRED__"}
  ],
  "presets": ["presets/initial.json"],
  "files": {}
}
```

`game.json` 从 [gamepack-starter 里的 Wwise 配置](../../gamepack-starter/game.json) 生成。核对目标 EXE 后，把相同的摘要写入清单的 `games[0].sha256` 和后端配置的 `gameSha256`，再按实际引擎和音频路由填写其余字段。整个起始目录见 [gamepack-starter](../../gamepack-starter/README.md)，可以直接复制。

`presets/initial.json` 的 `packId` 对应清单的 `id`。确认实际分组后，用适配器返回的稳定分组 ID 作为 `groups` 的键。

```json
{
  "schemaVersion": 1,
  "id": "initial",
  "name": "初始配置",
  "packId": "author.my-game",
  "folders": [],
  "groups": {}
}
```

### 2. 复用或实现适配器

完整接口见 [适配器协议](../reference/adapter-protocol.md)，包括：

- 游戏连接、版本核对、分组列表、启停采集、状态与断开。
- 每条分组的独立 PCM16、48 kHz、双声道数据和统一 QPC 时间戳。
- 能力按实际采集结果报告；只能取得总混音时，提供一个整体混音节点并标明名称。
- 如需低延迟补充震动，实现可选 `realtimeMix`：采集端输出所选分组的混音共享缓冲区；空选择应静音，录音范围不跟随反馈开关缩减。

管道发送和写盘放在音频回调外。游戏适配代码放在 `adapters/` 和新包中，主程序不增加游戏名分支。

开发 Custom 或扩展原生采集器时，按[新模块开发](write-a-custom-adapter.md)核对回调、传输和资源释放。

### 3. 构建和打包

在源码根目录执行统一构建入口：

```powershell
pwsh -File tools/Build-GamePack.ps1 -DefinitionDirectory ../my-game -Compiler C:/Tools/zig/zig.exe
```

`C:/Tools/zig/zig.exe` 需替换成实际路径；已配置 `APEX_ZIG`、项目内编译器或 PATH 时可省略 `-Compiler`。Custom 分支不查找 Zig。输出为 `dist/packages/<manifest.id>.gpack`，引擎包的采集模块摘要由后端构建器填入。

统一入口先检查必填占位、游戏 EXE 摘要、分组和预设身份，再执行后端检查及构建。维护时可单独调用底层 `BuildPack.ps1`，但它不包含上述统一检查。`Pack.ps1` 只生成文件清单和归档。

自行实现的其他适配器，先整理一个含运行程序、依赖和清单的完整目录，再执行：

```powershell
pwsh -File tools/Pack.ps1 -Directory ./my-complete-pack -Output ./dist/packages/author.my-game.gpack
```

发布包保留运行文件和允许再分发的依赖；排除源码项目、`data/`、诊断用的内存转储、录音及游戏资源原包。接口和采集效果按下一步验证。


生成包后，继续[验证采集与反馈](verify-a-gamepack.md)。
