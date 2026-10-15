# 给新游戏制作适配包

## 先理解边界

`.gpack` 是包含独立适配器程序、采集模块、配置和初始预设的完整包，不只是填写游戏名称的配置文件。主程序管理连接、分组编辑、录音、试听和反馈；包负责找到游戏声音并输出规定格式的数据。

新游戏有三种情况：

1. **现有采集模块覆盖其接口和结构**：准备新游戏配置、分组和初始预设，复用模块构建。
2. **引擎相同，但版本、接口或对象结构不同**：在包的适配器中扩展定位或采集实现，再构建。不能仅改版本号或文件摘要。
3. **现有模块无法采集**：开发新的适配器，遵循同一宿主协议。主程序不需要增加游戏名称判断，但新的采集实现仍需要开发和验证。

知道资源名称、读出银行或找到分组不等于已经取得独立声音。当前没有自动适配任意游戏的流程。

## 源码项目里看哪里

下面的路径均相对源码项目根目录：

| 路径 | 用途 |
|---|---|
| `adapters/Mock/` | 最小可运行协议参考，产生不同测试音轨，不连接游戏 |
| `src/ApexAudio.Contracts/` | C# 适配器接口、消息和参考传输实现 |
| `adapters/Wwise/` | Wwise 适配器及原生模块；配置结构在 `GameDefinition.cs` |
| `adapters/Fmod/` | FMOD 适配器，字段和限制见其 `README.md` |
| `adapters/Unreal/` | Unreal AudioMixer 适配器，字段和限制见其 `README.md` |
| `gamepacks/` | 按游戏保存的版本匹配、配置和初始预设示例 |
| `schemas/` | 清单与预设的 JSON Schema |
| `tools/Pack.ps1` | 为完整包目录生成文件摘要并压成 `.gpack` |

现有定义示例：`gamepacks/monsterhunterworld`（Wwise）、`gamepacks/kingdomcome2`（FMOD）、`gamepacks/stellarblade`（Unreal）。这些是已知游戏的配置实例，不能把里面的地址、编号或版本直接套给其他游戏。

运行版只含应用和文档；制作采集模块需要完整源码。当前构建环境为 Windows x64、.NET 8 SDK；下列原生构建脚本调用 Zig 的 `cc`，`-Compiler` 应填写实际 `zig.exe` 路径。代码并未打包第三方游戏 SDK。

## 制作步骤

### 1. 确认目标版本和采集能力

记录实际游戏 EXE、参与调用的音频模块版本及 SHA256。确认能获取哪些真实独立分组、父子路由、采样率和通道布局。名称缺失时保留稳定编号，试听后标注；不要把猜测当成游戏原名。

Wwise 需要核对接口、SDK ABI 和银行结构；FMOD 需要找到有效 System 实例及兼容 API；Unreal 需要核对 AudioMixer 设备、对象布局和监听接口。现有 FMOD 定位方式和 Unreal 布局均只覆盖已核对的结构。引擎相同不保证可以直接复用。

### 2. 准备一个新的定义目录

```text
gamepacks/my-game/
  manifest.json
  game.json
  presets/initial.json
  README.md
```

- `manifest.json`：固定的包 ID、名称、版本、适配器入口、游戏匹配和预设引用，格式见 [包清单](gpack-format.md)。不要沿用示例包的 ID。
- `game.json`：由所用适配器解释。宿主不解析其中的游戏地址或引擎字段；自写适配器也可以采用别的文件名和配置格式。
- `presets/initial.json`：作者提供的初始命名、显示文件夹和开关，格式见 [预设说明](presets-recordings.md)。`packId` 必须对应新包 ID，分组键必须匹配适配器返回的稳定 ID。
- `README.md`：明确支持的游戏版本、实际可用能力、未验证项目和已知限制。

实际声音父子关系由适配器报告；预设里的文件夹只整理界面，不改变游戏音频路由。不要给多个不同编号重复发送同一份总混音来充当独立音轨。

### 3. 复用或实现适配器

完整接口见 [适配器协议](adapter-protocol.md)，包括：

- 游戏连接、版本核对、分组列表、启停采集、状态与断开。
- 每条分组的独立 PCM16、48 kHz、双声道数据和统一 QPC 时间戳。
- 正确报告能力；只有总混音时，只提供明确命名的整体混音节点。
- 如需低延迟补充震动，实现可选 `realtimeMix`：采集端输出所选分组的混音共享缓冲区；空选择应静音，录音范围不跟随反馈开关缩减。

不要在音频回调里等待管道或写盘。改动留在 `adapters/` 和新包中；不得靠给主程序加游戏名分支才能工作。

### 4. 构建和打包

在源码根目录执行。按实际采用的引擎运行一条命令，不需要全部执行：

```powershell
# Wwise：先还原适配器依赖；game.json 必须含已核对的 SDK / 银行配置。
dotnet restore adapters/Wwise/ApexAudio.Wwise.csproj --configfile NuGet.Config
pwsh -File adapters/Wwise/tools/BuildPack.ps1 -DefinitionDirectory gamepacks/my-game -Compiler C:/Tools/zig/zig.exe

# FMOD
pwsh -File adapters/Fmod/tools/BuildPack.ps1 -Profile gamepacks/my-game -Compiler C:/Tools/zig/zig.exe

# Unreal AudioMixer
pwsh -File adapters/Unreal/tools/BuildPack.ps1 -DefinitionDirectory gamepacks/my-game -Compiler C:/Tools/zig/zig.exe
```

`C:/Tools/zig/zig.exe` 是示例路径，必须替换。脚本把采集模块摘要写入构建产物并调用打包器，输出为 `dist/packages/<manifest.id>.gpack`。这些命令只构建包，不会自动发现游戏接口。

自行实现的其他适配器，先整理一个含运行程序、依赖和清单的完整目录，再执行：

```powershell
pwsh -File tools/Pack.ps1 -Directory ./my-complete-pack -Output ./dist/packages/author.my-game.gpack
```

打包器只负责生成文件清单和归档，不证明接口可用。不要把源码项目、`data/`、诊断内存、录音或游戏资源原包塞进发布包；携带的运行依赖必须允许再分发。

### 5. 用应用导入并实测

按普通用户流程导入 `.gpack`，确认来源后连接。发布前至少确认：

- 支持版本连接成功，不匹配版本明确拒绝。
- 不同分组录到对应声音，而非重复总混音；父子总线真实重叠需说明。
- 录制期间修改反馈选择不会打断录音；全关闭后补充震动输入静音。
- 试听只播放所选录音轨道；声音和反馈时间基准一致。
- 断开、游戏退出及重新连接行为正常；用户预设与还原初始状态正常。

没有游戏或手柄实测时，README 应写“离线检查通过，实际采集／反馈未验证”，不能把模拟声音测试写成游戏适配成功。

## 如何发布

普通用户只需要 `.gpack`，从应用导入即可。开发者交流时另提供适配器源码及构建说明，便于游戏更新后维护。更新保持同一包 ID、提高版本号、重新生成摘要并实测；不要为绕过匹配而删除版本检查。

完整源码项目至少保留 `src/`、`adapters/`、`gamepacks/`、`tools/`、`docs/`、`schemas/`、`providers/`、`ui/`、`assets/`、`vendor/` 和根目录的解决方案及构建配置。`artifacts/`、`dist/`、`bin/`、`obj/` 是生成物或开发记录，不是作者必须依赖的接口文档。
