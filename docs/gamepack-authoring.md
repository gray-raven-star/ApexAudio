# 给新游戏制作适配包

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
| `gamepacks/` | 你为目标游戏新建的版本匹配、配置和初始预设；公开仓库不附具体游戏定义 |
| `schemas/` | 清单与预设的 JSON Schema |
| `tools/Pack.ps1` | 为完整包目录生成文件摘要并压成 `.gpack` |

使用 `tools/New-Pack.ps1` 从 `templates/` 创建新定义，保存到 `gamepacks/<你的包ID>/`。字段来源见[配置参考](engine-config-reference.md)，调查方法见[接口识别](audio-discovery.md)。

制作采集模块需要完整源码，运行版只含应用和文档。构建环境为 Windows x64、.NET 8 SDK；原生构建脚本调用 Zig 的 `cc`，`-Compiler` 填写实际 `zig.exe` 路径。源码不含第三方游戏 SDK。

## 制作步骤

### 1. 确认目标版本和采集能力

先按[环境说明](build-environment.md)准备 DevTools，然后保存一次静态调查：

```powershell
prebuilt/devtools/ApexAudio.DevTools.exe inspect --exe "D:/Games/Example/Game.exe" --resources "D:/Games/Example" --output evidence/static.json
```

将路径换成实际安装位置。游戏启动后，加 `--pid 1234 --module Audio.dll` 检查指定加载模块，PID 和模块名也要替换。报告包含摘要、导出和候选资源；`truncated=true` 时缩小资源目录重查。

记录游戏 EXE 和调用模块的版本、SHA256，核对分组、父子路由、采样率和通道布局。未知名称先用稳定编号显示，试听后标注；猜测的名称保留为候选。

Wwise 核对接口、SDK ABI 和银行结构；FMOD 定位有效的 System 实例并核对 API；Unreal 核对 AudioMixer 设备、对象布局和监听接口。现有 FMOD 定位器和 Unreal 读取器只支持已核对的结构，复用前需确认目标结构一致。

### 2. 准备一个新的定义目录

按调查结果选择 `Wwise`、`Fmod`、`Unreal` 或 `Custom`，在一个尚不存在的目录创建定义：

```powershell
pwsh -File tools/New-Pack.ps1 -Id author.my-game -Name "目标游戏" -Backend Wwise -Directory gamepacks/my-game
```

生成目录：

```text
gamepacks/my-game/
  manifest.json
  game.json
  presets/initial.json
  README.md
  group-evidence.json
  validation-report.json
```

- `manifest.json`：包 ID、名称、版本、适配器入口、游戏匹配和预设引用，格式见 [包清单](gpack-format.md)。为新游戏分配独立的包 ID。
- `game.json`：适配器配置，包含游戏地址或引擎字段，宿主不解析。自写适配器可使用其他文件名和格式。
- `presets/initial.json`：作者提供的初始命名、显示文件夹和开关，格式见 [预设说明](presets-recordings.md)。`packId` 必须对应新包 ID，分组键必须匹配适配器返回的稳定 ID。
- `README.md`：支持的游戏版本、可用能力、未验证项目和已知限制。
- `group-evidence.json`：保存分组的名称来源、效果槽和证据文件。Wwise 的填写与构建检查见[分组证据说明](group-evidence.md)。
- `validation-report.json`：验证阶段、命令、结果和待测项目。各阶段初始为 `not-tested`，完成后填写结果。

`__REQUIRED__` 和空分组需要用调查结果补齐。Custom 还生成 `adapter/YourAdapter.csproj` 与协议骨架，供实现自己的采集器。

声音父子关系由适配器报告，预设文件夹只整理界面，不改变游戏路由。各编号需对应各自的音频来源，不能重复发送同一份总混音。

### 3. 复用或实现适配器

完整接口见 [适配器协议](adapter-protocol.md)，包括：

- 游戏连接、版本核对、分组列表、启停采集、状态与断开。
- 每条分组的独立 PCM16、48 kHz、双声道数据和统一 QPC 时间戳。
- 能力按实际采集结果报告；只能取得总混音时，提供一个整体混音节点并标明名称。
- 如需低延迟补充震动，实现可选 `realtimeMix`：采集端输出所选分组的混音共享缓冲区；空选择应静音，录音范围不跟随反馈开关缩减。

管道发送和写盘放在音频回调外。游戏适配代码放在 `adapters/` 和新包中，主程序不增加游戏名分支。

开发 Custom 或扩展原生采集器时，按[新模块开发](new-module.md)核对回调、传输和资源释放。

### 4. 构建和打包

在源码根目录执行统一构建入口：

```powershell
pwsh -File tools/Build-GamePack.ps1 -DefinitionDirectory gamepacks/my-game -Compiler C:/Tools/zig/zig.exe
```

`C:/Tools/zig/zig.exe` 需替换成实际路径；已配置 `APEX_ZIG`、项目内编译器或 PATH 时可省略 `-Compiler`。Custom 分支不查找 Zig。输出为 `dist/packages/<manifest.id>.gpack`，引擎包的采集模块摘要由后端构建器填入。

