# Owner FMOD 试验基线

## 来源和适用范围

本分支 `feat/gamepack-v2` 以目标仓库主分支提交 `233b3f18b956716d09e1fc32f86636f917da6ae7` 为基底。导入提交 `7178199` 保存 Owner 提供的 `ApexAudio-V2-Source-0.2.0` 交付内容；`tests/fixtures/v2/` 保留其中两份配置包和一份预编译后端，作为旧试验格式的回归输入。

当前源码的主程序和 FMOD 后端为 `0.3.0`，支持 FMOD JSON 参数与构建证据分离、EXE/实际加载模块的完整记录匹配、固定候选集与目标身份的会话准备；包安装版本与当前默认选择分别管理。格式与连接契约以[FMOD JSON 试验说明](v2-trial.md)和代码为准。静态启用预检、作者工具与后端使用同源参数契约，预设采用有限解析；其他引擎共享化及来源认证仍未提供，不能视为完整 V2 已交付。

根目录 `SOURCE-SHA256SUMS.txt` 是原始交付的历史清单；在原始解压目录核对的 178 项均通过。它不描述当前源码或整理后的目录，Git 文本换行规范化也可能改变检出字节。`tests/fixtures/v2/SHA256SUMS.txt` 用于校验保留的样例文件。

## 源码与随包程序

`tests/fixtures/v2/` 的 `.gbackend` 内含 Owner 预编译的 0.2.0 程序和采集 DLL。当前宿主按它的描述生成上下文 1，继续使用原试验参数；运行这些输入不能单独证明新源码后端的行为。

`tools/TestV2.ps1 -Compiler <zig.exe>` 在独立 `artifacts/source-v2-<随机标识>` 目录构建 0.3.0 后端和 `PackV2`，将测试配置的模块证据移至 `games`，写入 FMOD 参数格式 2，更新固定后端版本和描述摘要后再测试。新后端使用上下文 2；这是测试数据准备，不是主程序自动转换旧包的功能，也不修改历史 fixtures。

源码构建、旧后端回归、新后端协议检查和实际游戏采集应分别记录。`tools/BuildV2Backend.ps1` 固定输出 0.3.0；不同内容不能以同一已发布版本重新分发。当前重建产物仅用于本地验证，测试通过也不表示逐字节可复现或实机适配通过。

`tools/Prepare-V2Trial.py`、`tools/Bundle-V2Trial.py` 保留 Owner 本机交付用途，包含本机路径，不是新格式通用构建的依赖。

## 当前契约边界

| 边界 | 代码事实与限制 |
|---|---|
| 构建记录与 Profile | `BuildMatcher` 按每条记录的 EXE 和实际加载模块独立完整比较。同 Profile 的完整命中保留候选集，不同 Profile 冲突或必要证据不足时停止准备；不拼接记录，也不扫描任意安装根。进程候选列表仍仅以 EXE 信息提示。 |
| 目标身份与会话 | `PreparedSession` 固定包版本、目标、上下文和匹配结果快照，准备成功后才断开旧连接；使用保护一次性交给启动客户端。上下文 2 的 Candidates 保存原候选集合，Build 保留兼容投影；后端独立重采证据、仅复核原候选并在注入前重验。上下文 2 无目标时仅离线查询，Owner 0.2.0 上下文 1 保留原离线启动后 Connect 流程；重复检查不能消除全部检查与使用竞态。 |
| 引擎职责 | Core 处理通用包、构建选择和上下文。FMOD 后端解释参数格式、必需模块及定位范围，不允许包漏填关键模块绕过检查。原生采集 ABI 本轮未修改，Lua 未接入。 |
| 参数支持与作者流程 | 后端可声明各变体的参数格式、版本、尺寸和约束。Core 通过通用接口接受验证器，Desktop 注入第一方纯数据 FMOD 验证器，作者工具复用它；不运行后端或加载第三方插件。全部 Profile 的静态检查用于就绪、当前选择和准备；未知声明或缺匹配验证器拒绝。PackV2 提供待填写模板、诊断和原子打包；旧 0.2.0 无支持声明仍沿原流程。 |
| 安装与当前选择 | 新包按 ID/版本保存，导入不改变当前；包内容、固定依赖及适用的静态参数预检通过后才设为当前，失败保留旧选择。回退可改选旧版，旧安装继续被发现。会话持有版本使用保护，当前及在用版本不可直接删除；改选不替换会话版本。 |
| 来源与完整性 | 后端显式离线导入，校验描述、文件和固定摘要；没有独立可信目录、来源认证或撤销。自填摘要不能证明发布者可信。 |
| 用户数据 | `SoundPreset` 保存分组显示名、反馈启用、显示文件夹和排序等设置。`ResolveForGroups` 生成副本，复用已有预设时按稳定 ID 保留、尚无设置且未明确映射的新组关闭、历史留存；允许调用方明确一对一映射，不推断复杂变化、不自动写源。工作台显示重配与父混音包含关系提示，解析不关闭原父混音。全局偏好、用户预设和录音独立保存，没有通用无损迁移。 |
| 原生接口 | 现有代码按 FMOD 2.02/2.03 分支处理版本查询。修复及真实采集数据来自 Owner 报告，本轮没有独立核实 SDK ABI 或完成游戏采集。 |

