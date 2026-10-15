# Adapter IPC 协议（V1）

## 进程启动

宿主在包目录执行清单 entryPoint，不使用 shell，传入：

```
adapter.exe --control <唯一管道名> --audio <唯一管道名>
```

两个管道服务器都由宿主创建，仅当前 Windows 用户可连接。Adapter 连接 `\\.\pipe\<管道名>`。工作目录为包根目录。10 秒内未建立两个连接，启动失败。宿主退出时请求 Shutdown，超时则结束自己启动的 Adapter 进程树。

## 控制帧

字节模式 Named Pipe，双向。每帧为 4 字节小端无符号长度，后接 UTF-8 JSON；长度最多 4 MiB。读方必须处理任意分片，不能假设一次 Read 收到一帧。

请求：

```json
{"version":1,"id":1,"method":"Connect","params":{"processId":1234,"gamePath":"可选主程序路径"}}
```

成功响应：

```json
{"version":1,"id":1,"ok":true,"result":{"connected":true,"capturing":false,"detail":"已连接","droppedBlocks":0}}
```

失败响应：

```json
{"version":1,"id":1,"ok":false,"result":null,"error":"程序版本不匹配","code":"version_mismatch"}
```

每个请求有一个同 id 响应。V1 宿主串行发请求；不在控制管道发送未约定通知。普通请求超时 8 秒，Connect 15 秒，后台状态查询 2 秒，Shutdown 1.5 秒。超时后连接失效，不能继续读取迟到响应。

| 方法 | params | result |
|---|---|---|
| GetInfo | null | id、name、protocolVersion、capabilities |
| GetCapabilities | null | 能力对象 |
| Connect | processId、gamePath | 状态 |
| GetAudioGroups | null | 完整有序分组数组 |
| StartCapture | groupIds 字符串数组 | 状态 |
| StopCapture | null | 状态 |
| GetStatus | null | 状态 |
| GetFeedbackSource | null | 实时混音源描述，仅 realtimeMix=true |
| SetFeedbackGroups | groupIds 字符串数组 | 状态，仅 realtimeMix=true |
| Disconnect / Shutdown | null | 状态 |

能力：`groups`、`independentPcm`、`activity`、`sourceInfo`、`wholeMix`，都是布尔值。V1 分组录制要求 independentPcm；活动由 PCM 峰值计算。独立的事件／来源数据和 activity-only 传输不在 V1 协议内，相关能力当前应为 false，未来升版定义。

分组结构：

```json
{"id":"stable-id","name":"原始名称","parentId":null,"category":"音效","hasPcm":true,"containsChildren":false,"nameSource":"作者标注"}
```

id 稳定且唯一，层级不得循环，parentId 必须存在或为 null。最多 2000 条。hasPcm=false 的显示节点不能输出音频。containsChildren=true 表示输出包含实际子总线，宿主用于避免重复混合。

GetAudioGroups 后的数组顺序在整个连接期间固定；音频 groupIndex 引用此数组。层级改变需要断开重连。无法获得真实独立音频时，只能输出一个明确标识的整体混音节点，不能复制给多个分组。

## 二进制音频帧

音频管道为 Adapter → Host 单向流。40 字节小端头，紧跟 `frames × channels × 2` 字节 PCM：

| 偏移 | 类型 | 内容 |
|---|---|---|
| 0 | uint32 | magic = 0x31504141 |
| 4 | uint32 | groupIndex |
| 8 | uint32 | sampleRate = 48000 |
| 12 | uint32 | channels = 2 |
| 16 | int32 | frames，1～2048 |
| 20 | uint32 | format = 1：PCM16 小端、有符号、左右交错 |
| 24 | int64 | 第一帧的 Windows QueryPerformanceCounter 时间戳 |
| 32 | int64 | 全连接递增 sequence，从 0 起，所有音轨共享序列 |

不同音轨同一时间片可以有相同 tick。使用 QueryPerformanceFrequency 获取频率；Windows 上不同进程共用同一 QPC 基准。禁止用 Unix 时间或进程相对计时冒充 QPC。

