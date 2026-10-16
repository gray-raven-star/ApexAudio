# 文档入口

使用说明位于 `user/`；适配制作方法位于 `authoring/`；格式和字段位于 `reference/`；架构、环境及实现维护位于 `development/`。

## 使用软件

- [项目首页](../README.md)：下载、导入适配包和运行条件。
- [连接、录音和设置](user/settings-recording.md)。
- [当前能力与限制](user/capabilities.md)。

## 制作适配包

从[开发入口](../START-HERE.md)开始，按需查阅：

| 文档 | 用途 |
|---|---|
| [制作指南](authoring/gamepack-authoring.md) | 从目标调查到包交付的完整流程 |
| [接口识别](authoring/audio-discovery.md) | Wwise、FMOD、Unreal 的复用判断 |
| [调查工具与资料检索](authoring/investigation.md) | 静态检查、运行时读取和反汇编 |
| [容器元数据](authoring/container-metadata.md) | RE Pak、UE Pak 读取、补丁合并和名称候选 |
| [新模块开发](authoring/new-module.md) | 自定义采集器的实现要求、回调、传输和资源释放 |
| [故障排查](authoring/troubleshooting.md) | 按注册、回调、PCM、录音和反馈分层排错 |
| [验证与报告](authoring/validation.md) | 诊断命令、报告字段和各阶段验收要求 |

实现要求和验收项目供开发者逐项完成；目标游戏实际完成的测试记录在随包 README 和验证报告中。

## 格式与接口

| 文档 | 内容 |
|---|---|
| [适配包格式](reference/gpack-format.md) | manifest、版本识别、文件清单、导入与更新 |
| [适配器协议](reference/adapter-protocol.md) | 控制命令、独立 PCM、低延迟反馈共享缓冲区 |
| [后端配置字段](reference/engine-config-reference.md) | game.json、字段来源和各后端限制 |
| [分组证据](reference/group-evidence.md) | 名称来源、Wwise 效果槽和构建检查 |
| [预设与录音格式](reference/presets-recordings.md) | 作者预设、用户副本和录音数据 |

## 开发和维护

- [构建环境](development/build-environment.md)：源码与开发套件的依赖、编译器和诊断工具。
- [架构](development/architecture.md)：宿主、适配器及反馈提供器的职责。
- [适配器维护与迁移](development/adapter-maintenance.md)：Mock、旧 Wwise 配置迁移和原生构建。
- [X-Haptics 接入](development/xhaptics-provider.md)：反馈桥接和震动遥测。

## AI 制作入口

[AI-INSTRUCTIONS.md](../AI-INSTRUCTIONS.md) 用于指导 AI 按阶段调查、制作、验证和交接；技术细节引用上述说明。

公开文档统一在本目录维护。开发套件包含这些文档、源码及 AI 制作入口；普通运行包仅携带 `user/` 使用说明和许可。`packaging/` 保存发布首页模板及图标素材。
