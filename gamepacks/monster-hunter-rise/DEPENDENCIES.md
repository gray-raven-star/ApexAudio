# 来源与依赖

- 用户提供的 ApexAudio DevKit 0.1.0：复用 Wwise 后端、Contracts、Windows 接口及构建/打包工具。本次未修改其源码，也未读取其他目标游戏的适配结果。原项目没有指定项目级开源许可证，未另行宣称许可。
- .NET SDK 8.0.413、Zig 0.15.2：本机既有 SDK 和指定 DevKit 随附编译器。适配器运行依赖 .NET 8，不捆绑编译器。
- 本机当前游戏的基础 Pak 和 patch_001 Pak：只按索引读取初始化银行和四个小型音频设置元数据；优先使用补丁覆盖后的 Init.bnk。没有整包解压或分发游戏资源。
- [Ekey/REE.PAK.Tool](https://github.com/Ekey/REE.PAK.Tool)：公开的 PakUnpack.cs、PakHeader.cs、PakEntry.cs、PakList.cs、PakHash.cs、PakCipher.cs、Murmur3.cs 作格式参考；MHR_STM_Release.list 仅作为公开资源路径清单，不包含适配配置。本次使用自写有界读取器，没有运行其整包解压或重打包程序。索引按公开格式实现处理，未修改游戏保护、可执行程序或反作弊。
- [python-zstandard 0.25.0](https://pypi.org/project/zstandard/0.25.0/)：仅安装到本任务目录，用于 Zstd 压缩的元数据读取。不捆绑进运行包。
- [bnnm/wwiser](https://github.com/bnnm/wwiser)：本次对话从公开源码下载的 v20260808，读取 Init.bnk v140 的 HIRC 路由与效果槽。未加载名称数据库或既有目标配置。未生成模拟音频。
- SDK 选择依据：DevKit 已实现 bank 140 / Wwise 2021.1 的兼容路径、当前 EXE 中七项完整导出签名、实际效果注册/银行加载/回调/PCM 验证。尚未识别精确的 Wwise 补丁版本，不把 bank 版本单独当作完整 SDK 版本证明。

公开文件的 SHA256 见 validation-report.json；下载日期 2026-10-15。原始游戏银行、Pak、EXE、媒体、原始内存、完整第三方解包器及 SDK 二进制均不分发。
