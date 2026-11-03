# 预设与录音数据（V1）

## 作者预设

适配包的 manifest.presets 引用预设文件。其原稿不在编辑时修改。

```json
{
  "schemaVersion": 1,
  "id": "author-default",
  "name": "作者配置",
  "packId": "author.game-name",
  "folders": [{"id":"folder:player","name":"玩家音效","parentId":null,"order":0}],
  "groups": {
    "stable-group-id": {"name":"玩家脚步","enabled":true,"folderId":"folder:player","order":0}
  }
}
```

`groups` 的键是 Adapter 稳定分组 id，不是列表下标。设置包括显示名称（null 使用原名）、反馈开关、显示文件夹和排序。复用已有预设时，相同稳定 ID 保留这些设置，尚无设置且未明确映射的新组默认关闭；暂时未出现的旧组继续保留，但不参与当前组操作和反馈。

folder.id 以 `folder:` 开头，同一预设唯一，父文件夹必须存在，禁止循环。显示文件夹不改变实际路由；actual parentId 始终由 Adapter 提供。

作者预设的宿主选择编号为 `author:<id>`，用户副本编号为 `user-<32位guid>`。副本附加 `basePresetId`、`basePackVersion`，用于还原。第一次保存作者预设自动生成用户副本，之后“保存”覆盖该副本，“另存为”生成新的副本。

主动选择或还原作者预设时，按工作台所用包版本的作者初始配置载入，再为原稿中缺失的当前组补充关闭设置。“还原作者初始状态”保留用户副本编号和名称，并标记未保存，不会立即覆盖磁盘上的用户副本。若作者更新移除了原稿，明确报错，不猜测另一个预设。

JSON Schema：`schemas/preset.schema.json`。导入要求 packId 匹配。导出为普通 JSON。卸载适配包仍保留用户副本。

## 适配当前声音组

`PresetStore.ResolveForGroups` 接收预设和当前组，返回独立的预设副本，以及新增、不可用、已映射和需检查的父混音组列表与提示。解析不修改输入预设，也不自动写入磁盘。载入、重连、保存后的提示刷新及导入使用这套解析；导入是用户明确建立副本的操作，保存行为不由解析触发。

调用方可以明确传入“不可用旧 ID → 全新 ID”的一对一映射：旧 ID 必须存在于原设置且当前不可用，新 ID 必须当前可用且尚无旧设置；不允许多对一、覆盖、拆分或合并。映射复制已知设置并保留历史项。当前没有新增适配包映射字段或界面映射编辑器，也不按名称或相似程度推断关系。复杂 ID 或语义变化由用户重新配置，不承诺任意更新无损迁移。

解析保留已启用父混音组的选择。已有预设中尚无设置的新子组独立反馈默认关闭，父混音仍可能包含它的声音；这时工作台提示检查包含关系。树中的横杠表示当前显示树内只有部分可采集成员被勾选，不表示已经从父混音中排除未勾子组。若需排除某些声音，应取消包含它们的父混音，改选所需独立组；仅整理显示文件夹不能改变混音内容。

## 独立录音

录制全部有 HasPcm 的组，不受反馈开关影响。每组一份 PCM16 / 48 kHz / 双声道 WAV。静音和缺失区间补零，各轨道统一原点和长度。一次最多 60 秒，定时模式默认 10 秒；手动模式按同一快捷键开始／结束，60 秒自动停止。停止时立即结束音频写入，再收集震动遥测尾部。

```
recordings/<session-id>/
  session.json
  track-0.wav
  track-1.wav
```

session.json 包含 schemaVersion、id、packId、packVersion、name、created、originTick、frequency、sampleRate、channels、duration、missingPackets、note、tracks、haptics。

每个 track：groupId、name、file（相对路径）、frames、peaks（每 10 毫秒一项峰值）。轨道名记录录制时显示名称，后续改预设不改历史录音。

每个 haptics.block：seconds、span、left、right、trigger、supplementLeft、supplementRight、supplementTrigger。值为该块峰值，缺少字段表示未提供，不能视作 0。显示默认对照声音补充分量与引擎合成输出，可切换单独来源。X-Haptics 未启动时会话仍保存声音，提示未录到震动。

试听页的“曲线放大”提供 1×、4×、10×，默认 4×，只放大震动曲线的显示高度。显示超过单个通道高度时截顶，原始幅度、录音、游戏包与手柄反馈强度均不改变。设为 1× 可查看原始比例。

声音使用 QPC 第一帧时间。Beta 的 X-Haptics 使用官方 captureMonoUs 和音频块长度进行近似对齐。正式版 samplingMethod=instantaneous-poll 时每个值只代表单次诊断查询，span=0，以请求中点近似定位，sampleIntervalMs 记录平均查询间隔；点间为空白，不能当作没有震动。时间轴允许手动 ±1000 ms 偏移。指令曲线不是马达物理测量，丢块空白不证明未震动。

试听请求读取一个 track 的指定时间区间，返回新的 WAV 片段。不会混入其他轨道，不修改原 WAV。游戏本身播放的声音仍存在，建议先暂停游戏再试听。
