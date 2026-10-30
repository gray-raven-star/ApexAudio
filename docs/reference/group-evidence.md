# 分组、名称、效果槽与初始预设

分组表是目标音频路由的描述；显示文件夹和 category 只整理界面，不改变实际声音流向。数据来源、命名依据与运行时声音应分开记录。

## 元数据如何取得

- Wwise：用支持实际银行版本的读取器解析对象类型、Bus ID、父关系和 FX 记录。DevTools bank-index 只读 chunk 与 HIRC 索引，不能代替版本相关的内部字段解析。辅助总线与普通总线分开处理。
- FMOD：核对当前活动 Studio System，使用对应 API 的 Bank / Bus 枚举取得 GUID、路径和路由。离线银行清单不能证明游戏正在使用这个实例。
- Unreal：资源里的 Submix 名称提供候选，实际设备中的对象及路由决定可用性。名称相同不证明对象布局或监听接口相同。

保留元数据输入摘要与解析器版本。补丁、DLC 和本地化覆盖层需按实际加载顺序合并，不能把扫描到的第一份同名资源直接认定为当前版本。

## 名字从哪里来

依次考虑游戏保留的名称、配套名称元数据、可公开取得的名称参考和人工试听。中文翻译保留原名；不知道就保留稳定编号，不把猜测标成原始名称。

对于哈希 ID，有限名称表可产生候选，但不能保证原始大小写、唯一性、对象类型或语义。参考表来自其他平台或版本时，只接受与当前目标确实对应的条目，记录来源与冲突。不要把整个表或另一平台配置原样移植。

随附通用候选词表 tools/Metadata/common-audio-names.txt。它不是目标游戏的名称答案；保留全部竞争候选，未匹配项继续用编号。可以从实际游戏字符串和来源可核对的公开资料扩充，而不是无界穷举。

可先直接运行：

```powershell
python tools/Metadata/name_candidates.py --definition gamepacks/example/game.json --candidates tools/Metadata/common-audio-names.txt --output evidence/name-candidates.json
```

输入为已经取得的当前分组表，输出是候选报告，脚本不自动改名。不要仅因 Init.bnk 没有文本名表就写“任何工具都无法恢复”。

已有工具：Python 3 运行 `tools/Metadata/name_candidates.py --definition gamepacks/example/game.json --candidates evidence/names.txt --output evidence/name-candidates.json`。输入每行一个候选名，工具按当前 Wwise FNV-1 实现匹配定义中的 ID，输出所有竞争候选，不自动改配置。它不是音频识别器或穷举破解器。

## Wwise 效果槽依据

当前构建器读取定义旁的 group-evidence.json。每个非辅助、slot>=0 的路由应有一条对应 ID 记录：

```json
{
  "schemaVersion": 1,
  "packId": "author.example",
  "packVersion": "0.1.0",
  "groups": [
    {
      "id": "123456",
      "effects": {
        "selectedSlot": 3,
        "occupiedSlots": [0, 1],
        "verification": "static-only",
        "source": "evidence/parsed-routing.json",
        "sourceSha256": "<上述证据文件的64位SHA256>",
        "targetSha256": "<实际音频模块的64位SHA256>"
      }
    }
  ]
}
```

这是结构示例，编号和槽位不能用于实际目标。verification 目前接受 static-only 或 runtime-verified。source 应可追溯到解析记录，证据中注明原银行摘要；targetSha256 必须匹配 game.engineSha256。

已有 FX 槽不能覆盖。槽满时设置 slot=-1，记录“仅元数据／未采集”，或者开发有依据的其他观察点。静态空槽不保证运行时仍空；有动态配置时核对当前所有权和释放行为。

SetBusEffect 返回成功、创建插件和执行音频是不同事件。不要用成功返回码代替真实 PCM；不要在断开时清掉其他代码后来占用的槽。当前后端的实际生命周期限制需按目标核对。

## 层级与初始预设

真实父关系由 AudioGroup.parentId 报告，containsChildren 表示父音轨实际包含子组。父子试听重叠可能正常；混响返回也可能混合多种声源，名称不能把已经混合的声音拆开。

用户命名、反馈开关和界面文件夹放 presets/initial.json，manifest.presets 引用它。groups 键是稳定 ID 字符串，packId 与 manifest.id 一致；不使用列表下标。未列出的新增组默认关闭。

把用户调好的设置作为初始预设时，优先从应用导出该预设，保留 groups 与 folders，给包原稿设置稳定的 id、name 和对应 packId，移除用户副本的 basePresetId / basePackVersion，复制到包的 presets/ 并重新构建。不要把整个用户 data/ 打进包。更新后“还原初始状态”使用当前包内原稿，不会自动覆盖用户已有副本。

格式与存储行为见 [预设与录音](presets-recordings.md)。独立录音范围和反馈勾选分开：关掉送给 X-Haptics 的声音不应停止该组的独立录音。

## 不同系统选择不同找名路线

| 系统 | 优先路线 | 缺失时继续查 |
|---|---|---|
| Wwise | SoundBanksInfo、生成的名称文件、资源名称 | 有限名称哈希匹配、程序或资源字符串；并非所有 ID 由名字产生 |
| FMOD Studio | strings bank 中的 GUID／路径映射，通过 GetPath / lookupPath 查询 | 其他名称元数据、集成代码或资源引用；不能把 GUID 当成名字哈希去倒推 |
| Unreal 自带 AudioMixer | Submix 资源、对象名称与路径 | 当前设备和名称池；确认真正使用的是自带系统还是 Wwise / FMOD |
| 自研或其他 | 正式接口、资源索引、名称表与代码引用 | 先确认 ID 生成方式和对象身份，无法确认就保留编号 |

FMOD 的银行已加载，不代表 strings bank 也已加载。名称查询失败时仍可保留 GUID，不应把缺少名称当成缺少总线或无法采集。可用 first-probes.md 中的独立工具验证元数据读取；实际父子路由仍要核对，不能只按名称路径推断。官方说明：[GUID 与路径](https://fmod.com/docs/2.04/studio/advanced-topics.html#guids-and-paths)。
