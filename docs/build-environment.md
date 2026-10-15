# 开发环境与首次构建

## 项目根目录

解压源码包后，进入同时包含 `ApexAudio.sln`、`src/`、`adapters/`、`docs/` 的 `ApexAudio/` 目录。本文所有命令从这里运行。`E:/APEX6pro/ApexAudio` 之类的应用安装目录不是源码根目录。

## 前置条件

| 项目 | 要求／检查 |
|---|---|
| 操作系统 | Windows x64；原生模块和本项目目标都是 x64 |
| .NET | .NET 8 SDK，运行 `dotnet --list-sdks` 确认有 8.x；仅安装 Runtime 不能编译 |
| PowerShell | PowerShell 7，运行 `pwsh --version`；脚本使用其 JSON 和路径 API |
| 原生 C 编译器 | 本次开发分发包附带 Zig 0.15.2（Windows x64），位于 `toolchains/zig/zig.exe`；不能只复制 exe 而丢掉同目录的 `lib/` |
| UI 运行环境 | Microsoft Edge WebView2 Runtime；`vendor/webview2/` 的 SDK DLL 不等于浏览器 Runtime |
| 手柄反馈 | 运行实际反馈时需另装受支持的 X-Haptics；普通构建和 Mock 检查不需要连接手柄 |

本分发包不包含 .NET SDK、PowerShell、WebView2 Runtime 或官方驱动安装程序。它们是明确的外部前置，不应误认为项目丢失文件。使用官方发行渠道安装相应前置。

```powershell
dotnet --list-sdks
pwsh --version
./toolchains/zig/zig.exe version
```

如果只有源码而没有 `toolchains/zig/`，可自行准备 Zig，并把下面的 `-Compiler` 换成其实际绝对路径；不要求安装 MSVC，也不需要把 Zig 加入 PATH。当前脚本调用的是 `zig cc`，不是通用的 `cl.exe` 命令行。

## 构建主程序与离线检查

```powershell
pwsh -File tools/Build.ps1 -Test
```

输出 `dist/ApexAudio.exe`，测试会构建 Mock 包并在 `artifacts/` 下写临时数据。它不会连接实际游戏，也不会启动驱动发送实体震动。FMOD 和 Unreal 是包内程序，不要求添加进主程序解决方案；按各自 BuildPack 脚本构建。

项目的 NuGet 配置不依赖外部第三方 NuGet 包，WebView2 SDK 文件已保存在 `vendor/`；但机器必须具有相应 .NET SDK 和所需框架引用包。如果还原报告框架缺失，应先补齐环境，不要跳过错误。

## 构建三个参考包

这些命令只编译包，不自动连接游戏：

```powershell
dotnet restore adapters/Wwise/ApexAudio.Wwise.csproj --configfile NuGet.Config
pwsh -File adapters/Wwise/tools/BuildPack.ps1 -DefinitionDirectory gamepacks/monsterhunterworld -Compiler ./toolchains/zig/zig.exe
pwsh -File adapters/Fmod/tools/BuildPack.ps1 -Profile gamepacks/kingdomcome2 -Compiler ./toolchains/zig/zig.exe
pwsh -File adapters/Unreal/tools/BuildPack.ps1 -DefinitionDirectory gamepacks/stellarblade -Compiler ./toolchains/zig/zig.exe
```

结果位于 `dist/packages/`。定义目录含源配置，`captureSha256` 可暂为空；BuildPack 在构建暂存区填入实际 DLL 摘要。不要直接把未编译的定义目录压缩后声称它是完整可运行包。

## 重建 X-Haptics 桥接

分发源码已带当前桥接二进制与对应校验配置，普通开发不需重新编译它。`tools/BuildNative.ps1 -Compiler ./toolchains/zig/zig.exe` 是维护桥接／特定 Wwise 原生构建的入口；重建后会改变桥接摘要，应一起发布新配置。给新游戏制作包通常不需要动这个提供器。

## 交付开发项目

项目提供 `tools/ExportSource.ps1`，使用明确目录清单导出源码、三个参考定义和文档。可选 `-CompilerDirectory` 携带完整 Zig 工具链。它不收集运行数据、历史实验、旧游戏配置或开发机的固定安装路径；导出的根目录不含 `artifacts/`、`dist/`、`bin/`、`obj/` 或用户录音。

```powershell
pwsh -File tools/ExportSource.ps1 -Output ../ApexAudio-Developer.zip -CompilerDirectory ./toolchains/zig
```

导出目录 `artifacts/source-export-*` 只是构建暂存区，不应再次作为输入。压缩包中工具链及第三方文件保留其许可；本项目并未因此取得游戏资源、商业 SDK 或品牌图标的公开再分发许可，来源见根目录 `THIRD-PARTY.md`。
