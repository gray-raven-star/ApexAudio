# FMOD JSON 共享后端试验

当前主程序和源码构建的 `apex.fmod` 后端版本为 `0.3.0`，提供构建证据与采集参数分离、完整记录匹配、候选集与目标固定的会话准备；保留 Owner `0.2.0` 试验包及其后端。源码来源和验证边界见[试验基线](owner-v2-baseline.md)，领域概念见[项目术语](../../CONTEXT.md)。

## 版本与兼容边界

| 使用方式 | 包格式 | 通信协议 | 启动上下文 | FMOD 参数 |
|---|---|---|---|---|
| 已有 V1 包 | `schemaVersion=1` | 1 | 沿用包内程序原启动方式 | 包内原配置 |
| Owner 0.2.0 试验包与原配后端 | `schemaVersion=2` | 1 | 1 | 原试验参数，包含构建摘要 |
| 源码构建的 0.3.0 后端 | `schemaVersion=2` | 1 | 2 | 参数文件中 `schemaVersion=2`，不含构建证据 |

主程序按后端描述的 `contextVersion` 生成上下文。新 FMOD 后端只接受 `apex.fmod`、`0.3.0`、`studio-v1` 和上下文 2 的组合；旧包仍使用其固定的 0.2.0 后端，不能把新上下文交给旧二进制。原生配置与 PCM ABI 本轮未修改，产品 V2 不意味着所有协议都升级为 2。

## 游戏构建与采集参数

清单的每条 `games` 记录包含 EXE 文件名、完整 SHA-256、`profileId`，以及可选的 `modules` 字典（模块名到 SHA-256）。连接准备收集实际目标的 EXE 与已加载模块证据，对每条记录独立比较全部要求。因此相同 EXE 摘要、不同模块组合可以按完整记录区分，不将不同记录的命中项拼接使用。

多条完整记录匹配同一 Profile 时保留全部命中候选，不强造唯一构建；完整匹配不同 Profile 时返回冲突。必要模块尚未加载、路径或文件不可读时返回证据不足，不能算作摘要不匹配；另一个 Profile 的记录仍证据不足时也不会默认采用已知匹配的 Profile。进程检测列表仍以 EXE 信息提示候选，真正的完整匹配在 `PreparedSession.Prepare`，不能把列表提示当作连接许可。当前证据范围为 EXE 与实际已加载模块，不做任意安装根发现或磁盘全扫描。

在通用模型中 `modules` 可省略，但新 FMOD 后端连接真实目标时，强制要求准备候选提供 `fmod.dll`、`fmodstudio.dll` 和 `wrapperModule` 对应模块的摘要。后端独立收集实际已加载模块证据，漏填不能绕过检查。

FMOD 参数格式 2 仅描述定位和总线等采集内容，拒绝 `executable`、`executableSha256`、`modules`、`captureFile`、`captureSha256`。后端从选中构建取得 EXE 与模块证据，从自身描述取得采集 DLL。两个 EXE 摘要不同的构建可以引用同一份 Profile；各自关键模块仍须满足所选记录的要求。

下面是结构片段，用于说明字段归属；摘要和定位值为占位示例，不是可连接游戏的完整适配包。

清单片段：

```json
{
  "games": [{
    "executable": "ExampleGame.exe",
    "sha256": "<构建 A 的 EXE SHA-256>",
    "profileId": "shared",
    "modules": {
      "fmod.dll": "<模块 SHA-256>",
      "fmodstudio.dll": "<模块 SHA-256>",
      "GameAudio.dll": "<模块 SHA-256>"
    }
  }],
  "profiles": [{
    "id": "shared",
    "parameters": "profiles/shared.json",
    "backend": {
      "id": "apex.fmod", "version": "0.3.0", "variant": "studio-v1",
      "descriptorSha256": "<本次后端描述的 SHA-256>"
    }
  }]
}
```

`profiles/shared.json` 片段：

```json
{
  "schemaVersion": 2,
  "name": "示例采集参数",
  "locator": "wrapper",
  "wrapperModule": "GameAudio.dll",
  "wrapperRva": 128,
  "runtimeVersion": 131584,
  "stateOffset": 0,
  "studioOffset": 8,
  "buses": []
}
```

第二条 `games` 记录可填写另一 EXE 摘要及其模块证据，继续引用 `shared`。实际参数还必须提供有效总线；FMOD 支持 `wrapper` 和 `studioHandle` 两种现有定位方式、1～64 条总线。`buses[].stableId` 保存声音语义，原 `id` 仍为 FMOD GUID，父组按 GUID 转换；无法可靠对应的用户设置不自动猜测迁移。

## 后端支持声明与参数检查

