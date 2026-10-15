# FMOD 分组适配器

独立 EXE 适配器，实现既有 IPC V1、独立 PCM 和可选 realtimeMix。宿主源码和已安装二进制无需改变。每款游戏的定位数据和总线定义由单独的 `gamepacks/<你的包ID>` 提供，最终打包为自包含 `.gpack`。

## 当前实现

- 通过包内配置定位现有 Studio System，核对游戏、实例所属模块、FMOD Core 和 Studio 的 SHA-256。
- 每条 Studio Bus 挂接 pass-through DSP，原始游戏缓冲原样传递；音轨单独写入有界环形缓冲。
- 所选总线在 Core master DSP 回调中汇总，输出既有 `apex-f32-planar-ring-v1`，X-Haptics 直接消费。
- 开关只改变混音选择，录音仍保留全部独立总线。聚合父组和子组可能包含相同声音，宿主既有 frontier 规则避免重复反馈。
- 静音总线使用 LockChannelGroup 保持采集位置；断开时移除 DSP、释放 DSP 和 channel-group lock。
- 采集保活独立于 IPC 写音轨，反馈不等待宿主录音写盘。

## game.json

`executable` / `executableSha256` 是版本匹配规则；`modules` 是实际调用模块及其哈希。

`wrapperModule` / `wrapperRva` 定义模块内全局对象指针的位置；`stateOffset` / `studioOffset` 是该对象中初始化状态（值 2）及 Studio System 句柄的成员偏移。所有位置都属于包内数据，不在宿主中判断游戏名。此定位器目前适用于已核对的对象包装方式；其他游戏若使用不同实例访问方式，需要在适配器包内扩展定位实现，不能照搬其他游戏的偏移。

`runtimeVersion` 是 FMOD 实际版本；版本检测比较原始整数，并应在目标游戏运行时确认。

`buses` 最多 64 条：每条包含 16 字节 GUID 小端原始字节的 hex `id`、原始 `path`、显示 `name`、实际 `parentId`、`category`、`containsChildren`。顺序固定且与原生索引一致。GUID、路径来自银行 API；翻译和初始开关由包作者整理。

`captureFile` / `captureSha256` 由构建器写入，原生 DLL 文件名带内容哈希，避免更新时与游戏仍加载的旧文件同名。新版本只能在上一份采集已停止后连接，不能同时采集同一游戏。模块仅支持已核对的 FMOD 2.02 C API / DSP SDK 110、48 kHz，最多 16 声道下混为立体声，块长不超过 2048。不同版本、静态链接、隐藏 API、额外输出设备和非标准声道布局需要另行适配；不要宣称所有 FMOD 游戏已兼容。

## 构建

```powershell
pwsh -File adapters/Fmod/tools/BuildPack.ps1 -Compiler <zig.exe> -Profile gamepacks/<你的包ID>
```

仅编译本适配器和原生模块，复用 Contracts / Windows 辅助程序集，产物位于 `dist/packages/`。包不携带游戏银行、FMOD DLL 或第三方参考源码。匹配失败应更新包的定位数据和哈希，再实测，不要放宽版本检查。

真实游戏验证应记录在该游戏包自己的验证报告中，注明运行时采集、音轨独立性、反馈与重连各自的覆盖范围。不能把适配器构建或离线协议测试写成实际游戏验证。
