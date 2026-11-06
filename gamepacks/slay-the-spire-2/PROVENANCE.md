# 杀戮尖塔 2适配包来源

当前定义取自源码归档的 `definitions-v1/slay-the-spire-2/`。包 ID 为 `apex.slay-the-spire-2`。版本 `1.0.1` 的当前定义与已发布版本对应。

重建使用共用 FMOD 采集器，按[归档验证记录](validation-report.json)恢复 V1 使用的 Studio System 句柄定位。句柄值来自该记录，可能随运行时版本或初始化顺序变化。[配置参考](../../docs/reference/engine-config-reference.md#fmod-studio)说明了此定位模式。

本次重建未进行游戏连接或采集实测。能否逐字节重现发布时的 V1 二进制尚未确认。
