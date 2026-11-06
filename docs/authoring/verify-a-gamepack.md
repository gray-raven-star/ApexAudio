# 验证适配包采集与反馈

以下命令用于按阶段验证开发结果，各命令的执行范围分别说明。项目构建记录见 release-manifest.json。

## 工具位置

随附 `prebuilt/devtools/ApexAudio.DevTools.exe`。源码在 tools/DevTools，可用 .NET 8 SDK 重建。所有报告支持 `--output <文件.json>`；报告可能包含你电脑的游戏路径，分享前自行检查。

### 完整性与协议

```powershell
prebuilt/devtools/ApexAudio.DevTools.exe check --pack dist/packages/author.example.gpack --output evidence/package.json
prebuilt/devtools/ApexAudio.DevTools.exe check --pack dist/packages/author.example.gpack --launch --output evidence/protocol.json
```

第一条只校验并解压，不执行适配器；第二条执行包里的适配器并读取信息及分组，不连接游戏。只能对来源可信的包使用执行选项。临时目录在报告 scratch 字段中。未连接游戏的检查可在文件不用后清理；capture 加载的模块可能仍被游戏占用，只在目标正常退出后清理对应目录。

### 开发示例音轨

先用 `tools/Build.ps1` 构建，生成的开发示例在 artifacts/packages/demo.gpack。它不随用户版自动安装。

```powershell
prebuilt/devtools/ApexAudio.DevTools.exe check --pack artifacts/packages/demo.gpack --launch --demo-audio --seconds 3 --output evidence/demo.json
```

只证明协议与生成的不同波形可传输，不证明任何真实游戏已适配。

### 实际游戏采集

```powershell
prebuilt/devtools/ApexAudio.DevTools.exe capture --pack dist/packages/author.example.gpack --pid 1234 --allow-game-connection --seconds 5 --output evidence/capture.json
```

与 inspect 不同，这个命令执行包的正常 Connect，可能加载采集 DLL 或挂接 DSP，存在游戏崩溃风险，应在已授权的测试目标上执行。它不启动 X-Haptics，也不发送物理震动。也可以由用户在应用中导入、连接和录音。

报告包含实际分组、收到包数、帧数、各组峰值、首个有效数据块摘要、状态和错误。支持公共 realtimeMix 的包还报告映射 active 与发布块数；没有选中反馈组时混音静音是正常的，不能由此否定独立录音。

realtime.blocks 增长不能证明块内有非零样本；active=1 只代表生产端启用。packets / frames 对应独立音频管道，nonZeroGroups / peaks 才能证明本次观察收到非零 PCM。多条分组的累计 frames 不能直接当作录音时长。

不同组摘要相同可能来自父子内容或真实重复，也可能是错误复制，必须结合路由与试听。非零 PCM 不证明命名和语义正确。

### 原生模块自己的离线测试

```powershell
prebuilt/devtools/ApexAudio.DevTools.exe native-check --module "your-module.dll" --entry "YourSelfTest"
```

在诊断工具自身进程加载并执行指定的单参数 uint32 自测导出，不连接游戏。仅用于你自己或来源可信的模块。不是每个后端都有自测导出；没有时不能编造通过记录。

## 在应用中验证

按普通用户流程导入 `.gpack`，确认来源后连接。发布前至少确认：

- 支持版本连接成功，不匹配版本明确拒绝。
- 逐组试听，确认各轨对应的声音；父子总线包含相同内容时注明重叠关系。
- 分别录制背景音乐／对白和枪声、脚步、撞击等动作场景，记录候选组包含的声音。目标声音仍与背景混在同一轨时，寻找更细路由；无法继续分离的部分写入包说明。
- 录制期间修改反馈选择不会打断录音；全关闭后补充震动输入静音。
- 试听只播放所选录音轨道；声音和反馈时间基准一致。
- 断开、游戏退出及重新连接行为正常；用户预设与还原初始状态正常。

启用 X-Haptics，在相同场景比较全组输入与所选动作组输入，记录背景音是否仍触发明显补充震动、目标动作反馈是否保留、是否有可感知延迟。音轨分离、反馈输入和实体手柄测试分别填写结果。

完成构建和协议检查后填写对应结果；尚未运行的测试保留 `not-tested`，注明缺少的环境。

## 一轮验证如何安排

先确认实际游戏实例、正常启动方式和持续声源，尽量保持前台；由开发者或 AI 启动即可，不强制要求用户手动打开。若游戏后台暂停声音，先恢复声源，不立即重编模块。

首次 PCM 检查先用约 5 秒，不启动 X-Haptics 或做物理震动；只对明确发现的问题追加一次有目的的复查。按任务授权选择后续独立性、重连或手柄测试，用户留给自己测的项目标注未测并交付，不能为了填满报告反复要求重开游戏。

capture 成功退出表示诊断命令完成，不表示游戏音轨已经通过验收。读取实际报告字段和最早失败阶段再下结论。开发包的完整性和编译检查也不等于接收者已经成功完成新游戏适配。

## 阶段记录与交接

`New-Pack.ps1` 已从 `gamepack-starter/validation-report.json` 生成报告。随制作过程更新现有字段，续接时根据证据决定下一步：

| 阶段 | 保存内容 | 报告位置与后续判断 |
|---|---|---|
| 目标与接口调查 | EXE／模块摘要、资源解析和接口依据 | `targetVersionEvidence`、`versions`、`audioInstanceEvidence`；仍缺 ABI、实例或路由时继续调查 |
| 定义与构建 | manifest、后端配置、预设、分组证据和构建结果 | `groupEvidenceFile`、`stages.build`；按构建器的具体错误修正输入，成功后检查生成的 `.gpack` |
| 包与协议 | `check`、`check --launch` 报告 | `stages.packageIntegrity`、`stages.protocol`；协议失败先修启动或消息交互，成功后再测游戏连接 |
| 游戏内采集 | 对象、注册、回调、各轨录音和场景对照 | `stages.resolvedObjects` 至 `stages.independentTracks`；非零 PCM 后仍需试听确认各组含义 |
| 选择与反馈 | 开关、重连、反馈源和手柄测试记录 | `stages.selection`、`disconnectReconnect`、`feedbackSource`、`physicalHaptics`；分别填写结果 |

命令写入 `reproductionCommands`，报告或录音路径写入 `evidenceFiles`，资料版本及来源写入 `sources`。未执行项保留 `not-tested`，在 `notTested` 写明缺少的环境。阶段失败时填写 `blockingStage`，并在 `knownLimitations` 记录具体错误、缺少的字段和下一项验证；停止测试后更新 `cleanup`。

续接先核对目标文件摘要、定义和已有产物。目标版本变化后重新检查相关接口与测试；同一版本已有的证据可继续使用。交付时随源码或定义提供报告及其引用的可分享证据，包内 README 列出已验证版本和限制。


完成适用阶段后，继续[发布适配包](release-a-gamepack.md)。
