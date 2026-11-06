# 构建应用和开发套件发行版

按[构建环境](build-environment.md)准备 PowerShell、.NET SDK 和完整的 Zig 目录。在含 `ApexAudio.sln` 的源码根目录运行：

```powershell
pwsh -File tools/Release.ps1 -Version 2026.10.18 -CompilerDirectory 'C:/Tools/zig' -OutputDirectory '../release-output'
```

将版本、编译器目录和输出位置换成本次构建的值。脚本生成 Windows x64 运行包、DevKit ZIP、SHA-256 清单和构建报告。DevKit 提供通用源码、工具、适配包起始目录（`gamepack-starter/`）、示例和许可证；运行包附应用和用户文档。构建通过只说明源码和后端入口通过了编译，游戏采集和手柄反馈由各适配包的验证报告单独记录。

## 现有 GitHub 发布流程

[开发套件工作流](https://github.com/gray-raven-star/ApexAudio/blob/main/.github/workflows/publish-bundle.yml) 监听 `bundle-*` 标签，读取标签提交里的适配包、`release-inputs/gamepacks.json` 和 `release-inputs/release-notes.md`，并调用 `Release.ps1 -DevKitOnly`。当前 `main` 缺少这两个发布文件，`Release.ps1` 也已移除该参数。因此直接给当前 `main` 打 `bundle-*` 标签无法按原流程发布。需要在本地生成应用和 DevKit 两个 ZIP 时，使用上节命令。历史发布见 [ApexAudio 2026.10.18](https://github.com/gray-raven-star/ApexAudio/releases/tag/bundle-2026.10.18)。
