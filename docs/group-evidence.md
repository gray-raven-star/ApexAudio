# 分组与效果槽证据

`tools/New-Pack.ps1` 创建空的 `group-evidence.json`。根据目标游戏调查结果填写 `groups`；单组字段可参考 `templates/group-evidence.json`。后端配置中的 `buses` 定义采集路由，本文件记录这些定义的依据。

## 通用字段

| 字段 | 填写内容 |
|---|---|
| `packId` / `packVersion` | 与本次 `manifest.json` 一致 |
| `groups[].id` / `parentId` | 稳定分组编号和实际父组；与适配器中的分组对应 |
| `objectType` | 从元数据或运行对象确认的类型；尚未确认时保留 `unverified` |
| `names` / `nameSource` | 名称候选及来源，区分原始元数据、哈希候选和试听标注 |
| `auditionConfirmed` | 是否在实际场景试听确认了该分组的含义 |
| `category` / `categoryInheritedFrom` | 显示分类及继承来源；分类不改变音频路由 |
| `evidenceFiles` | 与该分组相关的解析报告、诊断或录音记录路径 |

同一编号存在多个名称候选时保留候选及来源，先以编号显示。试听记录应说明场景和实际听到的声音，便于之后复测。

## Wwise 的效果槽

Wwise 通过 Bus 的效果槽挂载采集效果器。对每个 `auxiliary=false` 且 `slot>=0` 的 Bus，添加一条相同 `id` 的证据记录，并填写 `effects`：

| 字段 | 填写内容 |
|---|---|
| `occupiedSlots` | 已确认由游戏使用的槽编号，范围 0～3 |
| `selectedSlot` | 本包选择的槽编号，与 `game.json` 中 `slot` 一致，且不在占用列表中 |
| `verification` | 从资源分析取得时写 `static-only`；经运行时核对时写 `runtime-verified` |
| `source` / `sourceSha256` | 据以判断占用情况的文件或报告，以及该文件实际 SHA256 |
| `targetSha256` | 本次目标引擎模块的 SHA256，与 `game.json.engineSha256` 一致 |
| `checkedAt` | 检查时间 |
| `runtimeOwnershipVerified` | 是否确认运行时该槽的实际使用者 |

四个槽均占用时，将该 Bus 的 `slot` 设为 `-1`，保留显示元数据并选择其他采集点。静态槽信息可能受运行时切换影响，应在目标场景检查挂载前后的音频和槽位。

## 构建器实际检查的内容

`Build-GamePack.ps1` 的 Wwise 分支读取本文件，核对包身份、每个可采集 Bus 恰有一条记录、选中槽不冲突、验证类型、来源字段及摘要格式，并比较目标引擎摘要。

构建器不会打开 `source` 重新解析效果槽，也不会执行试听；`auditionConfirmed`、`checkedAt` 和 `runtimeOwnershipVerified` 由开发者根据实测填写。保留来源报告，供之后复核这些判断。

旧定义缺少证据文件时，当前构建器给出警告后继续；通过 `New-Pack.ps1` 新建的定义已有空文件，需填完可采集 Bus 的记录才能通过该检查。直接调用底层 Wwise `BuildPack.ps1` 或 `Pack.ps1` 不执行这项证据检查。
