# 开发文档

先读根目录 [START-HERE](../START-HERE.md)，不需要一次读完全部文档。

| 任务 | 文档 |
|---|---|
| 环境与首次构建 | [构建环境](build-environment.md) |
| 在宣布不兼容前做小实验 | [最小验证](first-probes.md) |
| 从空白到完整包 | [适配流程](gamepack-authoring.md) |
| 找资源、接口与运行实例 | [调查方法](investigation.md) |
| 复用现有 Wwise / FMOD / Unreal | [后端边界](backend-configs.md)、[字段参考](engine-config-reference.md) |
| 实现不同定位器或新采集模块 | [新模块开发](new-module.md) |
| 分组名称、FX 槽、初始预设 | [分组依据](group-evidence.md) |
| 空数据、重复音轨、延迟与模块残留 | [链路排查](troubleshooting.md) |
| 诊断命令与实测范围 | [验证说明](validation.md) |
| 确有缺口才做反编译 | [有限代码分析](code-analysis.md) |
| 包格式、控制与音频传输 | [包格式](gpack-format.md)、[适配器协议](adapter-protocol.md) |
| 用户设置和时间轴 | [预设与录音](presets-recordings.md)、[录音设置](settings-recording.md) |
| 职责边界与反馈提供器 | [架构](architecture.md)、[X-Haptics](xhaptics-provider.md) |

开发包包含源码、空白模板、可移植 Zig 和预编译诊断工具。运行包的 docs 只供查阅，不能替代完整开发项目。只克隆源码时按环境说明准备缺少的工具。

实际游戏运行验证可由开发者或 AI 按已确认方式启动，不固定要求用户手动打开。验证范围按当前任务约定，资源、注册、真实 PCM 与物理反馈分别记录。
