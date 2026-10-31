# Owner FMOD 试验基线

## 来源和适用范围

本分支 `feat/gamepack-v2` 以目标仓库主分支提交 `233b3f18b956716d09e1fc32f86636f917da6ae7` 为基底。导入提交 `7178199` 保存 Owner 提供的 `ApexAudio-V2-Source-0.2.0` 交付内容；`tests/fixtures/v2/` 保留其中两份配置包和一份预编译后端，作为旧试验格式的回归输入。

当前源码的主程序和 FMOD 后端为 `0.3.0`，实现范围是 FMOD JSON 参数与构建证据分离、真实目标身份固定及启动失败清理。格式与连接契约以[FMOD JSON 试验说明](v2-trial.md)和代码为准。该实现尚不是覆盖全部引擎的完整 V2，生态提案中的包版本激活、完整多文件匹配、迁移和来源认证仍需分别实现。

根目录 `SOURCE-SHA256SUMS.txt` 是原始交付的历史清单；在原始解压目录核对的 178 项均通过。它不描述当前源码或整理后的目录，Git 文本换行规范化也可能改变检出字节。`tests/fixtures/v2/SHA256SUMS.txt` 用于校验保留的样例文件。

## 源码与随包程序

`tests/fixtures/v2/` 的 `.gbackend` 内含 Owner 预编译的 0.2.0 程序和采集 DLL。当前宿主按它的描述生成上下文 1，继续使用原试验参数；运行这些输入不能单独证明新源码后端的行为。

`tools/TestV2.ps1 -Compiler <zig.exe>` 在独立 `artifacts/source-v2-<随机标识>` 目录构建 0.3.0 后端和 `PackV2`，将测试配置的模块证据移至 `games`，写入 FMOD 参数格式 2，更新固定后端版本和描述摘要后再测试。新后端使用上下文 2；这是测试数据准备，不是主程序自动转换旧包的功能，也不修改历史 fixtures。

源码构建、旧后端回归、新后端协议检查和实际游戏采集应分别记录。`tools/BuildV2Backend.ps1` 固定输出 0.3.0；不同内容不能以同一已发布版本重新分发。当前重建产物仅用于本地验证，测试通过也不表示逐字节可复现或实机适配通过。

`tools/Prepare-V2Trial.py`、`tools/Bundle-V2Trial.py` 保留 Owner 本机交付用途，包含本机路径，不是新格式通用构建的依赖。

## 当前契约边界

| 边界 | 代码事实与限制 |
|---|---|
| 构建记录与 Profile | `V2Packages.SelectBuild` 按 EXE 名和摘要选择唯一记录，交付其模块摘要；FMOD 参数格式 2 禁止嵌入构建证据。EXE 摘要不同的构建可复用同一 Profile。相同 EXE 摘要但模块组合不同的多条记录仍会冲突，尚不支持模块摘要消歧或任意多文件匹配。 |
| 目标身份与会话 | 上下文 2 保存选中构建及目标 PID、UTC 启动 ticks、实际路径；FMOD 在预检及 Connect 中复核，注入前重验关键条件。无目标上下文只可离线查询，不能连接游戏。正常关闭及启动失败路径清理上下文；重复检查不能消除全部检查与使用竞态。 |
| 引擎职责 | Core 处理通用包、构建选择和上下文。FMOD 后端解释参数格式、必需模块及定位范围，不允许包漏填关键模块绕过检查。原生采集 ABI 本轮未修改，Lua 未接入。 |
| 安装与激活 | `PackageStore.Install` 仍使用 `packs/<id>` 替换原包并删除旧目录，没有独立历史版本和当前版本指针，也未在替换前保证后端齐全。后端按 ID/版本并存不等于配置包回退已经实现。 |
| 来源与完整性 | 后端显式离线导入，校验描述、文件和固定摘要；没有独立可信目录、来源认证或撤销。自填摘要不能证明发布者可信。 |
| 用户数据 | `SoundPreset` 保存分组显示名、反馈启用、显示文件夹和排序等设置；加载时为新组添加关闭状态并保留旧条目。应用全局偏好、用户预设和录音独立保存；稳定 ID 改变或语义变化时没有通用自动迁移。 |
| 原生接口 | 现有代码按 FMOD 2.02/2.03 分支处理版本查询。修复及真实采集数据来自 Owner 报告，本轮没有独立核实 SDK ABI 或完成游戏采集。 |