统一入口先检查必填占位、游戏 EXE 摘要、分组和预设身份，再执行后端检查及构建。维护时可单独调用底层 `BuildPack.ps1`，但它不包含上述统一检查。`Pack.ps1` 只生成文件清单和归档。

自行实现的其他适配器，先整理一个含运行程序、依赖和清单的完整目录，再执行：

```powershell
pwsh -File tools/Pack.ps1 -Directory ./my-complete-pack -Output ./dist/packages/author.my-game.gpack
```

发布包保留运行文件和允许再分发的依赖；排除源码项目、`data/`、诊断内存、录音及游戏资源原包。接口和采集效果按下一步验证。

### 5. 用应用导入并实测

先检查完整性和启动协议：

```powershell
prebuilt/devtools/ApexAudio.DevTools.exe check --pack dist/packages/author.my-game.gpack --output evidence/package.json
prebuilt/devtools/ApexAudio.DevTools.exe check --pack dist/packages/author.my-game.gpack --launch --output evidence/protocol.json
```

第一条解压并校验文件，第二条还执行适配器并查询信息、分组；两条都不连接真实游戏。随后可用下列命令采集，或直接在应用中录制：

```powershell
prebuilt/devtools/ApexAudio.DevTools.exe capture --pack dist/packages/author.my-game.gpack --pid 1234 --allow-game-connection --seconds 5 --output evidence/capture.json
```

PID 换成目标进程。`capture` 会调用包的连接流程，可能加载原生模块或挂接采集回调，但不启动 X-Haptics。`outcome` 表示命令是否完成；继续检查 `errors`、`status`、`packets`、`nonZeroGroups` 和各组 `peaks`，判断连接及音频输出是否正常。

按普通用户流程导入 `.gpack`，确认来源后连接。发布前至少确认：

- 支持版本连接成功，不匹配版本明确拒绝。
- 逐组试听，确认各轨对应的声音；父子总线包含相同内容时注明重叠关系。
- 分别录制背景音乐／对白和枪声、脚步、撞击等动作场景，记录候选组包含的声音。目标声音仍与背景混在同一轨时，寻找更细路由；无法继续分离的部分写入包说明。
- 录制期间修改反馈选择不会打断录音；全关闭后补充震动输入静音。
- 试听只播放所选录音轨道；声音和反馈时间基准一致。
- 断开、游戏退出及重新连接行为正常；用户预设与还原初始状态正常。

启用 X-Haptics，在相同场景比较全组输入与所选动作组输入，记录背景音是否仍触发明显补充震动、目标动作反馈是否保留、是否有可感知延迟。音轨分离、反馈输入和实体手柄测试分别填写结果。

完成构建和协议检查后填写对应结果；尚未运行的测试保留 `not-tested`，注明缺少的环境。

完整诊断命令、报告字段和临时文件处理见[验证说明](validation.md)。采集失败时按[故障排查](troubleshooting.md)定位最早失败的阶段。

## 阶段记录与交接

`New-Pack.ps1` 已从 `templates/validation-report.json` 生成报告。随制作过程更新现有字段，续接时根据证据决定下一步：

| 阶段 | 保存内容 | 报告位置与后续判断 |
|---|---|---|
| 目标与接口调查 | EXE／模块摘要、资源解析和接口依据 | `targetVersionEvidence`、`versions`、`audioInstanceEvidence`；仍缺 ABI、实例或路由时继续调查 |
| 定义与构建 | manifest、后端配置、预设、分组证据和构建结果 | `groupEvidenceFile`、`stages.build`；按构建器的具体错误修正输入，成功后检查生成的 `.gpack` |
| 包与协议 | `check`、`check --launch` 报告 | `stages.packageIntegrity`、`stages.protocol`；协议失败先修启动或消息交互，成功后再测游戏连接 |
| 游戏内采集 | 对象、注册、回调、各轨录音和场景对照 | `stages.resolvedObjects` 至 `stages.independentTracks`；非零 PCM 后仍需试听确认各组含义 |
| 选择与反馈 | 开关、重连、反馈源和手柄测试记录 | `stages.selection`、`disconnectReconnect`、`feedbackSource`、`physicalHaptics`；分别填写结果 |

命令写入 `reproductionCommands`，报告或录音路径写入 `evidenceFiles`，资料版本及来源写入 `sources`。未执行项保留 `not-tested`，在 `notTested` 写明缺少的环境。阶段失败时填写 `blockingStage`，并在 `knownLimitations` 记录具体错误、缺少的字段和下一项验证；停止测试后更新 `cleanup`。

续接先核对目标文件摘要、定义和已有产物。目标版本变化后重新检查相关接口与测试；同一版本已有的证据可继续使用。交付时随源码或定义提供报告及其引用的可分享证据，包内 README 列出已验证版本和限制。

## 如何发布

向用户分发 `.gpack`，供应用导入；向开发者另提供适配器源码和构建说明。更新时保持包 ID，提高版本号，重新生成摘要并实测，保留版本检查。

完整源码项目至少保留 `src/`、`adapters/`、`gamepacks/`、`tools/`、`docs/`、`schemas/`、`providers/`、`ui/`、`assets/`、`vendor/` 和根目录的解决方案及构建配置。`artifacts/`、`dist/`、`bin/`、`obj/` 是生成物或开发记录，开发接口以源码和文档为准。
