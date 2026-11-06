# 适配包工作目录约定

这些规则适用于本适配包的定义、预设和专用采集器。开始前先确认 ApexAudio 源码或 DevKit 的根目录，按其中的 `docs/authoring/identify-audio-system.md`、`docs/authoring/build-a-gamepack.md` 和 `docs/authoring/verify-a-gamepack.md` 推进。

- 先读现有定义和 `validation-report.json`，核对目标游戏构建、文件摘要和已有证据。游戏专用的地址、接口和采集逻辑归适配包。
- 版本、摘要、地址、ABI 和分组都要有来源。证据不足就保留占位或标记未确认，不要照抄其他游戏的配置。分组证据记入 `group-evidence.json`，预设使用适配器返回的稳定分组 ID。
- 在 ApexAudio 源码根目录用 `tools/Build-GamePack.ps1` 构建，`-DefinitionDirectory` 指向本目录。先做只读探查和完整性校验，启动游戏、连接或加载采集模块按当前任务的授权执行。
- 在 `validation-report.json` 分别记录构建、协议、非零 PCM、实测分轨和手柄反馈。未执行的阶段标记为未测，包内 `README.md` 只写实际验证过的范围。