声音静音期间可以不发帧；宿主补零，但 Adapter 状态必须继续响应。时间戳必须非递减地描述每条音轨的位置，不能通过给同一混音更换 groupIndex 假冒独立音轨。

音频回调不应直接阻塞写管道。适配器应在回调外从有界缓冲区发送，丢帧时报告 droppedBlocks。Wwise 示例读取原生有界环形缓冲；阻塞只发生在后台转送任务。宿主保留序列缺口与峰值统计，V1 不对任意第三方语义真实性自动认证。

在 `Contracts/Transport.cs` 提供 C# 参考服务，Mock 提供完整可运行实现。其他语言无需引用这些 DLL，只遵循字节协议。

## 可选实时反馈混音

能力对象可增加 `realtimeMix: true`，省略时为 false，旧 V1 包仍可使用独立音轨由宿主混音。支持实时混音的包由采集端混合所选声音，反馈读取该混音，不经过独立音轨管道、宿主录音写盘和宿主混音定时器。

此能力与 Wwise、FMOD 或游戏名称无关。主程序只调用两个控制方法，并验证下面的共享缓冲区格式。包可以用任何引擎采集，但需要自行实现实时混音输出，声明能力不等于自动获得采集支持。

`SetFeedbackGroups` 接收 `{ "groupIds": ["稳定分组编号"] }`。宿主已排除被聚合父组包含的子组，包应仅混合这些组，空数组应输出静音。每次切换预设或开关后调用，返回成功代表新选择已应用；不需要停止录音、重连或重启 X-Haptics。此选择不得改变 StartCapture 的独立音轨范围。

`GetFeedbackSource` 返回例如：

```json
{"format":"apex-f32-planar-ring-v1","mappingName":"Local\\MyAdapterMix-1234","producerProcessId":1234}
```

名称由包提供，不要求固定前缀或包含游戏名。使用当前会话 `Local\\` 名称，最多 127 个 UTF-16 字符。producerProcessId 是生产者进程编号，必须与映射头一致。映射在连接期间保持有效，断开时停止生产；不同游戏实例必须使用不同名称。

`apex-f32-planar-ring-v1` 使用小端字节序，总映射大小 3,150,488 字节。只使用第一个流，其余区域保留；布局沿用已验证的原生传输 ABI：

| 绝对偏移 | 类型 | 内容 |
|---|---|---|
| 0 / 4 / 8 | uint32 | magic=0x37504341 / version=1 / producerProcessId |
| 16 | int32 | active，运行时 1，停止时 0 |
| 56 | uint64 | QPC frequency |
| 64 / 72 | uint64 | started / heartbeat，QPC ticks |
| 80 | int32 | 已发布块数，从 0 开始 |
| 96 / 100 | uint32 | sampleRate=48000 / channelConfig=0x3102 |
| 104 | bytes | 第一流的 64 块环形缓冲，每块 16,408 字节 |

每块：偏移 0 是 int32 sequence，4 是 uint32 frames（1～2048），8 是 uint32 channels=2，12 是 uint32 channelConfig=0x3102，16 是 uint64 QPC tick，24 是 float32 左声道数组（2048 项），8216 是 float32 右声道数组（2048 项）。只消费 frames 项；振幅约定 ±1，不经过 Windows 主音量。tick 记录声音采集时刻，用于识别积压，不能用发送时刻掩盖旧音频。

生产者按 `writes % 64` 写块：先将 sequence 改为奇数，写头和 PCM，内存屏障后改为偶数，再原子增加 writes。不得在游戏音频回调中写盘或等待宿主管道。X-Haptics 消费端从连接时的最新位置开始；超出缓冲区或过时块会被丢弃，不积压回放。保活及反馈消费独立于录音通道。

C# 参考接口为 `IRealtimeMixAdapter`，Wwise 提供采集端混音实例；Mock 的测试模式提供自定义映射名称实例，验证主程序不依赖引擎或游戏名称。格式暂仅支持 Windows、48 kHz、立体声；其他采样率由适配包转换。格式变化需新 format 值，不能复用 v1 名称改变布局。
