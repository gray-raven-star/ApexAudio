# 制作适配包和迁移

新游戏作者先读 [适配包制作指南](gamepack-authoring.md)。本文补充底层开发、旧配置迁移与验证说明；迁移旧配置不是新游戏适配的必经步骤。

## 从 Mock 开始

1. 按 `docs/adapter-protocol.md` 实现 Adapter 进程。
2. Connect 确认目标游戏，GetAudioGroups 返回稳定层级，StartCapture 输出真实独立 PCM。
3. 不支持的能力报告 false。记录只有混音时只能输出一个整体混音节点。
4. 准备 manifest、作者预设、运行文件及必要许可。
5. 执行 `tools/Pack.ps1` 自动生成文件 SHA256 清单并压成 .gpack。
6. 在 UI 导入；连接后确认音轨内容、停止／退出行为。Mock 自带四条不同测试声音。

```powershell
pwsh -File tools/Pack.ps1 -Directory ./my-pack -Output ./my-game.gpack
```

C# 可复用 Contracts 的 AdapterServer，不需要修改 Hub。其他语言按字节协议实现即可。允许自带 Python／Node／其他私有运行环境，不能在正常使用时要求用户另装前置或手动运行程序。发布文件是否允许再分发由包作者确认。

## 复用现有 Wwise 配置

`adapters/Wwise` 是可选的独立适配器，不属于本体。它使用 game.json、对应原生采集 DLL 和作者预设，支持既有配置的迁移。宿主没有 Wwise 版本表或游戏判断。

```powershell
pwsh -File tools/MakeWwisePack.ps1 `
  -Profile '旧工具/profiles/游戏.json' `
  -LegacyRoot '旧工具目录' `
  -Id 'local.game-name'
```

先运行 Build.ps1。迁移只接受 canCapture=true 的配置，复制目录中记录的采集 DLL，移除本机安装路径、扫描缓存路径和旧录音路径。清单保留程序 SHA256，适配器核对游戏及引擎模块 SHA256。

迁移后的包依然是**该已知版本的适配包**，不代表自动找到了新游戏的接口。包中“迁移的初始配置”来源明确，不冒充引擎原始名称；用户可以重新命名、保存、分享。

Wwise 原生模块在游戏内复用已有有界多组录音环形缓冲，由独立 Adapter 读出并转换成统一 PCM 帧。全组捕获与反馈选择分开；录音起始时钟为渲染帧起点的近似映射。旧工具占用同一游戏采集时，先断开旧工具。

## 构建原生代码

随项目保存 `adapters/Wwise/native/ApexWwiseCapture.c` 和 `providers/XHaptics/native/ApexGroupsBridge.c`。源码附带 X-Haptics 桥接二进制；Wwise 采集 DLL 由适配包构建流程根据 SDK／银行参数编译。

可用 Zig / Windows C 编译器重建。Wwise 编译需提供 CAPTURE_BANK_VERSION、CAPTURE_SDK_YEAR、CAPTURE_SDK_SERIES；重建后更新 game.json 的 captureSha256，再重新打包。X-Haptics 桥接重建后更新 native/xh-support.json。接口未知时不能只填写新版本编号就声称支持。

## 验证范围

`Build.ps1 -Test` 检查完整包导入、协议、四条不同独立音轨、WAV 分轨／截取、多个用户预设、还原作者原稿、适配器崩溃隔离、越界路径、版本拒绝、文件篡改及卸载保留数据。

协议检查和模拟声音只能验证传输、录音和包格式，不能证明真实游戏采集成功。各适配器的支持范围和验证记录见其 README；发布者应另行记录目标游戏版本、真实独立音轨、开关、断开和反馈测试结果。
