# 现有后端配置与复用条件

宿主只理解 manifest 与统一音频协议。以下 game.json 属于各适配器，不是宿主内置的游戏表。模板以 `backend` 字段选择构建器；引擎适配器忽略该额外字段。

## Wwise

参考 `adapters/Wwise/GameDefinition.cs` 和 `native/ApexWwiseCapture.c`，最多 1024 个可采集组。需要实际引擎模块具有可解析的受支持 SoundEngine 接口。当前导出解析器包含已实现的类型签名变体；只有相似符号不能证明 ABI 一致。

| 字段 | 填写依据 |
|---|---|
| title、gameSha256 | 游戏显示名、EXE 文件摘要 |
| engineModule、engineSha256 | 实际引擎模块文件名和摘要；静态链接时可能是主程序 |
| bankVersion | 实际银行 BKHD 中的格式版本，不等同 SDK 年份 |
| bankHeader | 目标格式的 BKHD 内容（不含标签及 chunk 长度），Base64；28～64 字节，首个 uint32 匹配 bankVersion |
| sdkYear、sdkSeries | 经文档和实际接口核对的 SDK 系列；不能由年份字符串单独猜定 |
| bankId | 为包的临时采集效果银行选用的稳定 ID，避免与游戏银行及其他工具冲突 |
| captureFile | 包内原生模块相对路径；构建器写入 captureSha256 |
| buses | 含 id、parent、name、nameSource、category、auxiliary、slot 的数组 |

`buses.id` / parent 是 uint32，parent=0 表示无父节点。非 auxiliary 且 slot>=0 的条目参与捕获；slot=0..3 必须是经确认可用的效果槽，不能覆盖游戏已有 DSP 而改变声音。slot=-1 仅显示元数据。辅助返回节点与普通混音总线不能混用。名称无法确认时保留编号并标明来源。

资源侧需要支持实际容器和银行格式的读取器。套件未附任意游戏解包器，不能声称仅执行构建命令就能读出银行。不要把整份银行或游戏媒体纳入发布包。

## FMOD Studio

参考 `adapters/Fmod/`。当前实现使用 FMOD 2.02 C API、DSP SDK 110、48 kHz、最多 64 条 Studio Bus、每块最多 2048 帧、1～16 声道下混立体声。

| 字段 | 填写依据 |
|---|---|
| name、executable、executableSha256 | 显示名、主程序文件名及摘要 |
| modules | 所有实际调用模块的文件名→SHA256；当前原生实现使用 fmod.dll 与 fmodstudio.dll |
| wrapperModule、wrapperRva | 包装对象全局指针所在模块及相对模块基址的位置 |
| stateOffset、studioOffset | 包装对象中初始化状态（当前实现要求值 2）和 Studio System 句柄位置 |
| runtimeVersion | FMOD 实际返回的版本整数，而非任意显示字符串 |
| buses | 稳定 GUID、原始 path、name、parentId、category、containsChildren |

Bus 的 id 为 16 字节 GUID 的实际小端内存 hex（32 个十六进制字符），不能直接误用带短横线的打印顺序。GUID 与 path 应来自银行工具／API 或经验证的数据，不靠声音语义猜测。当前定位器只支持以上包装方式，换一种 System 存储方式必须修改包内定位代码。构建器写入 captureFile / captureSha256。

## Unreal AudioMixer

参考 `adapters/Unreal/`。当前实现支持已核对的 FName 块布局、AudioMixer 对象布局与原始指针监听接口，最多 64 个 Submix、48 kHz、1～16 声道、每块最多 2048 帧。它不是任意 Unreal 版本读取器。

| 字段 | 填写依据 |
|---|---|
| executable、executableSha256 | 主程序文件名及文件摘要 |
| engineRva | 模块中引擎对象全局指针的相对位置 |
| holderOffset、deviceOffset | 从引擎对象到音频设备的指针链成员位置 |
| vtableRva | 用来确认最终设备类型的虚表相对位置 |
| nameBlocksRva | FName 名称池块指针表位置 |
| registerRva、unregisterRva | 与已实现签名一致的监听注册／移除方法位置 |
| buses | id、nativeName、显示 name、parentId、category、containsChildren |

`nativeName` 按引擎对象名匹配。不能把本次进程的堆地址当作稳定 ID。连接后以实际对象和运行时路由更新可用性；没加载的对象不能标成已有独立声音。

设备集合、名称池、父组、监听器布局中仍有当前原生代码的结构约束。遇到差异要扩展包内 C 模块或建立新后端，单改 JSON 无法覆盖所有差异。必须核对监听方法实际调用槽、参数和生命周期，不以另一版本头文件代替证据。

## 兼容性核对

SDK 110 是插件接口编号，不是 FMOD 2.02 的同义词；2.03 的部分 C API 也可能有参数变化。先按 [最小验证](../authoring/first-probes.md)逐项核对，不整体推断兼容或不兼容。包装对象状态=2 是当前定位器假设，不是任何游戏都必须满足的结构。

## 新后端

Custom 配置完全由自己的适配器解释。只有 manifest、预设和公共传输协议是所有后端共同要求。不得将未知后端硬套成已知后端，或放宽摘要检查以跳过布局验证。

完整字段说明见 [配置参考](engine-config-reference.md)，来源和效果槽记录见 [分组依据](group-evidence.md)。
