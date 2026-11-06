# 来源与依赖

- ApexAudio DevKit 0.1.0：用户提供的发布包。复用 Wwise 后端、Contracts、Windows 进程接口、Core 校验与协议；没有从发布包以外的 ApexAudio 工作目录取代码或游戏配置。原项目未指定项目级开源许可证，源码随本次用户授权的交付附上，未另行宣称许可。
- Zig 0.15.2：用户提供的 DevKit `toolchain/zig`，仅编译期使用，不随本适配包捆绑。
- .NET SDK 8.0.413：本机既有环境，适配器运行需要 .NET 8。
- 当前游戏 Pak v11 索引、Init.bnk v150、Wwise.uplugin 中的 2023.1.3.8471.2970：来自当前安装的游戏原文件；游戏媒体和银行不分发。
- Pak 格式参考：[CUE4Parse 原始实现](https://github.com/FabianFG/CUE4Parse/blob/master/CUE4Parse/UE4/Pak/Objects/FPakEntry.cs)，另参考 PakFileReader.cs / FPakInfo.cs。本次仅下载公开源文件作格式参考，自写有界读取器；未用已有游戏解包结果。
- Oodle 解压调查：[powzix/ooz](https://github.com/powzix/ooz)，GPL-3.0-or-later。公开原始源码在本任务内以 Zig 构建临时 DLL，仅读取未加密元数据；不链接到适配器、不随运行包分发。
- 银行结构调查：[bnnm/wwiser](https://github.com/bnnm/wwiser)，CLI 显示 v20260808，读取原始银行产生结构树；不生成模拟音频或使用名称数据库。运行包不依赖该工具。
- 接口资料：[Audiokinetic SetBusEffect / AkSoundEngine.h](https://www.audiokinetic.com/en/public-library/2024.1.5_8803/?id=AkSoundEngine.h&source=SDK)、[2023.1.3.8471.2970 发行说明](https://blog.audiokinetic.com/en/public-library/2024.1.5_8803/?id=releasenotes_2023_1_3_2970.html&source=UE4)。在线文档是邻近版本，仅作接口说明；实际采用发布套件已实现的 2023.1 ABI，并以当前 EXE 导出签名、银行版本、插件集成版本和实际回调验证。

下载时间为 2026-10-15；源码及工具摘要在 evidence/dependency-hashes.json。未分发下载的第三方源码或游戏资源。可用 investigation/prepare-ooz.ps1 重建离线调查所需解压库。