`backend.json` 的可选 `support` 声明平台、IPC 版本及各变体的参数格式、版本、最大字节数和约束。`BackendStore.Describe` 读取并校验固定依赖的本地描述及文件，不启动后端。缺少该声明的 Owner 0.2.0 后端保留原流程，不推定其支持新参数格式。

`adapters/Fmod/Definition` 保存纯数据模型和 `FmodParameterContract`，由作者工具与 FMOD 后端共同使用。格式 2 限制参数为 1 MiB、1～64 条总线，检查字段类型、未知字段、GUID/稳定 ID、父组引用与循环，以及定位模式和运行时范围的组合约束。`wrapper` 要求有效 RVA 且句柄为零；`studioHandle` 要求有效句柄，RVA 和两个偏移均为零。声明和静态检查不能证明游戏中的地址或音频语义正确。

Core 通过 `Contracts` 定义的 `IBackendConfigurationValidator` 接受静态验证器，不引用 FMOD 参数模型。Desktop 组合根注入第一方 `FmodConfigurationValidator`，作者工具复用同一验证器；它使用同源参数契约检查完整字段、组合及各关联构建的必需模块摘要声明。静态验证不运行后端、不加载第三方插件，也不把支持描述解释为任意校验程序。

`PackageStore.Readiness` 对包的全部 Profile 检查固定依赖、支持声明、参数头和尺寸，并调用匹配验证器。`SelectCurrent` 在写入默认选择前检查，失败保留旧默认；`PreparedSession.Prepare` 同样检查，失败释放已取得的版本使用保护。带新支持声明却缺少匹配验证器或声明不受支持时返回 `backend_unsupported`，不能仅凭 JSON 可解析就启用。旧后端未声明 `support` 时保留历史流程，V1 不强制使用共享后端校验。

FMOD 后端初始化仍独立检查参数，使用工厂方式在控制与音频管道连接后初始化，错误通过 IPC 返回具体原因，不以管道连接超时代替诊断。静态就绪检查不访问实际游戏目标，不能代替完整记录匹配、连接前复核或实机验证。

## 会话与连接

`PreparedSession` 固定包版本身份、Profile、后端、目标身份、参数和完整匹配结果。上下文与匹配结果保存为 JSON 快照，读取返回快照内容，不与后续可变对象共享修改。应用层先准备新会话，再断开旧会话；准备失败不先破坏已有连接。准备期间即持有包版本使用保护，启动客户端一次性接管其所有权，不能重复使用同一个准备对象。

上下文 2 包含原 `Build`、目标 `Target` 和可选 `Candidates`。`Candidates` 保存原准备候选集合，`Build` 仅保留首条兼容投影；旧上下文 2 缺少 `Candidates` 时回退到 `Build`。后端只在原候选内重新采集并逐条完整复核，至少一条仍完整匹配且满足 FMOD 必需证据才允许连接，返回仍命中的集合而不改写原集合。`Connect` 不能绕过复核，也不切换到未准备的 Profile。

目标身份为 PID、UTC 启动时间 ticks 和实际程序路径。后端在 `ValidateTarget`、`Connect` 及注入前复核；宿主也按准备时的目标身份检查连接请求。Owner 0.2.0 继续使用原上下文 1、参数和命令，宿主若已有实际目标准备结果仍在本地复核，不要求旧后端识别新字段。

源码 FMOD 后端在正常构建复核通过后，由 `CaptureModuleGuard` 检查目标已加载的 `ApexFmodCapture*.dll` 文件名族；同文件名且模块文件 SHA-256 与所选后端一致才允许复用。已知族内出现不同文件名或不同摘要时返回 `restart_required`，下一步为 `restart_game`；模块列表或文件摘要不可读时拒绝，不能视为兼容。`Connect` 在加载前再次检查。这不识别任意改名的未知 DLL，也不能消除检查与加载之间的全部竞态。

原生配置或启动报告已有采集占用时返回 `capture_busy`，提示 `close_other_session`。只有本次 `FmodStart` 成功后，连接才取得停止原生采集的清理责任；失败连接不能调用停止操作影响原有采集。`Disconnect` 释放本连接资源但不卸载游戏内 DLL，更换冲突版本需要重启游戏。跨进程同时连接尚无原子所有权机制，不能将占用错误及本地清理责任当作完整并发互斥保证。

上下文 2 未指定目标时，启动后端仅用于离线查询身份和分组；这种上下文不能随后用于连接游戏，必须重新准备目标上下文。Owner 0.2.0 的上下文 1 保留原有离线启动后再 Connect 的流程。上下文在正常关闭及启动失败路径清理。重复检查可缩小检查与使用之间的竞态窗口，不能消除全部竞态。

