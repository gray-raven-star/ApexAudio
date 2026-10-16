# 文档入口

## 使用软件

- [项目首页](../README.md)：下载、导入适配包和运行条件。
- [录音和设置](settings-recording.md)：录制、试听、快捷键和设置。
- [预设与录音格式](presets-recordings.md)：作者预设、用户副本和录音数据。
- [当前功能与限制](status.md)。

## 制作适配包

从[开发入口](../START-HERE.md)开始，按需查阅：

| 文档 | 用途 |
|---|---|
| [构建环境](build-environment.md) | 源码与开发套件的依赖、编译器和诊断工具 |
| [制作指南](gamepack-authoring.md) | 从目标调查到包交付的完整流程 |
| [接口识别](audio-discovery.md) | Wwise、FMOD、Unreal 的复用判断 |
| [调查工具与资料检索](investigation.md) | 静态检查、运行时读取和反汇编 |
| [容器元数据](container-metadata.md) | RE Pak、UE Pak 读取、补丁合并和名称候选 |
| [新模块开发](new-module.md) | 自定义采集器、回调、传输和资源释放 |
| [故障排查](troubleshooting.md) | 按注册、回调、PCM、录音和反馈分层排错 |
| [验证与报告](validation.md) | 诊断命令、报告字段和实测范围 |

编译适配器需要含 `src/`、`adapters/`、`templates/`、`tools/` 的源码或开发套件。运行包中的文档可供查阅，应用目录本身不包含完整开发文件。

## 格式与接口

| 文档 | 内容 |
|---|---|
| [适配包格式](gpack-format.md) | manifest、版本识别、文件清单、导入与更新 |
| [适配器协议](adapter-protocol.md) | 控制命令、独立 PCM、低延迟反馈共享缓冲区 |
| [后端配置字段](engine-config-reference.md) | game.json、字段来源和各后端限制 |
| [分组证据](group-evidence.md) | 名称来源、Wwise 效果槽和构建检查 |

## 维护主程序

- [架构](architecture.md)：宿主、适配器及反馈提供器的职责。
- [开发与迁移](development.md)：Mock、旧 Wwise 配置迁移和原生构建。
- [X-Haptics 接入](xhaptics-provider.md)：反馈桥接和震动遥测。

## 维护记录

- [界面文案讨论](notes/ui-copy-review.md)：保留的修改建议，非当前功能说明。
- [2026-10-14 保活修复](notes/feedback-repair-2026-10-14.md)：当时的调查、版本和验证记录。

技术文档统一维护在 `docs/`，构建与发布从这里复制。`release-docs/` 保存发布包首页、许可说明和图标素材；AI 操作流程见根目录的 [AI-INSTRUCTIONS.md](../AI-INSTRUCTIONS.md)。

单个游戏包的支持版本、已知问题和实际测试范围以其随包 README 为准。
