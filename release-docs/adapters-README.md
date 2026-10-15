# 采集后端

| 目录 | 内容 |
|---|---|
| Wwise | SoundEngine 效果器与渲染回调采集 |
| Fmod | Studio Bus / Core DSP 采集 |
| Unreal | AudioMixer Submix 监听采集 |
| Mock | 不连接游戏的开发用生成音轨 |

先读 [开发入口](../START-HERE.md)，再读 [配置字段及限制](../docs/backend-configs.md)、[调查方法](../docs/investigation.md) 和 [新模块开发](../docs/new-module.md)。

共享接口位于 `src/ApexAudio.Contracts/`；可选进程操作辅助位于 `src/ApexAudio.Windows/`。主程序不判断游戏名，后端并不保证同引擎所有版本兼容。开发套件不包含目标游戏配置和旧调查数据。
