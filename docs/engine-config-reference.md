# 现有适配器的 game.json 字段

配置格式由适配器决定，不属于宿主 V1 协议。本文对应当前源码；修改结构时请同步维护文档、序列化和原生配置布局。数字在 JSON 中使用十进制，不直接写 `0x...`。模块相对地址 RVA 与对象内字段偏移是不同概念；每次启动变化的绝对地址不能写成 RVA。

## 通用约定

清单格式见 [gpack-format](gpack-format.md)，初始预设格式见 [presets-recordings](presets-recordings.md)。清单与配置的目标游戏摘要应一致，摘要使用实际文件 SHA256。

`captureFile` 为包内原生 DLL 相对路径；`captureSha256` 为它的实际 SHA256，BuildPack 自动填入构建产物。两者不是目标游戏摘要。源定义的空模块摘要不能直接拿去发布。父组 ID 必须存在或为空根，不允许循环；稳定 ID 不是界面行号。

配置中的分组名称来自游戏元数据、编号或人工标注；`category` 是展示大类，不决定引擎路由。未能确认来源时不要标成游戏原名。初始开关由预设提供，适配器仍录制所有请求的独立音轨。

## Wwise

实现：`adapters/Wwise/GameDefinition.cs`、`Program.cs` 和 `native/ApexWwiseCapture.c`。目标游戏定义不随公开源码仓库提供。

| 字段 | 类型／来源／用途 |
|---|---|
| title | 显示名称，字符串 |
| gameSha256 | 主程序文件 SHA256；Connect 核对 |
| engineModule | 实际包含 Wwise 导出的模块文件名，可以是 EXE；不是安装绝对路径 |
| engineSha256 | 上述模块文件 SHA256 |
| bankVersion | 实际银行版本整数，来自已解析银行头；编译参数需对应 |
| bankHeader | base64 编码的版本相关银行头数据；当前读取器要求 28～64 字节，前四字节小端整数等于 bankVersion；不是整个 Init.bnk，也不是可随意编造的空头 |
| bankId | 采集器构建插件银行所使用的 uint32 编号；应核对不会与目标已加载内容冲突，不是某条声音的编号 |
| sdkYear / sdkSeries | 已核对的 SDK 年份／系列整数；对应原生 ABI 和编译参数，不能仅靠猜测银行版本填写 |
| captureFile / captureSha256 | 构建输出模块路径和摘要；Wwise 脚本使用定义中的模块文件名，建议新版本或内容变化使用新文件名 |
| buses | 下表的全部元数据；只有非辅助且 slot ≥ 0 项进入采集路由，最多 1024 项 |

| buses 每项 | 含义 |
|---|---|
| id | uint32 Bus ID，来自目标银行／元数据，不使用排序下标 |
| parent | uint32 父 Bus ID；0 表示没有已知父项 |
| name / nameSource | 名称及来源；未知名称可空，适配器显示编号 |
| category | 展示大类，缺省为“其他” |
| auxiliary | true 表示辅助混音元数据，不作为普通音轨输出 |
| slot | 实际可挂载效果器的槽位；-1 表示不采集。必须核对目标 DSP 链，不能用编号推测 |

普通 Bus（包括没有槽位的显示节点）形成 `GetAudioGroups`；非负 slot 决定 `hasPcm`。适配器根据真实父关系推导 `containsChildren`。预设的组键为十进制 `id` 字符串。

如何取得字段：银行解析用于版本、编号与层级；模块导出／SDK 结构用于接口和 ABI；实际效果器路由核对用于 slot。项目未内置从任意新银行生成全部这些字段的扫描命令。历史配置迁移只是可选工具，不能替代新游戏独立发现。

## FMOD Studio

实现：`adapters/Fmod/`。目标游戏定义不随公开源码仓库提供。

| 字段 | 类型／来源／用途 |
|---|---|
| name / executable | 显示名称、目标主程序文件名 |
| executableSha256 | 主程序文件 SHA256 |
| modules | 模块文件名 → SHA256 的字典；Connect 核对每项 |
| wrapperModule | 保存包装对象全局指针的模块名 |
| wrapperRva | uint32，该全局指针相对模块基址的位置；不是 System 绝对地址 |
| stateOffset | 包装对象内 uint32 初始化状态字段偏移，当前约定值为 2，配置上限 4092 |
| studioOffset | 包装对象内 Studio System 指针字段偏移，配置上限 4088 |
| runtimeVersion | `FMOD_System_GetVersion` 的整数结果；例如 2.02.21 编码为 0x00020221，JSON 写其十进制整数 |
| captureFile / captureSha256 | BuildPack 生成带内容摘要的模块名和校验值 |
| buses | 1～64 条，顺序固定，并与原生索引一致 |

| buses 每项 | 含义 |
|---|---|
| id | FMOD GUID 的 16 字节小端原始内存 hex，共 32 个十六进制字符；不是直接去掉短横线的标准 GUID 字符串 |
| path | 原始 Bus 路径 |
| name | 显示名 |
| parentId | 实际父组 ID 字符串或 null；不要只凭显示文件夹推断 |
| category | 展示大类 |
| containsChildren | 此 Bus 输出是否包含真实子 Bus 音频 |

GUID、路径和活动路由来自银行元数据／活动 Studio System。现有定位器只处理“模块全局指针 → 包装对象 → Studio 指针”这条链，其他实例管理方式须扩展包内实现；当前 DSP SDK 常量为 110，并要求已有模块/API 与 48 kHz 格式匹配。

## Unreal AudioMixer

实现：`adapters/Unreal/`。目标游戏定义不随公开源码仓库提供。

| 字段 | 类型／来源／用途 |
|---|---|
| name / executable / executableSha256 | 显示名、主程序文件名、主程序 SHA256 |
| engineRva | uint64，主模块中保存引擎对象指针的位置 |
| holderOffset / deviceOffset | uint32，从引擎对象到 holder，再到设备的指针字段偏移 |
| vtableRva | uint64，设备虚表相对主模块的位置，用来核对设备类型 |
| nameBlocksRva | uint64，当前名称读取器使用的 FName 块指针数组位置 |
| registerRva / unregisterRva | uint64，监听注册和注销方法相对主模块的位置；必须核对函数签名 |
| captureFile / captureSha256 | BuildPack 生成带摘要的模块名和校验值 |
| buses | 1～64 项，顺序固定 |

| buses 每项 | 含义 |
|---|---|
| id | 作者确定的稳定 ID，不能使用运行时对象地址 |
| nativeName | 引擎对象名，连接时匹配；当前 UTF-16 名称最多 95 个代码单元 |
| name / category | 展示名称与大类 |
| parentId / containsChildren | 初始父关系与聚合标志，连接时核对实际路由 |

**并非所有布局都已经参数化。** 原生代码仍使用特定对象偏移、Submix 表结构、FName 编码和监听虚表；例如设备标志、Submix 映射、默认根项都在 C 中核对。对象结构不同需要更新模块，不能只调整以上字段。已知名字只代表候选对象存在，不保证当前场景已经载入该组或触发监听。

## 发布前逐项核对

保存一份适配笔记，列出每个关键字段的取得方法、目标文件摘要、工具／版本、真实采集与未验证项目。构建器生成的 `manifest.files` 只证明包文件完整性，不证明这些字段正确，也不证明作者身份或声音独立性。
