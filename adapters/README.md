# 现有采集适配器：先检查这里

项目提供 Wwise、FMOD、Unreal 三个采集实现，以及生成测试声音的 Mock。表内路径相对本目录，另有说明的除外。

| 实现 | 源码和原生模块 | 配置及构建 | 当前边界 |
|---|---|---|---|
| Wwise | `Wwise/Program.cs`、`Wwise/GameDefinition.cs`、`Wwise/native/ApexWwiseCapture.c` | `Wwise/tools/BuildPack.ps1`；[说明](Wwise/README.md) | 已实现一组导出 API 和若干 ABI 分支；引擎版本相同不等于接口和效果器 ABI 均兼容 |
| FMOD Studio | `Fmod/Program.cs`、`Fmod/GameDefinition.cs`、`Fmod/native/ApexFmodCapture.c` | `Fmod/tools/BuildPack.ps1`；[说明](Fmod/README.md) | 当前依赖指定 DLL 名称、C API 和已核对的 System 包装对象布局，最多 64 条 Bus |
| Unreal AudioMixer | `Unreal/Program.cs`、`Unreal/GameDefinition.cs`、`Unreal/native/ApexUnrealCapture.c` | `Unreal/tools/BuildPack.ps1`；[说明](Unreal/README.md) | Submix listener 路线，最多 64 项；原生代码仍含已核对对象布局，不覆盖任意 UE 版本 |
| Mock | `Mock/Program.cs`、`Mock/RealtimeMix.cs` | `tools/Build.ps1`（项目根目录下） | 合成测试声音，只验证宿主协议、录音和反馈传输，不验证游戏适配 |

共同接口在 `src/ApexAudio.Contracts/`，进程调用辅助在 `src/ApexAudio.Windows/`。主程序不按游戏名选择采集技术；包通过入口程序提供实现。

新增采集器见[新模块开发](../docs/new-module.md)，诊断和实测分别见[故障排查](../docs/troubleshooting.md)与[验证报告](../docs/validation.md)。

## 新游戏的起点

1. 阅读 [新游戏指南](../docs/gamepack-authoring.md) 和 [接口发现流程](../docs/audio-discovery.md)。
2. 识别候选音频系统，并检查这里已有实现需要的接口。没有字符串命中，不足以排除某种音频系统。
3. 用 [配置字段参考](../docs/engine-config-reference.md) 列出缺少的数据，并记录每项来源。
4. 确定差异仅为数据后才创建新配置；有 ABI、定位或回调差异时，在相应适配器中扩展。不要重复实现现有 IPC、录音转送和实时混音协议。
5. 按 [环境说明](../docs/build-environment.md) 构建。构建成功和模拟自测均不能代替真实游戏验证。

各游戏定义是参考数据，不是通用探测规则。此项目没有附带自动扫描任意游戏、解包任意资源或猜测 SDK ABI 的完整工具链。
