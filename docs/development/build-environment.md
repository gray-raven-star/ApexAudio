# 开发环境与首次构建

## 项目根目录

解压源码包后，进入同时包含 `ApexAudio.sln`、`src/`、`adapters/`、`docs/` 的源码根目录。本文所有命令从这里运行。`E:/APEX6pro/ApexAudio` 之类的应用安装目录不是源码根目录。

## 前置条件

| 项目 | 要求／检查 |
|---|---|
| 操作系统 | Windows x64；原生模块和本项目目标都是 x64 |
| .NET | .NET 8 SDK，运行 `dotnet --list-sdks` 确认有 8.x；仅安装 Runtime 不能编译 |
| PowerShell | PowerShell 7，运行 `pwsh --version`；脚本使用其 JSON 和路径 API |
| 原生 C 编译器 | 本次开发分发包附带 Zig 0.15.2（Windows x64），位于 `toolchain/zig/zig.exe`；不能只复制 exe 而丢掉同目录的 `lib/` |
| UI 运行环境 | Microsoft Edge WebView2 Runtime；`vendor/webview2/` 的 SDK DLL 不等于浏览器 Runtime |
| 手柄反馈 | 运行实际反馈时需另装受支持的 X-Haptics；普通构建和 Mock 检查不需要连接手柄 |

本分发包不包含 .NET SDK、PowerShell、WebView2 Runtime 或官方驱动安装程序。它们是明确的外部前置，不应误认为项目丢失文件。使用官方发行渠道安装相应前置。

```powershell
dotnet --list-sdks
pwsh --version
./toolchain/zig/zig.exe version
```

如果只有源码而没有 `toolchain/zig/`，可自行准备 Zig，并把下面的 `-Compiler` 换成其实际绝对路径；不要求安装 MSVC，也不需要把 Zig 加入 PATH。当前脚本调用的是 `zig cc`，不是通用的 `cl.exe` 命令行。

## 环境预检与诊断工具

```powershell
pwsh -File tools/Setup.ps1 -Output evidence/setup.json
```

Setup 只检查，不自动下载软件或修改系统权限。失败先读报告的具体项目。需要显式指定编译器时传 -Compiler <zig.exe>。

完整开发包附 prebuilt/devtools/ApexAudio.DevTools.exe。仅源码仓库可重建：

```powershell
dotnet publish tools/DevTools/ApexAudio.DevTools.csproj -c Release -o prebuilt/devtools --configfile NuGet.Config
```

默认缓存可由构建脚本初始化在项目 .cache/；直接运行诊断 exe 时可在当前终端设置 $env:APEX_AUDIO_TEMP 到自己可写的本地目录，以免其他进程设置的环境变量传不到当前终端。详见 [权限与临时目录排查](../authoring/troubleshooting.md)。

FMOD 自检源码可单独发布：

```powershell
dotnet publish tools/FmodProbe/ApexAudio.FmodProbe.csproj -c Release -o prebuilt/fmod-probe --configfile NuGet.Config
```

其输入、版本边界及验证范围见 [FMOD 探查指南](../authoring/probe-fmod-runtime.md)。

## 构建主程序与离线检查

```powershell
pwsh -File tools/Build.ps1 -Test
```

输出 `dist/ApexAudio.exe`。构建从 [Mock 示例定义](../../examples/demo-pack) 生成演示包，测试会在 `artifacts/` 下写临时数据。它不会连接实际游戏，也不会启动驱动发送实体震动。FMOD 和 Unreal 是包内程序，不要求添加进主程序解决方案；按各自 BuildPack 脚本构建。

项目的 NuGet 配置不依赖外部第三方 NuGet 包，WebView2 SDK 文件已保存在 `vendor/`；但机器必须具有相应 .NET SDK 和所需框架引用包。如果还原报告框架缺失，应先补齐环境，不要跳过错误。

## 重建 X-Haptics 桥接

分发源码已带当前桥接二进制与对应校验配置，普通开发不需重新编译它。`tools/BuildNative.ps1 -Compiler ./toolchain/zig/zig.exe` 是维护桥接／特定 Wwise 原生构建的入口；重建后会改变桥接摘要，应一起发布新配置。给新游戏制作包通常不需要动这个提供器。

本地组装应用 ZIP 和开发套件见[发行版构建](release-builds.md)。新游戏包的构建命令见[制作适配包](../authoring/build-a-gamepack.md)。
