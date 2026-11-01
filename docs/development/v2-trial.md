# FMOD JSON 共享后端试验

当前主程序和源码构建的 `apex.fmod` 后端版本为 `0.3.0`。本轮实现构建证据与采集参数分离、连接目标固定；保留 Owner `0.2.0` 试验包及其后端。源码来源和验证边界见[试验基线](owner-v2-baseline.md)，领域概念见[项目术语](../../CONTEXT.md)。

## 版本与兼容边界

| 使用方式 | 包格式 | 通信协议 | 启动上下文 | FMOD 参数 |
|---|---|---|---|---|
| 已有 V1 包 | `schemaVersion=1` | 1 | 沿用包内程序原启动方式 | 包内原配置 |
| Owner 0.2.0 试验包与原配后端 | `schemaVersion=2` | 1 | 1 | 原试验参数，包含构建摘要 |
| 源码构建的 0.3.0 后端 | `schemaVersion=2` | 1 | 2 | 参数文件中 `schemaVersion=2`，不含构建证据 |

主程序按后端描述的 `contextVersion` 生成上下文。新 FMOD 后端只接受 `apex.fmod`、`0.3.0`、`studio-v1` 和上下文 2 的组合；旧包仍使用其固定的 0.2.0 后端，不能把新上下文交给旧二进制。原生配置与 PCM ABI 本轮未修改，产品 V2 不意味着所有协议都升级为 2。

## 游戏构建与采集参数

清单的每条 `games` 记录包含 EXE 文件名、完整 SHA-256、`profileId`，以及可选的 `modules` 字典（模块名到 SHA-256）。主程序按 EXE 名和摘要选择唯一构建，再把该记录的模块证据交给后端核验。相同 EXE 摘要对应多条记录时，即使模块组合不同，也会明确冲突；尚未实现用模块摘要消歧或按安装根匹配任意多文件组合。不能将不同记录的证据拼接使用。

在通用模型中 `modules` 可省略，但新 FMOD 后端连接真实目标时，强制要求选中构建提供 `fmod.dll`、`fmodstudio.dll` 和 `wrapperModule` 对应模块的摘要。后端对照目标进程实际加载的模块校验，漏填不能绕过检查。

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

## 会话与连接

上下文 2 保存包身份、清单摘要、Profile、后端依赖、参数，以及选中的 `Build` 和目标 `Target`。目标身份为 PID、UTC 启动时间 ticks 和实际程序路径。主程序在准备真实连接时取得该身份，后端在 `ValidateTarget` 和 `Connect` 时复核身份、EXE 和关键模块，并在注入前重新检查条件。

未指定目标启动后端仅用于离线查询身份和分组；这种上下文不能随后用于连接游戏，必须重新准备目标上下文。上下文在正常关闭及启动失败路径清理。重复检查可缩小检查与使用之间的竞态窗口，不能消除全部竞态。

`Core` 负责通用包、构建选择和启动上下文；FMOD 参数含义与必需模块由 `GameDefinition.FromContext`、`Validate` 和 `ValidateTarget` 解释。录音、独立 PCM 和震动反馈沿用已有数据路径，主程序显示正常运行状态，不提供适配认证或评分。

源码 FMOD 后端的连接生命周期由 `FmodStream` 维护。连接建立后持续保活，独立 PCM 转送任务只管理已选择的音轨；`StopCapture` 或空独立选择不结束连接心跳和实时反馈。空选择返回已连接、未传输独立音轨及空范围，随后可重新开始。未知或不可采集的组在修改范围前拒绝，原请求继续有效。断开时先结束连接任务，再停止原生采集并释放映射。

IPC 仍为 1，原状态和错误字段保留；可选的 `failure` 提供明确运行故障，新 FMOD 用非 null 的 `captureGroupIds` 说明独立范围。对此状态，工作台按连接失效或明确故障恢复，允许连接保留而独立传输已停止。旧后端缺失该字段或为 null 时沿用原恢复行为：声明独立 PCM 能力却停止采集仍被视为异常；不能将它当作空选择或推定旧后端支持新停止语义。详细字段见[适配器协议](../reference/adapter-protocol.md)。

