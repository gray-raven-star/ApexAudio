# V2 本地试验

本分支是 0.2.0 FMOD 试验，不是已经覆盖全部后端的 V2 正式格式。V1 原有 .gpack 仍通过包内入口运行。IPC 与音频共享内存格式保持 V1。

V2 .gpack 仅含 JSON、说明和许可，不包含入口程序。清单使用 schemaVersion=2、minHostVersion、profiles；每条 games 记录通过 EXE 文件名、完整 SHA256 和 profileId 指向采集参数。多个构建可引用同一 Profile；匹配多个不同 Profile 时拒绝自动选择。试验期所有 EXE 和关键模块均精确匹配，不实现未知版本警告放行。

每个 Profile 提供 id、parameters 和 backend（id、version、variant、descriptorSha256）。后端离线分发为 .gbackend，在游戏适配包的同一个导入按钮选择。后端安装在 data/backends/<id>/<version>，同版本重导入只接受相同内容；两个游戏共用同一份文件，每次连接各自启动进程。后端路径、DLL 和摘要来自后端描述，配置包不能另指 DLL 或下载地址。

程序生成本次会话的启动上下文，后端重新验证描述和文件；GetBackendInfo 返回身份、变体与上下文摘要，ValidateTarget 只读检查目标进程、启动时间、EXE 和已加载模块。Connect 在注入前重验。上下文在关闭后删除。录音、反馈仍走独立数据路径；未开始物理手柄测试。

FMOD 参数延续原定义，增加 locator=wrapper 或 studioHandle，后者使用 studioHandle 数值。buses[].stableId 是保存选择用的稳定 ID；原 id 仍是 FMOD GUID，父组通过 GUID 转换。名称路径改变时需作者明确保持或迁移 stableId，不能自动按语义猜对应关系。

共享原生模块覆盖 FMOD 2.02/2.03。2.03 的 System_GetVersion 有第三个输出指针，必须按系列分开调用；两参数形式不能直接用于 2.03。tests/fmod-version-probe.c 在独立无声进程验证同一个版本查询辅助函数。SDK 110 不替代 API 签名核对。

作者不必编译游戏采集器：准备定义目录后运行预编译的 ApexAudio.PackV2 <定义目录> <输出.gpack>。工具生成文件摘要并检查格式，不连接游戏。协议检查可用 DevTools check --pack 文件 --backend 后端文件 --launch；真实采集追加 capture、--pid 和 --allow-game-connection。

维护共享后端时，可运行 tools/BuildV2Backend.ps1 -Compiler <zig.exe> -Destination <新的.gbackend>，独立构建托管入口和原生 DLL 并生成 backend.json；不要复用已发布的后端版本标识来替换内容。重建后需更新配置清单中的 descriptorSha256。tools/Prepare-V2Trial.py 仅是本机从现有 V1 定义准备两款游戏试验数据的脚本，路径是本机数据位置，不是通用新游戏扫描器。示例定义可直接解压试验 .gpack 查看，作者工具不依赖本机原始 V1 数据。

这一轮尚不提供：Wwise/Unreal V2、在线后端目录、来源认证或撤销、自动 V1 预设迁移、包历史版本激活与回退、未知游戏构建放行。用户录音和预设仍独立保存，但把同一个包 ID 的 V1 替换成稳定分组 V2 前，需先显式转换设置。本次分发在独立 data 目录，当前保存的选择烘焙成示例包内的“动作与环境/音效与环境”预设。

社区入口按用户要求在 V2 实测和体验确认后继续。
