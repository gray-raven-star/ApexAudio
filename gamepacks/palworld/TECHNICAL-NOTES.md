> 历史技术说明：此文档保留自之前的包版本，可能描述旧版本的验证状态或默认开关。当前包版本、匹配范围和默认预设以 README.md 与 manifest.json 为准。
# 幻兽帕鲁 · ApexAudio 音频适配包 0.1.0

本包独立使用 ApexAudio DevKit 0.1.0、当前游戏原文件及公开格式资料制作，没有读取旧适配包、旧定位结果或维护者的历史调查。

## 使用

1. 打开 ApexAudio 0.1.0，在适配包管理中导入 `Palworld-ApexAudio-0.1.0.gpack`。
2. 按程序提示确认本包来源；启动游戏后连接。
3. 作者初始配置的反馈开关全部关闭。按需选择音频组；录制独立音轨不受反馈开关影响。
4. 先选一条音频总线测试。主混音包含子组，父组与子组同时用于反馈会叠加。

适用于本机 Steam build **25246127**，主程序为 `Palworld-Win64-Shipping.exe`。
主程序 SHA256：`E590B5E7BFAA3FEA40FAB1A02CC72C8FC5FD6F8631EF2308E95AC56C25195837`。
游戏更新后若摘要改变，本包会拒绝连接，需要重新适配；不会跳过版本校验。

## 内容与边界

- 20 条普通音频总线，父子路由来自原始 Init.bnk；9 条辅助混响总线记录在定义中，不冒充可独立采集组。
- 通过游戏主程序导出的 Wwise 接口，在经银行核对为空闲的第 3 号效果槽加载透传采集器，只复制 PCM。
- 所有普通总线在本次连接中返回挂接成功；这不等于所有组都已发声或语义都已确认。
- 英文名称通过 Wwise FNV 与当前 EXE 字符串对应，中文为便于使用的释义；仍未逐项试听确认。没有名称依据的组保留编号。
- 不含模拟音轨、不复制总混音伪装分组，不修改游戏文件、数值、存档或玩法，不绕过反作弊。
- 首次连接会在游戏进程加载本包采集模块和临时效果银行；断开会移除采集效果与回调。模块会驻留到游戏退出。
- .NET 8、ApexAudio 运行环境及可选的 X-Haptics 由用户原有环境提供；没有捆绑游戏媒体或中间件 SDK。

## 已做验证

完整性和适配器协议通过；原生 CaptureSelfTest 返回 0。标题画面采集到 BGM 和主混音；游戏内采集到环境、音效、玩家语音和主混音，均为真实非零 PCM，未报告丢块或传输错误。

空反馈选择、选中主混音、取消选择、停止/恢复采集、断开/重连均通过。空选择时反馈缓冲峰值为 0，独立录音仍可接收声音。

细分分组、各动作语义、不同场景、游戏退出场景及物理手柄震动尚未完整验证；依用户要求留待自行测试。完整阶段记录见 `validation-report.json`。本次测试最终已断开，未启动 X-Haptics 或物理手柄输出。

## 复现构建

配套 `Palworld-ApexAudio-0.1.0-source.zip` 包含本次定义、使用的后端源码、公共依赖源码、构建工具、调查脚本及验证记录。未改动 ApexAudio 主程序或通用后端代码。

在源码包根目录，用 PowerShell 7 执行（编译器位置按实际 DevKit 修改）：

```powershell
pwsh -File tools/Build-GamePack.ps1 -DefinitionDirectory gamepacks/palworld -Compiler '完整DevKit路径/toolchain/zig/zig.exe'
```

需要 .NET 8 SDK 和 DevKit 随附的 Zig 0.15.2。输出 `dist/packages/independent.palworld.wwise.gpack`；文件名可改为发布文件名。运行依赖不需要 ooz 或 wwiser，它们仅用于这次离线调查。

公开资料来源及版本摘要见 `DEPENDENCIES.md`。
