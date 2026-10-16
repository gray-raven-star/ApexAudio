# Apex Audio

面向飞智八爪鱼 6 Pro、配合飞智 X-Haptics 使用的 Windows x64 游戏声音采集与补充震动工具。主程序管理适配包、预设、分轨录音和试听；游戏声音采集由独立 `.gpack` 提供。

## 开始使用

解压后运行 `ApexAudio.exe`。需要另行安装 .NET 8 Desktop Runtime 和 WebView2 Runtime。

从同一 GitHub Release 单独下载并导入所需游戏的 `.gpack`，确认来源后连接已启动的游戏。适配包只支持其声明的游戏版本；支持范围和实测记录见包内 README。补充震动需要兼容的官方 X-Haptics，本项目不分发驱动。

- [连接、录音和设置](docs/user/settings-recording.md)
- [当前能力与限制](docs/user/capabilities.md)

## 开发资料

制作适配包需要完整源码或开发套件。构建环境、制作流程、协议及 AI 制作入口见[项目仓库](https://github.com/gray-raven-star/ApexAudio)，从其中的 START-HERE.md 开始。

## 许可

本项目采用 [MIT License](LICENSE)。第三方来源和许可见 [THIRD-PARTY.md](THIRD-PARTY.md)。
