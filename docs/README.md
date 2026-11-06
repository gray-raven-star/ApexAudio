# 文档索引

从[项目首页](../README.md)进入软件下载、适配制作或源码开发。这里收录操作指南、接口格式和维护资料。

## 使用软件

`user/` 收录下载入口、日常操作和当前能力范围。

- [游戏适配包下载](user/gamepack-downloads.md)
- [导入适配包、连接与录音](user/connect-and-record.md)
- [当前能力与限制](user/capabilities.md)

## 制作适配包

`authoring/` 收录新游戏适配流程、调查专题和采集排查。

按 [识别游戏音频接口](authoring/identify-audio-system.md) → [制作适配包](authoring/build-a-gamepack.md) → [验证采集与反馈](authoring/verify-a-gamepack.md) → [发布适配包](authoring/release-a-gamepack.md) 推进。

- 调查专题：[探查 FMOD 运行库](authoring/probe-fmod-runtime.md)、[读取容器元数据](authoring/container-metadata.md)、[分析采集接口](authoring/analyze-capture-interfaces.md)。
- 扩展与排错：[编写自定义采集器](authoring/write-a-custom-adapter.md)、[排查采集与反馈问题](authoring/troubleshooting.md)。

## 格式与接口

`reference/` 记录适配包、采集协议和数据格式的约定。

- [适配包格式](reference/gpack-format.md)
- [适配器 IPC 协议](reference/adapter-protocol.md)
- [各引擎配置字段](reference/engine-config-reference.md)
- [分组证据、名称和效果槽](reference/group-evidence.md)
- [预设与录音格式](reference/presets-recordings.md)

## 开发和维护

`development/` 收录源码架构、构建环境和维护资料。

- [架构与代码入口](development/architecture.md)
- [开发环境与构建](development/build-environment.md)
- [适配器维护与旧 Wwise 配置迁移](development/adapter-maintenance.md)
- [X-Haptics 桥接](development/xhaptics-provider.md)
- [构建应用和开发套件发行版](development/release-builds.md)

[AGENTS.md](../AGENTS.md) 记录编码代理在这个仓库中需要遵守的项目约定。
