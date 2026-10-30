# Wwise 采集适配器

独立适配器进程实现宿主 V1 协议。原生效果器复制总线 PCM，在渲染回调中生成所选总线的反馈混音；独立音轨传输不控制反馈节奏。支持字段见 [配置参考](../../docs/reference/engine-config-reference.md)。

## 复用前检查

在目标 EXE 或实际音频模块中核对 `IsInitialized`、`GetAudioSettings`、`SetBusEffect`、`LoadBankMemoryCopy`（部分旧版为 `LoadBank`）、`RegisterPlugin`、`RegisterGlobalCallback` 和 `UnregisterGlobalCallback`。

当前模块用 `GetProcAddress` 找导出。可识别的完整 C++ 修饰名及旧版别名位于 `native/ApexWwiseCapture.c` 的 `findWwiseExport` 和 `CaptureStart`。只找到相近函数名、静态代码或银行文件不能满足现有实现；没有导出时需扩展定位／调用实现，不能只改配置。

`CAPTURE_BANK_VERSION`、`CAPTURE_SDK_YEAR`、`CAPTURE_SDK_SERIES` 是编译参数。代码已有 2016 及较新接口的分支，但不是完整 SDK 兼容矩阵。编译器允许填入年份不代表该 ABI 已实现或验证。效果器 vtable、参数和返回码须与目标版本一致。

普通采集路由最多 1024 条，48 kHz，每渲染块最多 2048 帧；辅助混音条目不作为普通采集路由。每条路由的 `slot` 是挂载效果器的实际槽位，不能把所有未知项随便填成 0。`SetBusEffect` 会改变运行中的效果器槽位；需要核对槽位用途，不能笼统声称对游戏 DSP 链完全没有影响。

## 配置和构建

每款游戏需提供单独的定义目录；新游戏需要重新获取模块摘要、银行头、Bus ID、父子关系和可用槽位。完成后的游戏包可以通过 GitHub Releases 分发，其定义源码可按维护需要一并公开。

在项目根目录运行：

```powershell
pwsh -File tools/Build-GamePack.ps1 -DefinitionDirectory gamepacks/my-game
```

输出 `dist/packages/<包ID>.gpack`。源定义的 `captureFile` 必须使用包内 `modules/` 下的 DLL 路径；构建工具写入对应 `captureSha256`。环境和编译器选择见[环境说明](../../docs/development/build-environment.md)，分组证据及完整流程见[制作指南](../../docs/authoring/gamepack-authoring.md)。统一入口会先还原 Wwise 依赖。

## 故障定位

| 结果 | 当前实现中的含义／下一步 |
|---|---|
| 启动码 102 | 模块、所需导出缺失，或者引擎未初始化；这个码本身不能区分原因 |
| 105 / 106 / 107 | 分别检查插件注册、采集银行装载、全局回调注册；结合 SDK ABI 核对 |
| 108 | 已有另一份采集占用，不要再注册同一套采集 |
| 109 | 音频设置不是当前支持的采样率或块长 |
| 111 | 已加载实例的配置改变，不能在同一游戏实例随意更换配置 |
| 有分组但无独立 PCM | 检查槽位挂载、实际播放路径、回调计数与缓冲区；不是包格式成功就表示音频成功 |

`CaptureSelfTest` 只测试人工数据路径；`CaptureAdapterInfo` 报告编译参数。两者都不能认证某游戏已可用。已加载旧原生模块时，按正常流程停止并重启游戏后再试新包，不强制结束用户游戏。
