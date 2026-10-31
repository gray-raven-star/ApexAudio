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

## 构建与工程验证

先运行 `pwsh -NoProfile -File tools/Build.ps1 -Test`，再用 `tools/TestV2.ps1` 检查：

- 默认模式校验 `tests/fixtures/v2/`，由源码宿主连接原样 0.2.0 后端。
- 指定 `-Compiler <zig.exe>` 时，源码重建 0.3.0 托管后端和原生 DLL，再在独立 `artifacts/source-v2-<随机标识>` 目录转换两份测试配置：将构建证据移入 `games`，写入参数格式 2，并固定新后端版本与描述摘要。
- `-LegacyPack <旧.gpack>` 增加 V1 导入、启动和查询分组回归；两种模式均不连接真实游戏。

测试转换用于验证当前契约，不是主程序的旧包自动升级功能。原始 fixtures 保持不变；重建的 0.3.0 产物也仅用于本地验证，不能以同一已发布版本分发不同内容。`tools/BuildV2Backend.ps1` 独立输出 0.3.0 后端，`tools/PackV2/` 负责配置包打包与完整性校验。

`tools/Prepare-V2Trial.py`、`tools/Bundle-V2Trial.py` 保留 Owner 本机试验用途，不能作为新格式的通用制作入口。

## 尚未覆盖

Lua、Wwise/Unreal V2、配置包多版本激活与回退、完整安装根多文件匹配、在线目录与来源认证/撤销仍未实现。配置包仍安装至 `packs/<id>` 并替换旧版，不能把后端多版本并存理解为包版本管理已经完成。用户预设和录音仍独立保存，复杂设置迁移不属于本轮。

FMOD 2.02/2.03 版本查询修复及短时游戏采集结果来自 Owner 的[原始验证记录](../../V2-验证记录.md)。本轮未独立核实对应 SDK 的 ABI，也未进行 GUI、真实游戏采集或物理手柄复测；工程测试结果见[试验基线](owner-v2-baseline.md)。
