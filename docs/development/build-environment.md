# 开发环境与首次构建

## 项目根目录

解压源码包后，进入同时包含 `ApexAudio.sln`、`src/`、`adapters/`、`docs/` 的 `ApexAudio/` 目录。本文所有命令从这里运行。`E:/APEX6pro/ApexAudio` 之类的应用安装目录不是源码根目录。

## 前置条件

| 项目 | 要求／检查 |
|---|---|
| 操作系统 | Windows x64；原生模块和本项目目标都是 x64 |
| .NET | .NET 8 SDK，运行 `dotnet --list-sdks` 确认有 8.x；仅安装 Runtime 不能编译 |
| PowerShell | PowerShell 7，运行 `pwsh --version`；脚本使用其 JSON 和路径 API |
| 原生 C 编译器 | Git 源码不附 Zig。`Release.ps1` 将发布者提供的编译器放入开发套件的 `toolchain/zig/`，连同同目录的 `lib/` 一起分发 |
| UI 运行环境 | Microsoft Edge WebView2 Runtime；`vendor/webview2/` 的 SDK DLL 不等于浏览器 Runtime |
| 手柄反馈 | 运行实际反馈时需另装受支持的 X-Haptics；普通构建和 Mock 检查不需要连接手柄 |

SDK、PowerShell、WebView2 Runtime 和官方驱动需另行安装。编译器按 `-Compiler`、环境变量 `APEX_ZIG`、`toolchain/zig/zig.exe`、PATH 中的 `zig` 顺序查找。

```powershell
dotnet --list-sdks
pwsh --version
./toolchain/zig/zig.exe version
```

最后一条命令检查项目内的 Zig；没有该目录时换成实际安装路径。原生构建脚本调用 `zig cc`。

检查原生开发环境并保存报告：

```powershell
pwsh -File tools/Setup.ps1 -Compiler C:/Tools/zig/zig.exe -Output evidence/setup.json
```

`C:/Tools/zig/zig.exe` 需替换成实际路径；已通过 `APEX_ZIG`、项目目录或 PATH 配置时可省略 `-Compiler`。`Setup.ps1` 同时检查 .NET 8 SDK 和 Zig；`Build.ps1` 不调用 Zig。

## 准备诊断工具

开发套件的诊断工具位于 `prebuilt/devtools/`。使用 Git 源码时单独构建：

```powershell
dotnet publish tools/DevTools/ApexAudio.DevTools.csproj -c Release --no-self-contained --configfile NuGet.Config -o prebuilt/devtools
prebuilt/devtools/ApexAudio.DevTools.exe help
```

`Build.ps1` 不发布 DevTools。诊断工具的调查和采集命令见[制作指南](../authoring/gamepack-authoring.md)。

## 构建主程序与离线检查

```powershell
pwsh -File tools/Build.ps1 -Test
```

输出 `dist/ApexAudio.exe`，测试会构建 Mock 包并在 `artifacts/` 下写临时数据。它不会连接实际游戏，也不会启动驱动发送实体震动。FMOD 和 Unreal 是包内程序，不要求添加进主程序解决方案；按各自 BuildPack 脚本构建。

项目不依赖外部第三方 NuGet 包，WebView2 SDK 已保存在 `vendor/`。还原若报告框架缺失，需补齐相应 .NET SDK 或框架引用包。

## 构建新游戏包

先按[适配包制作指南](../authoring/gamepack-authoring.md)创建并填写 `gamepacks/<你的包ID>/`。日常使用统一构建入口，它根据 `game.json` 的 `backend` 检查字段并调用对应后端：

```powershell
pwsh -File tools/Build-GamePack.ps1 -DefinitionDirectory gamepacks/my-game -Compiler C:/Tools/zig/zig.exe
```

输出位于 `dist/packages/`。源定义中的 `captureSha256` 可暂为空，BuildPack 编译后填入 DLL 摘要，再打包运行文件；定义目录本身不能直接作为运行包。

## 重建 X-Haptics 桥接

源码包含桥接二进制和校验配置，普通开发直接使用。修改桥接或该脚本对应的 Wwise 原生模块时，运行 `tools/BuildNative.ps1 -Compiler C:/Tools/zig/zig.exe`，路径换成实际编译器。重建桥接后一起发布更新的摘要配置。制作新游戏包通常无需修改此提供器。

## 交付开发项目

公开源码仓库包含通用代码、空白模板和文档，不含目标游戏定义、运行数据或用户录音。自行整理分发包时，应排除 `artifacts/`、`dist/`、`bin/`、`obj/`、本机 `data/` 和目标游戏专属定义，并保留第三方许可证。

`artifacts/source-export-*` 是构建暂存区，不要再次用作导出输入。分发时保留工具链和第三方文件的许可，另行核对游戏资源、商业 SDK 和品牌图标的再分发许可。来源见根目录 `THIRD-PARTY.md`。
