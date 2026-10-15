# 文档入口

## 给新游戏制作适配包

先读 [适配包制作指南](gamepack-authoring.md)。它说明源码目录、现有采集器的复用边界、制作顺序、构建命令与发布检查。

应用运行版的 `docs/` 可以用于查阅协议；编写或编译适配器还需要完整源码项目中的 `adapters/`、`src/`、`gamepacks/`、`tools/` 及其构建依赖。只拿到应用运行版，不能直接编译新的采集模块。

## 格式与接口

| 文档 | 内容 |
|---|---|
| [适配包格式](gpack-format.md) | manifest、版本识别、文件清单、导入与更新 |
| [适配器协议](adapter-protocol.md) | 控制命令、独立 PCM、低延迟反馈共享缓冲区 |
| [预设与录音](presets-recordings.md) | 名称、文件夹、开关、初始预设与录音数据 |
| [架构](architecture.md) | 主程序和包的职责边界 |
| [开发与迁移](development.md) | Mock 参考、旧 Wwise 配置迁移和底层编译 |
| [X-Haptics 接入](xhaptics-provider.md) | 主程序的反馈模块 |

## 使用与状态

- [录音和设置](settings-recording.md)
- [交付状态与限制](status.md)

单个游戏包的支持版本、已知问题和实际测试范围以其随包 README 为准。
