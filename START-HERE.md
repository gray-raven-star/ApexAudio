# Apex Audio：适配开发入口

仓库包含 Apex Audio 源码、通用适配器、空白模板和开发文档，不含具体游戏的定义、定位数据或初始配置。这些内容由适配者调查、制作并验证。

在包含 `ApexAudio.sln` 的目录中操作。先按[构建环境说明](docs/build-environment.md)准备工具，再按[游戏适配包制作指南](docs/gamepack-authoring.md)制作。Git 源码不附 Zig 和预编译诊断工具；开发套件的对应位置是 `toolchain/zig/zig.exe` 和 `prebuilt/devtools/ApexAudio.DevTools.exe`，缺少时按环境说明准备。

建议按此顺序阅读：

1. [架构和职责边界](docs/architecture.md)
2. [新游戏适配流程](docs/gamepack-authoring.md)
3. [适配包格式](docs/gpack-format.md)
4. [适配器通信协议](docs/adapter-protocol.md)

选择能区分背景音乐、对白和动作音的场景，逐组试听并检查反馈选择。记录接口发现、构建、音轨及补充震动的测试结果，供游戏更新后复测。
