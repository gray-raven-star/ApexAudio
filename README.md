# Apex Audio

Apex Audio 是面向飞智八爪鱼 6 Pro 的 Windows 游戏音频工具。用游戏适配包采集音乐、对白和音效，选择哪些声音送给飞智 X-Haptics 生成补充震动，也可以录下独立音轨逐组试听。

[English](README.en.md)

## 下载与使用

1. 从[最新稳定版](https://github.com/gray-raven-star/ApexAudio/releases/latest)下载 Windows x64 程序 ZIP，解压后运行 `ApexAudio.exe`。运行需要 .NET 8 Desktop Runtime 和 WebView2 Runtime；补充震动需要受支持的官方 X-Haptics。
2. 从[游戏适配包目录](docs/user/gamepack-downloads.md)下载对应的 `.gpack`。
3. 按[导入适配包、连接与录音](docs/user/connect-and-record.md)开始使用。

## 制作适配包

从包含 `ApexAudio.sln` 的目录操作，先按[构建环境](docs/development/build-environment.md)准备工具。仓库提供采集器、构建工具、[官方适配包源码](https://github.com/gray-raven-star/ApexAudio/tree/main/gamepacks)和可复制的[新包示例](gamepack-starter/README.md)。

1. [识别游戏音频接口](docs/authoring/identify-audio-system.md)，判断可复用的采集器。
2. [制作适配包](docs/authoring/build-a-gamepack.md)，创建游戏定义并构建 `.gpack`。
3. [验证采集与反馈](docs/authoring/verify-a-gamepack.md)，检查独立音轨和实测范围。
4. [发布适配包](docs/authoring/release-a-gamepack.md)，准备下载附件、兼容说明和验证报告。

使用编码代理时，项目约定见 [AGENTS.md](AGENTS.md)。

## 开发主程序

按[构建环境](docs/development/build-environment.md)构建和运行离线测试。查找实现入口从[架构](docs/development/architecture.md)开始，协议、格式及专项资料见[文档索引](docs/README.md)。提交改动前请阅读[贡献说明](CONTRIBUTING.md)。

本项目采用 [MIT License](LICENSE)。第三方来源和许可见 [THIRD-PARTY.md](THIRD-PARTY.md)。