`Contracts/BuildMatching.cs` 定义证据和完整记录比较；`Core/PreparedSession.cs` 管理固定快照及准备所有权，通信客户端负责启动与协议。FMOD 参数含义与必需模块由 `GameDefinition.FromContext`、`Validate` 和 `ValidateTarget` 解释。录音、独立 PCM 和震动反馈沿用已有数据路径，主程序显示正常运行状态，不提供适配认证或评分。

源码 FMOD 后端的连接生命周期由 `FmodStream` 维护。连接建立后持续保活，独立 PCM 转送任务只管理已选择的音轨；`StopCapture` 或空独立选择不结束连接心跳和实时反馈。空选择返回已连接、未传输独立音轨及空范围，随后可重新开始。未知或不可采集的组在修改范围前拒绝，原请求继续有效。断开时先结束连接任务，再停止原生采集并释放映射。

IPC 仍为 1，原状态和错误字段保留；可选的 `failure` 提供明确运行故障，新 FMOD 用非 null 的 `captureGroupIds` 说明独立范围。对此状态，工作台按连接失效或明确故障恢复，允许连接保留而独立传输已停止。旧后端缺失该字段或为 null 时沿用原恢复行为：声明独立 PCM 能力却停止采集仍被视为异常；不能将它当作空选择或推定旧后端支持新停止语义。详细字段见[适配器协议](../reference/adapter-protocol.md)。

## 包版本与当前默认

