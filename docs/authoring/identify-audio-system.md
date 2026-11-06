# 识别游戏音频接口

按以下顺序调查目标游戏，再判断能否复用现有采集器。项目没有能覆盖所有资源包、SDK 版本和静态音频引擎的自动发现工具；缺少证据的配置项先保留为未知，由开发者或 AI 继续调查。

## 先盘点项目，再调查目标

先读[后端配置与限制](../reference/engine-config-reference.md)，了解现有实现支持的接口，再决定是否扩展包内实现或开发新后端。不能因引擎名称相同就认定兼容。

把目标游戏的主 EXE、发行版本、SHA256、实际音频模块和调查工具／方法写入自己的适配笔记。文件摘要可以用 PowerShell `Get-FileHash -Algorithm SHA256 -LiteralPath <文件>` 获取。不得把开发机的绝对路径、随机堆地址或旧缓存路径写成发布配置。

## 静态与运行时调查

先按[构建环境](../development/build-environment.md)准备 DevTools。使用 `inspect` 记录游戏文件摘要、候选资源和导出。报告出现 `truncated=true` 时缩小资源范围重查。启动游戏后可以核对实际加载的模块，将 PID 和模块名换成当前进程的数据。

```powershell
prebuilt/devtools/ApexAudio.DevTools.exe inspect --exe "D:/Games/Example/Game.exe" --resources "D:/Games/Example" --output evidence/static.json
prebuilt/devtools/ApexAudio.DevTools.exe inspect --exe "D:/Games/Example/Game.exe" --pid 1234 --module Audio.dll --output evidence/runtime.json
```

调查只读，范围限于指定进程和模块。记录游戏构建、模块 SHA-256、采样率、通道和路由；找不到名称时保留稳定编号。需要定位具体对象或核对签名时，再查[分析采集接口](analyze-capture-interfaces.md)。资源在容器内时，先查[容器元数据读取范围](container-metadata.md)。

## 分三类证据

| 证据 | 可以说明 | 不能直接说明 |
|---|---|---|
| 文件名、ASCII／UTF-16 字符串、资源目录 | 候选中间件、资源或名称线索 | 零命中不能排除引擎；命中也不证明当前实例使用了它 |
| PE 导出／导入、模块版本、可解析银行头、Bus／Submix 元数据 | 接口与结构的候选身份 | 有银行或分组不代表现有采集器能取到 PCM |
| 运行中的已初始化实例、成功回调、独立音轨 PCM | 实际采集路径和覆盖范围 | 一次菜单测试不能证明对白、战斗、DLC 等所有场景均覆盖 |

RE Engine、Unreal 等游戏引擎身份不能单独决定音频中间件。`.pak` 是容器命名，不是音频 API；同一游戏引擎的项目可能采用不同音频方案。资源被压缩、封装、保护或代码静态链接时，单纯扫描主 EXE 很容易漏掉线索。

记录字符串扫描的编码和范围。接着核对 PE 导出／导入、旁边 DLL 和资源内部格式；已有加载模块可帮助确认实际调用对象。若磁盘文件与运行时存在差异，需要明确说明调查尚未覆盖什么，不能把没有找到写成不存在。本项目不提供反作弊绕过。

## Wwise：从候选到可复用

1. 查找 Wwise 银行及有效 `BKHD` 结构，读取银行版本、总线编号与父子关系；BNK 可能在容器内部，也可能使用不同扩展名。当前项目不带通用容器解包器，需记录外部读取工具和格式支持范围。
2. 核对实际目标模块的七类接口：IsInitialized、GetAudioSettings、SetBusEffect、LoadBankMemoryCopy／旧 LoadBank、RegisterPlugin、RegisterGlobalCallback、UnregisterGlobalCallback。精确修饰名和已支持别名以 `adapters/Wwise/native/ApexWwiseCapture.c` 为准。
3. 检查实际 SDK ABI、银行头结构、插件虚表、回调签名及可用效果器槽位。银行版本不能单独推出所有 ABI；改变编译年份不会自动实现新 ABI。
4. 接口与 ABI 都覆盖时，独立整理 `game.json`；缺少导出而只有静态代码时，现有基于 GetProcAddress 的定位不够，需在包内扩展，不能假填一个地址字段——Wwise 当前配置没有这个字段。
5. 验证挂载和回调后再验证各组音频。名字匹配、显示分组数量、生成银行都不是成功采样的证据。

## FMOD：找到的是活实例，而不是任意指针

当前实现从配置中的模块全局指针，经包装对象的状态字段和 Studio System 字段取得实例，然后核对 IsValid、版本和 Core 音频格式，并为 Bus 挂接 DSP。

需要获取实际 Core／Studio 版本、Bus GUID／路径，以及已初始化 System 的定位方法。当前原生代码要求 `fmod.dll`、`fmodstudio.dll` 和相应 C API；其他名字、静态链接或不同包装方式可能需要改包内模块。不要把示例游戏的 wrapperRva、stateOffset、studioOffset 原样移给新游戏。

单独离线载入银行得到 GUID 也不证明找到了正在游戏中播放的 System。GUID 要与活动实例中的 Bus 对应；父总线含子声音时，录音相同片段可能是真实重叠，需与错误复制区分。

## Unreal：确认 AudioMixer 和对象布局

资源／配置中的 Submix 名称可提供稳定匹配线索。实际还需核对音频设备实例、名称池布局、Submix 对象与 mixer 映射、监听注册／注销方法及其 ABI。

当前读取器同时依赖 `game.json` 的地址链和原生代码中的布局常量。检查 `adapters/Unreal/native/ApexUnrealCapture.c`，不能只替换五个 RVA 就宣称覆盖另一个 UE 版本。没有实际加载的 Submix 可能暂时不产出音轨；回调必须得到目标分组的真实缓冲，而不是操作系统总混音。

## 最后做一个明确决定

- **数据差异**：现有代码完整覆盖，只新增定义和预设。
- **包内实现差异**：已有路线可行，但需修改定位器、ABI 或格式转换，保留宿主协议。
- **尚无可用采集路线**：列出缺口、证据和开发范围。可以提供研究结果，但不要交付冒充已可用的空壳包。

调查阶段、构建阶段、真实音频阶段和实体反馈阶段应分别记录结果。目标游戏的版本数据、分组与接口必须独立取得；是否允许读取目标游戏历史配置应提前约定。共享的公开源码仓库只包含通用模板，不包含历史扫描目录或具体游戏配置。

## 如何搜资料

按已观察到的接口名、类型名、实际错误码、SDK 系列及结构字段搜索，优先顺序为：官方版本文档→对应版本头文件／原始源码→引擎接口示例→第三方讨论。第三方片段只能提供线索。

资料入口：[Audiokinetic 文档](https://www.audiokinetic.com/library/)、[官方公开头文件仓库](https://github.com/audiokinetic/WwiseIncludes)、[FMOD 文档](https://www.fmod.com/docs/)、[Epic API](https://dev.epicgames.com/documentation/en-us/unreal-engine/API)。核对页面实际版本，最新文档不能直接套给旧版程序。

搜索模板可以是 `"<实际符号>" "<SDK系列>"`、`"<结构名>" "<字段名>"`、`"<错误码>" site:<官方域名>`。若只有其他版本资料，列出差异假设，再用目标中的调用和内存结构验证。保存来源链接、版本、证据与仍未知项，不把整份第三方文档保存进游戏包。

确定复用路径后，继续[创建定义并构建适配包](build-a-gamepack.md)。
