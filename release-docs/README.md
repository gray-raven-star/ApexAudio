# Apex Audio

Windows x64 声音采集工具。主程序负责适配包管理、预设编辑、分轨录音、试听和 X-Haptics 时间对照；游戏采集由独立 `.gpack` 提供。

## 使用运行包

解压后运行 `ApexAudio.exe`。需要 .NET 8 Desktop Runtime 和 WebView2 Runtime；本包不捆绑或安装它们。适配包由用户导入，运行包不预装任何游戏包或演示包。

游戏适配器需要核对其声明的游戏版本；不保证任意游戏开箱即用。补充震动需要另行安装兼容的官方 X-Haptics；不随本项目分发驱动。

## 开发适配包

使用完整开发套件，阅读 [START-HERE.md](START-HERE.md)。`docs/` 提供流程与协议，`adapters/` 提供后端实现，`templates/` 提供空白模板，`tools/` 提供构建和诊断工具。

开发套件不包含现成目标游戏配置或历史调查记录。Mock 仅供开发者验证传输，生成测试声音，不会自动出现在用户应用中。

## 构建

```powershell
pwsh -File tools/Setup.ps1
pwsh -File tools/Build.ps1
```

源码构建需要 Windows x64、PowerShell 7、.NET 8 SDK。随附 Zig 用于修改原生采集模块。编译与压缩完整性检查不代替游戏适配测试，详情见 [验证说明](docs/validation.md)。

第三方来源与许可见 THIRD-PARTY.md。
