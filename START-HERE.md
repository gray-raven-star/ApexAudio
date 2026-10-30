# Apex Audio：适配开发入口

仓库包含 Apex Audio 源码、通用适配器、空白模板和开发文档，不含具体游戏的定义、定位数据或初始配置。这些内容由适配者调查、制作并验证。

在包含 `ApexAudio.sln` 的目录中操作。源码和开发套件的工具准备方法见[构建环境](docs/development/build-environment.md)，完整文档见[文档入口](docs/README.md)。

建议按此顺序阅读：

1. [制作适配包](docs/authoring/gamepack-authoring.md)：调查、创建定义、构建和实测。
2. [识别接口](docs/authoring/audio-discovery.md)：判断能否复用现有后端。
3. [配置字段](docs/reference/engine-config-reference.md)：填写目标版本、定位信息和分组。
4. 现有后端不能覆盖时，阅读[新模块开发](docs/authoring/new-module.md)及[通信协议](docs/reference/adapter-protocol.md)。

选择能区分背景音乐、对白和动作音的场景，逐组试听并检查反馈选择。记录接口发现、构建、音轨及补充震动的测试结果，供游戏更新后复测。

使用 AI 协助制作时，将根目录的 [AI-INSTRUCTIONS.md](AI-INSTRUCTIONS.md) 交给它作为操作入口；接口、字段和工具说明按上述文档查阅。

需要目标运行时，开发者或 AI 可按已确认方式自行启动；登录、离线启动选择、存档操作等确需用户处理时才请求协助。已有短时验证通过后，按用户约定交付，避免重复测试。

新增调查工具见[最小验证](docs/authoring/first-probes.md)、[有限代码分析](docs/authoring/code-analysis.md)与[发布说明](docs/authoring/publishing.md)。
