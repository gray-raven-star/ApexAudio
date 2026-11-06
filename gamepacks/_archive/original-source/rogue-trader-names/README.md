# 战锤40K：行商浪人 — ApexAudio 适配包

Wwise 总线级独立采集适配包，用于把行商浪人的游戏音频分组送入 X-Haptics，并按需关闭对白、音乐等不想要的补充震动来源。

## 1. 支持范围

| 项目 | 值 |
|---|---|
| 游戏 | Warhammer 40,000: Rogue Trader（Steam，`WH40KRT.exe`） |
| 已验证版本 | 1.6.1 DLC3（`wh_release_1.6.1_DLC3_AfterRelease`，Unity 6000.0.64f1） |
| `WH40KRT.exe` SHA256 | `0A683B537726A6F52771B2FE70D32DBB5A13C5854C7D9ED9F7C8718EBEC9A186` |
| 音频引擎 | `WH40KRT_Data\Plugins\x86_64\AkSoundEngine.dll`，SHA256 `75E8589EBC09A3B044796F91B9EB17E7884430E57FA28C40D9C2B7F3CEB58656` |
| 中间件 | Wwise 2022.1.19（Build 8584），`WwiseUnity: Wwise(R) SDK Version 2022.1.19 Build 8584.` |
| 银行格式 | bank version 145（`Init.bnk`） |
| 分组数 | 80 个 Wwise 音频总线，其中 19 个默认开启 |

游戏升级后 `WH40KRT.exe` 或 `AkSoundEngine.dll` 摘要变化时，适配器会拒绝连接，需要重新核对银行版本和总线编号。

## 2. 分组从哪里来

1. 解析 `WH40KRT_Data\StreamingAssets\Audio\GeneratedSoundBanks\Windows\Init.bnk` 的 `BKHD`（版本 145，48 字节头）与 `HIRC`。
2. `HIRC` 中 80 个 type 8 对象为音频总线：偏移 0 是总线 ID，偏移 4 是父总线 ID（0 表示根）。
3. 两个根：`0xE2B7BC37`（主输出，18 个一级分支）与 `0x4D6E0F21`（第二输出，1 个一级分支）。
4. 总线的效果列表给出每个已占用的效果槽位：14 个总线带游戏自带效果（EQ / 压缩 / 限幅 / RoomVerb 等），其余 66 个为空。
5. `slot` 取“已占用槽位数”，即插入到游戏效果之后、且不覆盖它们；`GameDefinition.BuildConfiguration` 与原生模块按此挂载。

Wwise 银行**不保存总线名称**，本包因此使用结构编号：`A1`…`A18`、`A1-1` 等，根为“主输出总线 / 第二输出总线”。实际角色需要用户按试听确认；已测到的一级总线与信号见第 4 节。

## 3. 默认预设为什么只开一级分支

总线渲染输出已经包含其子总线的音频，父与子同时开启会让同一段声音被计算两次。因此默认只开启两个根的**一级子总线**（18 + 1 = 19 条），它们互不重叠且覆盖全部声音；根总线与更深层总线默认关闭，可在界面里按需打开。

## 4. 已验证（2026-10-17，本机实测）

| 阶段 | 结果 |
|---|---|
| 包完整性 | 通过（`ApexAudio.DevTools.exe check`） |
| 适配器启动 / 协议 | 通过，`GetAudioGroups` 返回 80 组（`check --launch`） |
| 原生模块自检 | 通过（`native-check --entry CaptureSelfTest` = 0） |
| 连接真实游戏 | 通过（主菜单场景，`CaptureConfigure` + `CaptureStart` 均返回 0；两次独立运行一致） |
| 效果挂载 | 80/80 `SetBusEffect` 成功（`attachFailures=0`，无 110 返回） |
| 渲染回调 | 通过，单次约 22 秒内 `published` = 1321 块（48 kHz，1024 帧/块） |
| 非零 PCM | 通过，主菜单实测：`0xE2B7BC37` 峰值 0.450、`0xEDF036D6` 峰值 0.286、`0x5C770DB7` 峰值 0.0002（瞬时） |
| 父子重叠 | 主输出 `0xE2B7BC37` = 各一级分支之和；一级分支的子总线当时为 0，说明声音直接挂在一级总线上 |

主菜单实测中 `0xEDF036D6`（`A15`）是持续有声的那条一级总线的候选音乐来源；想先关掉音乐震动时，可以优先试关 `A15` 分支。

## 5. 未验证

- 对白、战斗、环境音、UI 的具体总线归属（需要实际游玩场景；本次只在主菜单观测）。
- 界面里的“试听 / 监听”、预设开关对反馈混音的影响、断开后重连。
- X-Haptics 引擎与实体手柄的震动效果。
- DLC1–DLC3 独有总线：`Init.bnk` 只含通用总线表，未单独核对 DLC 内容。

## 6. 已知限制

- 分组名称为结构编号，不是 Owlcat 的项目原名。
- 第二输出 `0x4D6E0F21` 在实测中无信号（可能对应未使用的输出设备）。
- 采集模块以只读方式复制 PCM：不修改缓冲区、不改变音量、不替换游戏效果；效果插入在游戏自带效果之后。
- 原生模块编译参数为 `CAPTURE_BANK_VERSION=145`、`CAPTURE_SDK_YEAR=2022`、`CAPTURE_SDK_SERIES=1`。更换游戏版本时必须重新核对 Wwise ABI，不能只改年份。
- `game.json` 的 `engineSha256` 与 `captureSha256` 必须是大写十六进制：Wwise 适配器对这两个值做区分大小写的比较。

## 7. 重新构建

```powershell
# 在开发包根目录（含 ApexAudio.sln）
dotnet restore adapters/Wwise/ApexAudio.Wwise.csproj --configfile NuGet.Config -m:1
pwsh -File adapters/Wwise/tools/BuildPack.ps1 -DefinitionDirectory gamepacks/wh40k-rogue-trader -Compiler ./toolchain/zig/zig.exe
```

本机没有 PowerShell 7，构建改用等价步骤（`dotnet publish -c Release` → `zig cc -target x86_64-windows-gnu -shared -O2 -DCAPTURE_*` → `Pack.ps1` 同样的清单哈希与 ZIP 打包）。总线表由 `Init.bnk` 解析生成，见 `build/` 中的脚本与 `group-evidence.json`。

## 8. 作者与来源

- 适配定义：按 ApexAudio-DevKit-2026.10.17 流程，从本机游戏文件自行解析制作，未使用 Owlcat 项目文件。
- 采集实现与协议：ApexAudio 开发包自带的 Wwise 适配器与 `ApexWwiseCapture.c`，未修改。
- 本包不包含游戏音频、银行文件或账号数据。
