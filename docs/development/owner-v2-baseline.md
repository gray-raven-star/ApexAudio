# Owner FMOD 试验基线

## 来源和适用范围

本分支保存 Owner 提供的 `ApexAudio-V2-Source-0.2.0` 源码快照，以目标仓库主分支提交 `233b3f18b956716d09e1fc32f86636f917da6ae7` 为父提交。导入提交 `7178199` 保留交付内容，后续整理将随包样例移至 `tests/fixtures/v2/`，以便与可重新生成的 `artifacts/` 区分。

该快照是 FMOD 共享后端试验基线，尚不是正式 V2 架构或稳定格式。已存在的实现以[试验说明](v2-trial.md)及代码为准；社区生态提案描述的多文件构建记录、安装激活、迁移和来源认证等仍需分别实现和验证。

根目录 `SOURCE-SHA256SUMS.txt` 是原始交付的历史清单；在原始解压目录核对的 178 项均通过。它不描述整理后的目录，Git 文本换行规范化也可能改变检出字节，不能据此宣称检出目录与交付目录逐字节相同。`tests/fixtures/v2/SHA256SUMS.txt` 则用于校验随包样例文件。

## 源码与随包程序

`tests/fixtures/v2/` 包含两份配置型 `.gpack` 和一份共享 `.gbackend`。这些是 Owner 随包提供的测试输入，后端内含预编译程序和采集 DLL；成功运行它们不能单独证明当前源码可以构建或重建结果相同。

源码构建、随包后端协议检查、源码重建后端协议检查和实际游戏采集应分别记录。源码重建会改变后端描述摘要，需要为测试配置生成对应的依赖摘要。`tools/BuildV2Backend.ps1` 仍固定后端版本为 `0.2.0`；选择新的输出文件名并不等于发布新版本。不要以同一已发布版本标识分发不同内容。

基线验证入口是 `tools/TestV2.ps1`：默认校验样例摘要、构建 V2 测试并检查随包后端；指定 `-Compiler <zig.exe>` 时，从源码重建托管入口、原生 DLL 和 `PackV2`，在独立 `artifacts/source-v2-<随机标识>` 中生成更新依赖摘要的测试配置。两种模式都只检查协议，不连接真实游戏。未指定 `-LegacyPack` 时仅运行 17 项 V2 检查；显式传入旧包后增加一项 V1 回归检查。这里的 `0.2.0` 重建产物仅用于本地测试。

`tools/Prepare-V2Trial.py`、`tools/Bundle-V2Trial.py` 是 Owner 本机的交付辅助脚本，包含本机路径，不是通用构建的依赖或新游戏扫描器。

## 当前契约边界

| 边界 | 代码事实与后果 |
|---|---|
| 构建记录与 Profile | `BackendStore.cs` 的 `V2Packages.Select` 按 EXE 名称和摘要选择 Profile；FMOD `GameDefinition` 仍将 `ExecutableSha256` 保存在参数中并强制校验。两条 EXE 摘要不同的记录不能仅靠引用同一 Profile 完成复用。多文件构建记录与采集参数尚未分离。 |
| 目标身份与会话 | `BackendModels.cs` 的 `LaunchContext` 包含包、Profile、后端与参数，没有选中目标的 PID、启动时间、路径或构建证据。FMOD 预检从调用参数读取目标，并记住首次检查的启动时间；它不等于启动上下文已经固定完整目标身份。 |
| 安装与激活 | `PackageStore.Install` 使用 `packs/<id>` 替换原包，成功后删除旧目录；它没有独立的历史版本和当前版本指针，也未在替换前保证后端齐全。后端已有按 ID/版本并存的基础，不能据此声称包回退已经实现。 |
| 来源与完整性 | `BackendStore` 支持显式离线导入并校验描述、文件与固定摘要；未提供独立可信目录、来源认证或撤销。配置包和后端同时被替换时，自填摘要不能证明发布者可信。 |
| 分组与用户数据 | FMOD 可用稳定 ID 映射原生总线；预设、录音仍独立保存。V1 到 V2 的自动预设迁移、分组冲突处理和历史数据兼容仍需专项验证，不能将目录独立视为迁移完成。 |
| 原生接口 | 当前实现按 FMOD 2.02/2.03 分支处理版本查询。接口签名修复与真实采集数据来自 Owner 的验证记录，本次整理没有独立核实对应 SDK 的 ABI 或完成游戏采集。 |

