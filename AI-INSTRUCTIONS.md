# AI 辅助制作适配包

为指定游戏版本制作 `.gpack`，让用户选择哪些声音参与补充震动。先读 [START-HERE.md](START-HERE.md) 和[制作指南](docs/authoring/gamepack-authoring.md)。

## 开始时确认

确认游戏 EXE 路径、版本、要分开的声音和测试场景。需要用户启动游戏或进入场景时，说明要观察什么。

在含 `ApexAudio.sln` 的目录工作，按[环境说明](docs/development/build-environment.md)准备编译器和 DevTools，再做只读调查。Git 源码不附 Zig；使用开发套件时先检查工具是否存在。

## 按阶段推进

1. 用 DevTools `inspect` 保存 EXE 摘要、候选资源和运行模块。报告 `truncated=true` 时缩小范围重查。找不到名称或导出时，按[调查方法](docs/authoring/investigation.md)检查加载模块、资源头和接口，并分别记录游戏引擎、音频中间件和资源容器。容器中的资源先核对[读取器支持范围](docs/authoring/container-metadata.md)。
2. 按[接口识别](docs/authoring/audio-discovery.md)和[后端配置](docs/reference/engine-config-reference.md)核对复用条件。现有定位、ABI 和回调均适用时新增定义；结构不同时扩展包内实现；需要新采集路线时从 Custom 骨架开始。游戏版本、地址、资源格式和采集代码放在适配器中，宿主通过公共协议接收数据。
3. 用 `tools/New-Pack.ps1` 创建定义。关键字段记录来源文件、摘要、取得方法和命令；地址写成模块 RVA 或可复现的定位链。Wwise 分组与效果槽按[分组证据说明](docs/reference/group-evidence.md)填写。名称未知时使用稳定编号，试听确认后再标注。
4. 填好清单、后端配置、初始预设和分组证据，运行 `tools/Build-GamePack.ps1`，按报错补齐缺项。程序或模块摘要变化时重新核对版本。`Pack.ps1` 负责完整目录的归档，不执行统一构建入口的配置检查。
5. 按[验证说明](docs/authoring/validation.md)用 DevTools `check` 检查包，再用 `check --launch` 检查适配器启动及查询。查看报告中的状态、错误和音频统计；`outcome=completed` 表示命令执行完成，音轨还需实测。
6. 在已授权目标上运行 `capture --allow-game-connection`，或在应用中连接并录制。采集可能向游戏加载模块，超出当前授权范围时先确认。分别试听背景、对白和动作场景，检查父子重叠、全关闭静音、选择切换和断开重连。反馈测试见制作指南。

遇到失败时按[故障排查](docs/authoring/troubleshooting.md)定位最早失败的一层，先收集该层缺少的证据。已有命令、模板和构建检查负责重复步骤；接口调查和试听结果由适配者核实。

## 扩展采集器时

按[新模块开发](docs/authoring/new-module.md)实现连接、回调、传输和资源释放，消息与共享缓冲格式以[适配器协议](docs/reference/adapter-protocol.md)为准。

- 调用接口前核对函数签名、虚表、结构尺寸、回调阶段和对象释放规则。其他 SDK 版本的资料需在目标程序中验证。
- 音频回调只做有界复制、转换和发布；管道发送和写盘放在回调外。反馈与录音分别保活，空选择输出静音。
- 每条音轨应对应游戏中的实际采集路由。只能取得整体混音时报告整体混音；父组包含子组时保留路由关系，供反馈去重。

## 交付与续接

在定义目录维护 `validation-report.json`，按制作指南的[阶段记录与交接](docs/authoring/gamepack-authoring.md#阶段记录与交接)填写已有字段。每次续接先读报告和证据，核对目标文件摘要及已有产物，再从最早未完成的阶段继续。

交付 `.gpack`、对应源码或定义、依赖来源和报告。遇到阻塞时，记录已完成步骤、报错、缺少的接口或字段，以及下一步如何验证。引用公开资料或其他适配包时，注明版本和来源。