稳定入口为 `src/ApexAudio.Contracts/BuildMatching.cs`、`BackendModels.cs`、`src/ApexAudio.Core/PreparedSession.cs`、`AdapterClient.cs` 和 `adapters/Fmod/GameDefinition.cs`。包和依赖管理仍由 `PackageStore`、`BackendStore` 承担；修改这些边界时需同时核对调用方与失败路径。

## 验证证据

| 工程检查 | 实际覆盖 |
|---|---|
| 主程序自动回归 | 覆盖运输、录音、预设、反馈和包校验，以及版本选择、在用保护、完整记录匹配、候选集快照和准备失败保留旧连接。 |
| 旧后端路线 | Owner 0.2.0 后端回归、新源码边界及多构建证据打包/导入往返；可加 V1 导入、启动和查询分组回归。 |
| 新源码后端路线 | 源码构建 0.3.0 后端、重新打包测试配置、身份与候选集上下文检查；绕过宿主直接请求后台后端时，预检和 Connect 仍独立拒绝缺失模块/错误目标，未加载真实采集代码。 |
| FMOD 连接生命周期检查 | 使用合成生产者缓冲覆盖无效组保护、空选择、独立传输停止/恢复、心跳保持、在途帧及目标丢失；不包含真实 FMOD/DSP 或硬件反馈。 |
| 作者与初始化检查 | 覆盖模板占位、参数组合、预设引用及坏预设诊断、失败无正式包和原文件保留；后端初始化参数错误经 IPC 返回。没有连接真实游戏。 |
| 静态启用预检 | 覆盖全部 Profile 的参数及必需证据声明、未知契约拒绝、失败保留旧默认和准备失败释放版本保护；直接 IPC 请求仍由后端独立拒绝无效参数。没有连接真实游戏。 |
| 预设解析与提示检查 | 覆盖独立副本、明确映射边界、历史保留和父混音提醒；树渲染检查横杠，模拟适配器界面确认提示可见。没有验证真实游戏路由或物理反馈。 |
| 模拟适配器界面检查 | 当前默认与会话使用版本分别展示，改默认不改变已有会话；记录不代表真实游戏或手柄验收。 |
| 其他基线证据 | 整理基线时前端 3 个测试文件、元数据测试 4 项通过；不据此扩大本轮新契约的验证范围。 |

整理及本轮环境使用 .NET 10 SDK 构建 `net8.0-windows` 目标，复用本地 NuGet 缓存。V1 回归使用原样 `release-inputs/dragonsdogma2/ApexAudio-DragonsDogma2-0.1.1.gpack` 或本地构建的 `artifacts/packages/demo.gpack`，仅覆盖导入、启动和查询分组，未执行 Connect 或注入。

根目录 `V2-验证记录.md` 是 Owner 原始报告，其中 GUI、游戏 PCM 和手柄覆盖范围不能当成本轮复测结果。当前界面证据来自模拟适配器；未执行原生 ABI probe、真实游戏采集或物理手柄测试。重建原生 DLL 不等于重新验证了音频 ABI。

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

1. **按引擎验证复用边界。** Wwise、Unreal 保留 V1 流程，分别核对运行时参数和原生变体需求。Lua 属于后续扩展，当前先完善 JSON 流程。
2. **完善社区与工程实机覆盖。** 静态检查、来源声明和实机反馈由各自流程承担；不增加认证评分或测试管理面板。首期不引入任意安装根发现或磁盘全扫描。
