# Apex Audio

面向飞智八爪鱼 6 Pro、配合飞智 X-Haptics 使用的 Windows x64 游戏声音采集与补充震动工具。主程序负责适配包管理、预设编辑、分轨录音和试听；游戏采集由独立 `.gpack` 提供。

## 使用运行包

解压后运行 `ApexAudio.exe`。需要 .NET 8 Desktop Runtime 和 WebView2 Runtime；本包不捆绑或安装它们。请从同一 GitHub Release 单独下载并导入你要玩的游戏适配包，无需下载其他游戏包。

游戏适配器需要核对其声明的游戏版本；不保证任意游戏开箱即用。补充震动需要另行安装兼容的官方 X-Haptics；不随本项目分发驱动。

## 开发适配包

使用完整源码或开发套件，从 [START-HERE.md](START-HERE.md) 开始。使用说明、制作流程和协议集中在[文档入口](docs/README.md)。`adapters/` 提供后端实现，`templates/` 提供模板，`tools/` 提供构建和诊断工具。

开发套件不包含现成目标游戏配置或历史调查记录。Mock 仅供开发者验证传输，生成测试声音，不会自动出现在用户应用中。

## 构建

在完整源码或开发套件目录按[构建环境说明](docs/build-environment.md)准备工具，再执行其中的构建命令。游戏内采集及反馈按[验证说明](docs/validation.md)记录。

第三方来源与许可见 THIRD-PARTY.md。