稳定入口为 `src/ApexAudio.Contracts/BackendModels.cs`、`src/ApexAudio.Core/BackendStore.cs`、`src/ApexAudio.Core/AdapterClient.cs` 和 `adapters/Fmod/GameDefinition.cs`。修改这些边界时需同时核对调用方与失败路径。

## 验证证据

| 工程检查 | 实际覆盖 |
|---|---|
| 主程序原有自动验收 | 当前代码 43 项通过，覆盖运输、录音、预设、反馈和包校验等既有行为。 |
| 旧后端路线 | 34 项通过，包括 Owner 0.2.0 后端回归、新源码边界测试（含多构建证据的打包/导入往返）及一项 V1 导入/启动/查询分组回归。未指定 `-LegacyPack` 时为 33 项。 |
| 新源码后端路线 | 37 项通过，包括源码构建 0.3.0 后端、重新打包测试配置、身份与上下文检查、离线连接拒绝及启动失败清理。含一项 V1 回归；未指定 `-LegacyPack` 时为 36 项。 |
| 其他基线证据 | 整理基线时前端 3 个测试文件、元数据测试 4 项通过；不据此扩大本轮新契约的验证范围。 |

整理及本轮环境使用 .NET 10 SDK 构建 `net8.0-windows` 目标，复用本地 NuGet 缓存。V1 回归使用原样 `release-inputs/dragonsdogma2/ApexAudio-DragonsDogma2-0.1.1.gpack` 或本地构建的 `artifacts/packages/demo.gpack`，仅覆盖导入、启动和查询分组，未执行 Connect 或注入。

根目录 `V2-验证记录.md` 是 Owner 原始报告，其中 GUI、游戏 PCM 和手柄覆盖范围不能当成本轮复测结果。本轮未执行原生 ABI probe、GUI、真实游戏采集或物理手柄测试。重建原生 DLL 不等于重新验证了音频 ABI。

### 复现命令

在仓库根目录运行，Zig 参数替换为实际编译器路径。旧后端路线使用原样 V1 包，新源码后端路线使用 Build 生成的 V1 Mock 包。

```powershell
pwsh -NoProfile -File tools/Build.ps1 -Test
pwsh -NoProfile -File tools/TestV2.ps1 -LegacyPack release-inputs/dragonsdogma2/ApexAudio-DragonsDogma2-0.1.1.gpack
pwsh -NoProfile -File tools/TestV2.ps1 -Compiler "你的zig.exe路径" -LegacyPack artifacts/packages/demo.gpack
```

其他基线检查：

```powershell
node --test tests/timeline.test.mjs tests/tree.test.mjs tests/waveform.test.mjs
New-Item -ItemType Directory -Path .cache/tmp -Force | Out-Null
$env:TEMP = (Resolve-Path -LiteralPath .cache/tmp).Path
$env:TMP = $env:TEMP
uv run --no-project tests/metadata_test.py
```

上述临时目录设置仅用于当前 PowerShell 进程，便于在受限写入环境复现元数据测试。

## 后续实施建议

以下仍为待实施方案，按独立可回退的目标推进：

1. **补足构建识别。** 先定义实际需要的模块组合消歧和游戏安装根多文件证据，不把当前 EXE 唯一匹配扩大表述为完整提案已经实现。
2. **分离安装、当前选择与会话使用。** 配置包多版本并存，依赖齐全后选择当前版本，切换仅影响下次连接，失败不覆盖可用版本。
3. **完善有限数据迁移。** 明确稳定分组的一对一对应，保留可确认的设置；复杂变化提示重新配置，保留旧项和录音以便回退。
4. **按引擎验证复用边界。** Wwise、Unreal 保留 V1 流程，分别核对运行时参数和原生变体需求。Lua 属于后续扩展，当前先完善 JSON 流程。
5. **完善作者与社区流程。** 静态检查、来源声明和实机反馈由各自流程承担；不增加认证评分或测试管理面板。