包仓库区分已安装版本、每包 ID 的当前默认和会话使用记录。导入不改当前，包括首次导入；选择当前前校验包内容、该版全部共享后端依赖及带支持声明的 Profile 参数，检查失败保留原选择，新版本仍可保留安装。相同 ID/版本的相同内容幂等导入，不同清单摘要拒绝覆盖。新安装版本与旧单版本布局并存，旧安装继续可见且无需重打包；具体存储和选择规则见[安装管理](../reference/gpack-format.md#安装当前选择与移除)。

`PackageStore.SelectCurrent` 持久化默认元数据，不断开当前会话。桌面分别显示当前默认与会话使用的版本；连接确认固定版本身份，后续默认变化不能替换该次连接使用的包。安装句柄保存固定清单摘要，文件内容变动时拒绝继续使用，身份不随文件重新计算而漂移。准备会话到后端退出期间持有版本使用保护；当前默认或仍在用的版本不能直接移除。数据根由安装记录明确提供，不再从包目录层数推断。

移除版本不删用户预设和录音；不可用的历史组设置可以保留，当前文件夹批量操作仅作用于实际可用组。上述行为已通过源码回归及模拟适配器的桌面版本选择检查；这不代表真实游戏采集或完整 V2 架构已验收。

`PresetStore.ResolveForGroups` 返回与源预设分离的副本及变更提示：复用已有预设时相同稳定 ID 保留设置，尚无设置且未明确映射的新组关闭，历史组留存。调用方可明确提供不可用旧 ID 到全新 ID 的一对一映射，拒绝覆盖、拆分和合并；没有包内映射新字段、界面映射编辑器或自动猜测。载入与重连不自动写盘，导入仍由用户明确建立副本。工作台保留父混音原选择，并提示它仍可能包含未勾子组声音；既有树横杠反映显示树的部分勾选，不替代实际路由。详见[预设适配规则](../reference/presets-recordings.md#适配当前声音组)。

## 作者工具

`tools/PackV2/` 提供 `create`、`validate`、`package` 和 `describe`。当前作者流程面向 `apex.fmod` 0.3.0、上下文 2、`studio-v1` 和已声明的 FMOD 参数格式 2；已有 V1/Owner 0.2.0 包仍走原安装运行流程，无须通过此工具重制。

以下命令在仓库根目录运行，将 `$backend` 替换为包含 `backend.json` 的具体本地后端目录；它可以是已解包的构建产物或已安装的后端目录，不能直接传入 `.gbackend` 压缩包。

```powershell
$backend = '<本地后端目录>'
dotnet run --project tools/PackV2 -- describe $backend
dotnet run --project tools/PackV2 -- create authoring/example --backend $backend --id community.example --name '示例游戏' --version 0.1.0
# 填写实际 EXE/模块摘要、定位参数、总线及预设后，再校验和打包。
dotnet run --project tools/PackV2 -- validate authoring/example --backend $backend --json
dotnet run --project tools/PackV2 -- package authoring/example artifacts/example-0.1.0.gpack --backend $backend
```

`create` 只创建新的定义目录，固定后端版本及描述摘要，并生成参数、预设和含 TODO 的清单起点；零定位值、占位摘要等必须替换为实际调查结果。刚创建的模板不能直接发布。`validate` 和 `package` 的 `--backend` 可重复指定，以提供各 Profile 的确切本地依赖。

校验在临时副本中生成资源的 `files` 摘要，检查包结构、参数字段与组合、必需模块声明及预设引用，不改写作者原文件。诊断包含文件、字段路径、错误码和原因；`--json` 输出可供其他工具读取。打包重新校验归档后才原子写入正式输出，输出必须位于定义目录外且不能覆盖已有文件。失败不留下正式包。整个作者流程只读取文件，不运行包代码、后端程序或连接游戏；通过静态条件不代表真实游戏兼容性已验证。

## 构建与工程验证

先运行 `pwsh -NoProfile -File tools/Build.ps1 -Test`，再用 `tools/TestV2.ps1` 检查：

- 默认模式校验 `tests/fixtures/v2/`，由源码宿主连接原样 0.2.0 后端。
- 指定 `-Compiler <zig.exe>` 时，源码重建 0.3.0 托管后端和原生 DLL，再在独立 `artifacts/source-v2-<随机标识>` 目录转换两份测试配置：将构建证据移入 `games`，写入参数格式 2，并固定新后端版本与描述摘要。
- `-LegacyPack <旧.gpack>` 增加 V1 导入、启动和查询分组回归；两种模式均不连接真实游戏。

测试转换用于验证当前契约，不是主程序的旧包自动升级功能。原始 fixtures 保持不变；重建的 0.3.0 产物也仅用于本地验证，不能以同一已发布版本分发不同内容。`tools/BuildV2Backend.ps1` 独立输出 0.3.0 后端，构建时执行刚编译的后端 `--describe` 以生成支持声明；这与作者工具静态读取描述的职责不同。

`FmodStreamChecks` 用合成生产者缓冲检查无效组不干扰原传输、停止与空范围保留心跳、在途帧完整结束、同连接恢复时序列递增，以及目标丢失与正常停止的区分。主程序回归、旧 0.2.0/V1 路线和源码后端路线均有通过记录；合成缓冲不包含真实 FMOD/DSP 回调，也不能证明物理反馈效果。

匹配与准备的工程检查使用系统实际加载模块、合成构建声明及后台进程 IPC，覆盖候选保留、证据不足、跨 Profile 冲突、准备快照和版本保护；直接绕过宿主调用后端预检与连接时，缺少必要模块仍被拒绝。Mock 界面检查覆盖已用版本与当前默认分离。上述证据不等同于真实游戏采集、DSP 挂接或反馈硬件验收。

作者工具检查覆盖未完成模板、字段与组合错误、无效预设诊断、作者文件不被改写及失败不产生正式包；源码后端检查覆盖初始化错误经 IPC 返回。旧 0.2.0 和 V1 路线继续有回归记录。这些检查没有运行真实游戏或验证 DSP 与物理反馈。

静态启用检查覆盖全部 Profile、缺少或不匹配验证器的拒绝、失败保留旧默认，以及准备失败释放版本保护；直接通过 IPC 调用后端仍独立拒绝无效参数。这些检查不向游戏注入采集代码。

采集 DLL 冲突检查使用测试进程的实际模块列表、合成 FMOD 模块及真实后端 IPC，覆盖预检与直接 `Connect` 的独立拒绝。测试通过 `NativeLibrary.Load` 加载采集 DLL，只触发 `DllMain`，不调用采集导出；不包含真实游戏、FMOD/DSP 挂接或物理反馈验证。

预设回归覆盖副本解析、有限明确映射、历史保留和父混音提醒；树渲染检查覆盖横杠状态。模拟适配器界面检查确认重配提示可见，并覆盖录音、计时及版本选择；这些证据不代表真实音频路由或物理反馈已验证。

`tools/Prepare-V2Trial.py`、`tools/Bundle-V2Trial.py` 保留 Owner 本机试验用途，不能作为新格式的通用制作入口。

## 尚未覆盖

静态验证器目前只识别已声明的 FMOD 0.3.0 参数契约，不是可加载任意引擎校验器的插件平台。Lua、Wwise/Unreal V2、在线目录与来源认证/撤销仍未实现。首期不建设任意安装根发现或磁盘全扫描。包多版本和当前默认选择不改变引擎实际支持范围；回退可选旧包仍可能因游戏已加载不同采集 DLL 而要求重启。用户预设和录音独立保存；有限解析和提示不等于通用无损迁移，复杂语义变化不自动猜测。

FMOD 2.02/2.03 版本查询修复及短时游戏采集结果来自 Owner 的[原始验证记录](../../V2-验证记录.md)。本轮没有独立核实对应 SDK 的 ABI，也未进行真实游戏采集或物理手柄复测；模拟适配器的界面检查不能替代这些工程验收。