## 包版本与当前默认

包仓库区分已安装版本、每包 ID 的当前默认和会话使用记录。导入不改当前，包括首次导入；选择当前前校验包内容和该版全部共享后端依赖，缺依赖的新版本仍可保留安装。相同 ID/版本的相同内容幂等导入，不同清单摘要拒绝覆盖。新安装版本与旧单版本布局并存，旧安装继续可见且无需重打包；具体存储和选择规则见[安装管理](../reference/gpack-format.md#安装当前选择与移除)。

`PackageStore.SelectCurrent` 持久化默认元数据，不断开当前会话。桌面分别显示当前默认与会话使用的版本；连接确认固定版本身份，后续默认变化不能替换该次连接使用的包。`AdapterClient` 启动时持有包版本使用记录，在后端进程退出并释放本地资源后解除；当前默认或仍在用的版本不能直接移除。数据根由安装记录明确提供，不再从包目录层数推断。

移除版本不删用户预设和录音；不可用的历史组设置可以保留，当前文件夹批量操作仅作用于实际可用组。复杂 ID 或语义变化仍没有通用自动迁移。上述行为已通过源码回归及模拟适配器的桌面版本选择检查；这不代表真实游戏采集或完整 V2 架构已验收。

## 构建与工程验证

先运行 `pwsh -NoProfile -File tools/Build.ps1 -Test`，再用 `tools/TestV2.ps1` 检查：

- 默认模式校验 `tests/fixtures/v2/`，由源码宿主连接原样 0.2.0 后端。
- 指定 `-Compiler <zig.exe>` 时，源码重建 0.3.0 托管后端和原生 DLL，再在独立 `artifacts/source-v2-<随机标识>` 目录转换两份测试配置：将构建证据移入 `games`，写入参数格式 2，并固定新后端版本与描述摘要。
- `-LegacyPack <旧.gpack>` 增加 V1 导入、启动和查询分组回归；两种模式均不连接真实游戏。

测试转换用于验证当前契约，不是主程序的旧包自动升级功能。原始 fixtures 保持不变；重建的 0.3.0 产物也仅用于本地验证，不能以同一已发布版本分发不同内容。`tools/BuildV2Backend.ps1` 独立输出 0.3.0 后端，`tools/PackV2/` 负责配置包打包与完整性校验。

`FmodStreamChecks` 用合成生产者缓冲检查无效组不干扰原传输、停止与空范围保留心跳、在途帧完整结束、同连接恢复时序列递增，以及目标丢失与正常停止的区分。主程序回归、旧 0.2.0/V1 路线和源码后端路线均有通过记录；合成缓冲不包含真实 FMOD/DSP 回调，也不能证明物理反馈效果。

`tools/Prepare-V2Trial.py`、`tools/Bundle-V2Trial.py` 保留 Owner 本机试验用途，不能作为新格式的通用制作入口。

## 尚未覆盖

实际已加载关键模块的完整记录匹配、同 Profile 候选集合及相应会话准备仍待补齐；源码仍由 `SelectBuild` 按 EXE 名/摘要选择唯一记录，再由后端核对该记录的模块。首期范围是实际 EXE 和已加载模块，不建设任意安装根发现或磁盘全扫描。Lua、Wwise/Unreal V2、在线目录与来源认证/撤销仍未实现。包多版本和当前默认选择不改变引擎实际支持范围；回退可选旧包仍可能因游戏已加载不同采集 DLL 而要求重启。用户预设和录音独立保存，复杂设置迁移不属于本轮。

FMOD 2.02/2.03 版本查询修复及短时游戏采集结果来自 Owner 的[原始验证记录](../../V2-验证记录.md)。本轮没有独立核实对应 SDK 的 ABI，也未进行真实游戏采集或物理手柄复测；模拟适配器的界面检查不能替代这些工程验收。
