# Apex Audio 项目约定

- 游戏版本识别、地址、ABI、路由和采集实现归适配包。宿主通过公共协议连接。修改边界前查阅[架构](docs/development/architecture.md)和[适配器协议](docs/reference/adapter-protocol.md)。
- 主程序构建和离线检查使用 `pwsh -File tools/Build.ps1 -Test`。环境准备见[构建指南](docs/development/build-environment.md)。
- 制作适配包按[识别接口](docs/authoring/identify-audio-system.md)、[构建](docs/authoring/build-a-gamepack.md)和[验证](docs/authoring/verify-a-gamepack.md)推进。优先核对现有采集器和目标文件证据，按实际地址与版本记录依据。
- 只读探查与可能加载采集模块的游戏连接分开。按任务授权操作。先检查已有实例；需要用户处理登录、启动方式或存档时说明具体原因。正常连接流程遵守游戏的权限和保护限制。
- 分别记录包完整性、协议、非零 PCM、真实分轨、反馈和物理手柄结果。未执行的阶段标记未测。接手已有适配先核对版本摘要和验证报告，再继续最早尚未完成的阶段。
- 回调只做有界复制、转换和发布；管道发送与写盘放在回调外。录音和反馈分别保活。详见[自定义采集器指南](docs/authoring/write-a-custom-adapter.md)。
- Python 工具示例使用 `uv run`。文档变动同步更新中英文首页、文档索引、相关章节链接和打包说明。