上述代码入口分别位于 `src/ApexAudio.Contracts/`、`src/ApexAudio.Core/` 和 `adapters/Fmod/`。正式修改前应检查调用方与失败路径，避免仅修改模型而保留旧的校验假设。

## 验证证据

- 原始交付报告见根目录 `V2-验证记录.md`，其中 GUI、游戏 PCM 和手柄覆盖范围属于 Owner 报告，不能当作整理分支的复测结果。
- 整理环境使用 .NET 10 SDK 构建 `net8.0-windows` 目标，复用本地 NuGet 缓存；原有 43 项自动验收通过。
- 前端 3 个测试文件通过，元数据测试 4 项通过。
- 源码宿主搭配随包后端的 V2 专项 18 项通过；后端和配置从源码重建后的同一套 18 项也通过，包含 V1 Mock 包导入及启动。两条测试路线分别执行，不能据此宣称二进制逐字节可复现。
- 统一入口的随包后端模式另以 Owner 原样交付的 `release-inputs/dragonsdogma2/ApexAudio-DragonsDogma2-0.1.1.gpack` 运行 18 项并通过。旧 V1 二进制的覆盖范围为导入、启动和查询分组，未执行 Connect 或注入；源码重建后端模式使用 `artifacts/packages/demo.gpack` 作为 V1 回归输入。
- 本次整理未执行原生 ABI probe、GUI、真实游戏采集或物理手柄测试。

自动验收只证明所覆盖条件下的程序行为。协议握手和非零 PCM、分组声音语义、长时间采集稳定性与物理震动体验应分别验证。

### 复现命令

在仓库根目录运行，Zig 参数替换为实际编译器路径。第二条使用随包后端和原样旧 V1 包；第三条从源码重建后端并使用本地构建的 V1 Mock 包。

```powershell
pwsh -NoProfile -File tools/Build.ps1 -Test
pwsh -NoProfile -File tools/TestV2.ps1 -LegacyPack release-inputs/dragonsdogma2/ApexAudio-DragonsDogma2-0.1.1.gpack
pwsh -NoProfile -File tools/TestV2.ps1 -Compiler "你的zig.exe路径" -LegacyPack artifacts/packages/demo.gpack
node --test tests/timeline.test.mjs tests/tree.test.mjs tests/waveform.test.mjs
New-Item -ItemType Directory -Path .cache/tmp -Force | Out-Null
$env:TEMP = (Resolve-Path -LiteralPath .cache/tmp).Path
$env:TMP = $env:TEMP
uv run --no-project tests/metadata_test.py
```

上述临时目录设置仅用于当前 PowerShell 进程，便于在受限写入环境复现元数据测试。

## 后续实施建议

以下为待实施方案，不是当前能力。总体审查 prompt 可保留为核对清单，执行时按单个目标拆分任务。

1. **分离构建证据与采集参数。** 将选中构建的完整证据交给会话，后端复核；用两个不同 EXE 构建引用同一 Profile 的测试检验复用，同时覆盖错误目标在注入前被拒绝。若结构发生不兼容变化，应明确当前试验 `schemaVersion=2` 的迁移或新格式标识。
2. **固定目标身份与清理契约。** 会话固定 PID、启动时间、实际路径及依赖；结构化表达失败和清理结果，覆盖目标退出、重启和后端启动失败。重复检查只能缩小检查与使用之间的竞态窗口。
3. **分离安装、激活与正在使用。** 配置包多版本并存、依赖齐全后激活，更新不改动现有会话；失败不覆盖可用版本。
4. **验证数据兼容。** 明确稳定分组规则及一对一迁移，保留未知设置，新增组默认不参与反馈，回归旧包和历史录音。
5. **完善作者及社区流程。** 先提供静态校验、来源声明和实机反馈模板；在线目录、来源认证与撤销按明确政策另行实现。社区收录与静态检查都不表示 Owner 已实测或认可。

每一步应提交可独立回退的改动及有意义的失败场景测试。未知构建警告放行、Wwise/Unreal V2 和复杂依赖管理不属于当前基线整理范围。
