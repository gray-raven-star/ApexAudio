# 现有采集适配器：先检查这里

项目提供 Wwise、FMOD、Unreal 三个采集实现，以及生成测试声音的 Mock。表内路径相对本目录，另有说明的除外。

| 实现 | 源码和原生模块 | 配置及构建 | 当前边界 |
|---|---|---|---|
| Wwise | `Wwise/Program.cs`、`Wwise/GameDefinition.cs`、`Wwise/native/ApexWwiseCapture.c` | `Wwise/tools/BuildPack.ps1`；[说明](Wwise/README.md) | 已实现一组导出 API 和若干 ABI 分支；引擎版本相同不等于接口和效果器 ABI 均兼容 |
| FMOD Studio | `Fmod/Program.cs`、`Fmod/GameDefinition.cs`、`Fmod/native/ApexFmodCapture.c` | `Fmod/tools/BuildPack.ps1`；[说明](Fmod/README.md) | 当前依赖指定 DLL 名称、C API 和已核对的 System 包装对象布局，最多 64 条 Bus |
| Unreal AudioMixer | `Unreal/Program.cs`、`Unreal/GameDefinition.cs`、`Unreal/native/ApexUnrealCapture.c` | `Unreal/tools/BuildPack.ps1`；[说明](Unreal/README.md) | Submix listener 路线，最多 64 项；原生代码仍含已核对对象布局，不覆盖任意 UE 版本 |
| Mock | `Mock/Program.cs`、`Mock/RealtimeMix.cs` | `tools/Build.ps1`（项目根目录下） | 合成测试声音，只验证宿主协议、录音和反馈传输，不验证游戏适配 |

共同接口在 `src/ApexAudio.Contracts/`，进程调用辅助在 `src/ApexAudio.Windows/`。主程序不按游戏名选择采集技术；包通过入口程序提供实现。

新增采集器见[新模块开发](../docs/authoring/write-a-custom-adapter.md)，诊断和实测分别见[故障排查](../docs/authoring/troubleshooting.md)与[验证报告](../docs/authoring/verify-a-gamepack.md)。

## 制作新游戏适配

先[识别音频接口](../docs/authoring/identify-audio-system.md)，确认差异是配置、定位还是采集实现，再[制作适配包](../docs/authoring/build-a-gamepack.md)。各游戏的实测结果记录在[验证报告](../docs/authoring/verify-a-gamepack.md)中。
