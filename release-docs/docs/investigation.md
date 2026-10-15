# 如何调查目标与检索资料

## 建立证据，而不是猜引擎

分别记录渲染引擎、音频中间件、资源容器、接口可见性和版本。这几项互相独立；一个渲染引擎可以使用不同音频系统，同一扩展名也可以承载不同内容。

先用 DevTools inspect 检查 EXE 文件摘要、命名导出和资源候选。资源检查有文件数量上限，报告 truncated=true 时不能声称已经扫完。工具只读取裸银行头，不负责解密或解包容器。

没有 Wwise/FM​OD 等名称时继续检查模块清单、导入依赖、配置、银行头及程序运行后的模块。名称被裁剪、静态链接、加壳、延迟加载，都可能让磁盘字符串扫描没有结果。没有导出也不等于不能实现其他采集入口。

## 为什么需要打开游戏

游戏启动后会初始化音频设备、加载 DLL、创建 System / Submix 对象和路由。ASLR 会改变模块基址，堆对象地址每次也会变；磁盘上的名称和地址不一定反映已初始化进程。

可先让用户停在持续有音乐的画面；若目标资源尚未载入，再进入实际场景。只读核对使用 PID、模块基址和有限大小的 ReadProcessMemory，不写进程、不加载 DLL。

```powershell
prebuilt/devtools/ApexAudio.DevTools.exe inspect --exe "D:/Games/Example/Game.exe" --pid 1234 --output evidence/runtime.json
```

工具可读取选定模块的运行时导出，不自动寻找任意隐藏函数或遍历所有堆对象。进一步核对需要开发者使用只读调试器或自己写有范围的读取器：沿已知调用和引用确认设备、对象类型、结构尺寸、名称、父组与实际方法。记录推导过程，不做无界内存扫瞄。

已有候选地址后，可保存小段数据或函数代码：

```powershell
prebuilt/devtools/ApexAudio.DevTools.exe read-memory --pid 1234 --address 0xADDRESS --bytes 256 --binary evidence/snippet.bin --output evidence/snippet.json
```

`0xADDRESS` 必须用实际已核对的地址替换；单次最多 16 KiB，不写进程。堆地址每次可能变化；报告中记录地址来源和模块基址，不能把它直接写入发布配置。

如需反汇编，另安装可选 Python 3 与 Capstone，不影响 .NET 主构建：

```powershell
py -m venv .venv
.venv/Scripts/python -m pip install capstone==5.0.6
.venv/Scripts/python tools/Disassemble.py --input evidence/snippet.bin --base 0xADDRESS
```

该脚本只解码已取得的 x64 小段代码，不读游戏、不自动定位。Capstone 来源为 [原始项目](https://github.com/capstone-engine/capstone)，文档和许可由安装包提供；网络安装由评审者执行。

## 定位的方法

1. 优先使用正式导出、公开插件接口、游戏允许的扩展接口或可用元数据。
2. 找到相关注册、加载或设备访问方法后，沿调用者与数据引用核对对象来源。
3. 用类型、虚表、对象名称、版本等多项条件验证候选；找到一个相似指针不够。
4. 模块位置记录为 RVA 或可靠的定位规则，禁止把本次绝对地址写死。堆对象应通过指针链、句柄或正式 API 获取。
5. 函数签名、虚函数声明顺序、参数宽度、枚举、结构对齐和释放规则都要核对。Windows x64 调用约定一致也不代表不同 SDK 的 C++ 虚表布局一致。
6. 实际注册采集器是另一个阶段，可能向游戏加载代码或创建 DSP。与只读检查分开操作，确保当前授权覆盖它，并说明崩溃风险和停用办法。不要绕过反作弊；受限时换允许测试的目标。

## 资源读取的开发方法

已知容器优先研究格式作者、引擎官方文档或原始读取器实现；确认版本、索引、压缩及编码，再做只提取元数据的读取器。用目录索引和必要范围读取，避免整包解压。遇到未知压缩或加密，应报告具体缺口，不把修改扩展名当解包。

银行中的对象 ID、路由与 GUID 可以辅助配置；名称可能被删除或只剩哈希。哈希匹配只能标注候选来源，最终命名由试听确认。读出银行或分组不证明运行时可采集。

对已经取得的原始小端 Wwise 银行，可执行 `ApexAudio.DevTools.exe bank-index --file <银行路径> --output evidence/bank.json`，读取 chunk 与 HIRC 对象目录。它不解码不同版本的对象内部字段，不将所有对象当成音轨；路由、效果槽及名称需由对应版本读取器继续核对。

FMOD Studio 可沿对应版本官方 API 的 Bank / Bus 枚举、ID 和路径方法取得元数据；Unreal 可从注册的 Submix 对象、名称池和实际父组读取元数据。需要离线加载资源时，使用允许的匹配 SDK，核对加载／释放规则；本套件不携带其专有运行库。新读取器应输出稳定中间格式并明确版本边界，而不是依赖某款游戏的固定安装路径。

## 如何搜资料

按已观察到的接口名、类型名、实际错误码、SDK 系列及结构字段搜索，优先顺序为：官方版本文档→对应版本头文件／原始源码→引擎接口示例→第三方讨论。第三方片段只能提供线索。

资料入口：[Audiokinetic 文档](https://www.audiokinetic.com/library/)、[官方公开头文件仓库](https://github.com/audiokinetic/WwiseIncludes)、[FMOD 文档](https://www.fmod.com/docs/)、[Epic API](https://dev.epicgames.com/documentation/en-us/unreal-engine/API)。核对页面实际版本，最新文档不能直接套给旧版程序。

搜索模板可以是 `"<实际符号>" "<SDK系列>"`、`"<结构名>" "<字段名>"`、`"<错误码>" site:<官方域名>`。若只有其他版本资料，列出差异假设，再用目标中的调用和内存结构验证。保存来源链接、版本、证据与仍未知项，不保存整份第三方文档进游戏包。

## 交接记录

保留可复现命令、数据来源、接口证据、版本匹配规则及失败阶段。用户数据、访问令牌、全量内存和游戏媒体不能混入通用开发套件。分享前删去本机隐私路径，并明确记录实际验证过的游戏版本和结果。
