# Apex Audio：适配开发入口

这个仓库提供 Apex Audio 源码、通用适配器、空白模板和开发文档，不包含任何具体游戏的定义、定位数据或初始配置。目标游戏需要由适配者独立调查和验证。

仓库目前尚未发布完整开发套件压缩包。克隆源码后，按[构建环境说明](docs/build-environment.md)安装前置工具，再按[游戏适配包制作指南](docs/gamepack-authoring.md)开始。源码不附 Zig 编译器；制作原生采集模块时需要自行准备 Zig，并将路径传给构建脚本。

建议按此顺序阅读：

1. [架构和职责边界](docs/architecture.md)
2. [新游戏适配流程](docs/gamepack-authoring.md)
3. [适配包格式](docs/gpack-format.md)
4. [适配器通信协议](docs/adapter-protocol.md)

适配器构建成功不代表游戏内采集成功。请分别记录接口发现、构建、真实音轨采集和补充震动的验证结果。
