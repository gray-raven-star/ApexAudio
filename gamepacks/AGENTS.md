# 官方适配包工作目录约定

这些规则适用于 `gamepacks/` 下的定义、预设和专用采集器。按[识别接口](../docs/authoring/identify-audio-system.md)、[制作适配包](../docs/authoring/build-a-gamepack.md)和[验证采集与反馈](../docs/authoring/verify-a-gamepack.md)推进。

- 先读现有定义、`PROVENANCE.md` 和已有验证记录，核对目标游戏构建、文件摘要和证据。游戏专用的地址、接口和采集逻辑归适配包。
- 版本、摘要、地址、ABI 和分组都要有来源。证据不足时标记未确认。分组证据记入 `group-evidence.json`，预设使用适配器返回的稳定分组 ID。
- 保持已发布包 ID。构建配方的目录名、包 ID、版本和后端须与目录、清单及游戏配置对应。旧发布配置留在 `_history/`，原始材料和旧采集快照留在 `_archive/`。
- 从源码根目录运行 `tools/Build-GamePack.ps1`，将 `-DefinitionDirectory` 指向要构建的游戏目录。先做只读探查和完整性校验，启动游戏、连接或加载采集模块按当前任务的授权执行。
- 分别记录构建、协议、非零 PCM、独立音轨和手柄反馈。未执行的阶段标记为未测，包内说明只写实际验证范围。
