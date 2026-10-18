# Apex Audio：适配开发入口

先从本页开始。目标是为游戏制作可导入的 `.gpack`：选择哪些游戏声音送给 X-Haptics，减少对白、音乐等声音引起的补充震动。主程序管理连接、预设、分轨录音、试听和反馈；游戏差异放在包内。

## 先做什么

1. 在包含 `ApexAudio.sln` 的目录运行 `pwsh -File tools/Setup.ps1 -Output evidence/setup.json`。完整开发套件附 `toolchain/zig/zig.exe` 和 `prebuilt/devtools/`；仅克隆源码时按 [环境说明](docs/build-environment.md)准备缺失工具。
2. 先读 [最小验证实验](docs/first-probes.md)，不要把尚未验证的版本或结构差异当成阻塞。再阅读 [适配流程](docs/gamepack-authoring.md)，再按 [调查方法](docs/investigation.md)盘点现有实现与目标。先查资源、模块和导出，不预先要求逆向工具。
3. 需要的数据按 [后端配置](docs/backend-configs.md)查；现有实现不覆盖时按 [新模块开发](docs/new-module.md)扩展包内代码。
4. 按 [验证说明](docs/validation.md)取得真实 PCM；遇到问题按 [排查表](docs/troubleshooting.md)处理最早失败的一层。

需要目标运行时，开发者或 AI 可按已确认的正常启动方式自行启动。只有登录、用户选择的离线启动、实际游戏操作或退出保存需要用户处理时，再提出具体请求。不要把“需要游戏运行”写成“必须请用户打开”。

## 按需查阅

- 分组、名称、效果槽与初始预设：[分组依据](docs/group-evidence.md)。
- 软件接口与字节布局：[适配器协议](docs/adapter-protocol.md)、[包格式](docs/gpack-format.md)。
- 查不到隐藏接口时：[有范围的代码分析](docs/code-analysis.md)。这是可选路线，不是每个任务的起点；不要求 REA。
- 记录方法与未解决项：[调查笔记模板](templates/investigation-notes.md)。

套件提供通用源码、空白模板、构建工具和诊断工具。游戏银行、媒体、账号数据、访问凭据和本机历史调查不属于开发依赖。可以参考依法发布的包结构，目标的版本和实际采集能力仍需核对。

完整性、构建、连接、真实非零音频、分组语义和手柄效果是不同结果。按当前任务约定完成验证范围，未测项目如实记录；不要反复测试用户已经明确留给自己验证的项目。
