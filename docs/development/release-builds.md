# 构建应用和开发套件发行版

按[构建环境](build-environment.md)准备 PowerShell、.NET SDK 和完整的 Zig 目录。在含 `ApexAudio.sln` 的源码根目录运行：

```powershell
pwsh -File tools/Release.ps1 -Version 0.1.2 -CompilerDirectory 'C:/Tools/zig' -OutputDirectory '../release-output'
```

将版本、编译器目录和输出位置换成本次构建的值。脚本生成 Windows x64 运行包、DevKit ZIP、SHA-256 清单和构建报告。DevKit 提供通用源码、工具、适配包起始目录（`gamepack-starter/`）、示例和许可证；运行包附应用和用户文档。构建通过只说明源码和后端入口通过了编译，游戏采集和手柄反馈由各适配包的验证报告单独记录。

## GitHub 发布流程

使用 `vMAJOR.MINOR.PATCH` 标签发布稳定版。标签的版本部分须与该提交的 `src/ApexAudio.Desktop/ApexAudio.Desktop.csproj` 中的 `Version` 一致。工作流为标签发布 Windows x64 应用 ZIP、DevKit ZIP 和 SHA-256 校验清单。

拉取请求的打包检查仅验证这套构建流程。正式 Release 由稳定版标签触发。
