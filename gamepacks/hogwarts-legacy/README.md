# 霍格沃茨之遗 · ApexAudio 0.1.0

本包适用于 Windows x64 的 Epic Games Store 构建 `2153883`。游戏进程和 Wwise 引擎均按主程序 `HogwartsLegacy.exe` 的 SHA-256 `9CAB3D4214EC292BC7829CB662D25EB8817B9FFC14E33BBB360913F4273B1213` 匹配。

在 ApexAudio 中导入 `local.hogwarts-legacy.gpack`，启动游戏后选择对应进程并连接。启用反馈时，初始预设使用音效（SFX）和界面（UI）总线。父总线已选中时，其子组的声音随父总线一同送入反馈。

适配包为主音频树的 116 条普通总线配置了采集槽，另外 10 条普通总线仅显示元数据。42 条辅助总线记录在源码定义和路由证据中。通过 Wwise ID 匹配的显示名称属于候选，声音语义有待逐项试听。

实际采集、独立音轨和反馈的验证范围见 [validation-report.json](validation-report.json)。
