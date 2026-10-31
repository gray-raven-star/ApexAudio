# V2 本地试验

本分支是 0.2.0 FMOD 试验，不是已经覆盖全部后端的 V2 正式格式。源码来源、验证边界和后续建议见[试验基线](owner-v2-baseline.md)。V1 原有 .gpack 仍通过包内入口运行。IPC 与音频共享内存格式保持 V1。

V2 .gpack 仅含 JSON、说明和许可，不包含入口程序。清单使用 schemaVersion=2、minHostVersion、profiles；每条 games 记录通过 EXE 文件名、完整 SHA256 和 profileId 指向采集参数。匹配多个不同 Profile 时拒绝自动选择。清单允许多条 games 记录引用同一 Profile，但 FMOD 参数仍含一份 ExecutableSha256 并在后端强制校验，因此尚不能让 EXE 摘要不同的构建真正复用该 Profile。多文件构建证据与采集参数的分离仍是待实现方案。试验期所有 EXE 和关键模块均精确匹配，不实现未知版本警告放行。

每个 Profile 提供 id、parameters 和 backend（id、version、variant、descriptorSha256）。后端离线分发为 .gbackend，在游戏适配包的同一个导入按钮选择。后端安装在 data/backends/<id>/<version>，同版本重导入只接受相同内容；两个游戏共用同一份文件，每次连接各自启动进程。后端路径、DLL 和摘要来自后端描述，配置包不能另指 DLL 或下载地址。

程序生成包含包、Profile、后端和参数的启动上下文，后端重新验证描述和文件；GetBackendInfo 返回身份、变体与上下文摘要。当前上下文不含选定目标的 PID、启动时间或路径。ValidateTarget 从请求读取目标，只读核验 EXE、已加载模块，并在后端记忆首次检查的进程启动时间；Connect 在注入前重验。该检查不等于完整目标身份已固定在上下文，也不消除检查与使用之间的竞态。正常关闭时删除上下文；异常启动和清理路径仍需专项测试。录音、反馈仍走独立数据路径；未开始物理手柄测试。

FMOD 参数延续原定义，增加 locator=wrapper 或 studioHandle，后者使用 studioHandle 数值。buses[].stableId 是保存选择用的稳定 ID；原 id 仍是 FMOD GUID，父组通过 GUID 转换。名称路径改变时需作者明确保持或迁移 stableId，不能自动按语义猜对应关系。

共享原生模块按 FMOD 2.02/2.03 分支调用版本查询，2.03 分支传入第三个输出指针。Owner 报告修复前出现游戏访问冲突，并使用 tests/fmod-version-probe.c 在独立无声进程检查版本查询辅助函数；原始过程见根目录 V2-验证记录.md。本次整理未独立核实对应 SDK 的 ABI 或复测真实游戏。发布前仍需针对支持的接口系列核实签名并完成运行验证。

作者不必编译游戏采集器：准备定义目录后运行预编译的 ApexAudio.PackV2 <定义目录> <输出.gpack>。工具生成文件摘要并检查格式，不连接游戏。协议检查可用 DevTools check --pack 文件 --backend 后端文件 --launch；真实采集追加 capture、--pid 和 --allow-game-connection。

基线协议回归优先运行 `pwsh -NoProfile -File tools/TestV2.ps1 -LegacyPack artifacts/packages/demo.gpack`，校验随包样例摘要并构建专项测试；先用 `Build.ps1 -Test` 生成该 V1 Mock 包。追加 `-Compiler <zig.exe>` 会在独立的 artifacts/source-v2-<随机标识> 中重建后端与打包工具，为测试配置更新描述摘要后再测。两种模式均不连接真实游戏。未指定 `-LegacyPack` 时仅运行 17 项 V2 检查；显式传入旧包后增加一项 V1 回归检查。源码重建仍使用试验版本 0.2.0，仅供本地验证，不是可以覆盖正式发布的替代包。

维护共享后端时，可运行 tools/BuildV2Backend.ps1 -Compiler <zig.exe> -Destination <新的.gbackend>，独立构建托管入口和原生 DLL 并生成 backend.json。脚本当前固定版本为 0.2.0，新的输出文件名不改变版本身份；不要复用已发布的后端版本标识分发不同内容。重建后需更新配置清单中的 descriptorSha256。tools/Prepare-V2Trial.py 仅是本机从现有 V1 定义准备两款游戏试验数据的脚本，路径是本机数据位置，不是通用新游戏扫描器。tests/fixtures/v2/ 保存随包试验文件；示例定义可直接解压其中的 .gpack 查看，作者工具不依赖本机原始 V1 数据。

这一轮尚不提供：Wwise/Unreal V2、在线后端目录、来源认证或撤销、自动 V1 预设迁移、包历史版本激活与回退、未知游戏构建放行。后端安装目录按 ID/版本区分，配置包仍安装至 packs/<id> 并替换旧包，未在替换前保证后端依赖齐全。显式离线导入及摘要检查不构成来源认证。用户录音和预设仍独立保存，但把同一个包 ID 的 V1 替换成稳定分组 V2 前，需先显式转换设置。Owner 试验分发使用独立 data 目录，当前保存的选择烘焙成示例包内的“动作与环境/音效与环境”预设。

社区入口、目录和来源政策属于后续工作；当前代码不提供社区内容认证。
