# Apex Audio

面向飞智八爪鱼 6 Pro 开发的 Windows 游戏声音采集与补充震动工具。主程序负责连接、音轨分组、预设编辑、分轨录音与试听，并可将所选游戏音轨送入飞智 X-Haptics 生成补充震动。每款游戏的采集方式由独立的 `.gpack` 适配包提供。

[English](README.en.md)

## 下载与使用

目前仓库提供源码和适配开发文档，尚未发布可直接运行的正式版压缩包。运行程序需要 Windows x64、.NET 8 Desktop Runtime 和 WebView2 Runtime；补充震动功能目前以飞智八爪鱼 6 Pro 为主要适配和验证设备。

适配包由应用内的“导入 .gpack”功能安装。游戏包只匹配包中声明的程序版本；游戏更新后可能需要更新对应包。补充震动需要另行安装兼容的官方 X-Haptics。

游戏适配包适合通过 GitHub Release 作为独立附件发布，方便只下载需要的游戏包；仓库源码不会混入录音、用户配置或本机游戏数据。当前整理好的候选包有独立索引和校验摘要，实际验证范围见包内说明。

## 开发适配包

从 [开发入口](START-HERE.md) 开始，再阅读[游戏适配包制作指南](docs/gamepack-authoring.md)。

| 目录或文档 | 用途 |
|---|---|
| `src/` | 主程序与通用功能 |
| `adapters/` | Wwise、FMOD、Unreal 等独立采集器实现 |
| `gamepacks/` | 游戏版本定义和作者初始预设 |
| `templates/` | 新适配包模板 |
| `docs/` | 架构、接口、预设和适配说明 |
| `schemas/` | 适配包与预设格式校验规则 |
| `tools/` | 构建、检查和打包脚本 |

制作新游戏适配包需要检查目标游戏实际采用的音频接口和版本，并验证录到的是独立音轨。引擎名称相同不代表可以直接套用其他游戏的接口或配置。没有进行游戏内验证时，请在说明中明确标注。

## 从源码构建

需要 Windows x64、PowerShell 7 和 .NET 8 SDK。详见[开发文档](docs/development.md)及[构建环境说明](docs/build-environment.md)。

```powershell
pwsh -File tools/Build.ps1 -Test
```

本项目采用 [MIT License](LICENSE)。第三方组件及各自许可见 [THIRD-PARTY.md](THIRD-PARTY.md)。
