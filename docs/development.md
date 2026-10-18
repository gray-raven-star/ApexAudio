# 开发入口与维护

新游戏从 [适配流程](gamepack-authoring.md)开始。复用条件查 [配置参考](engine-config-reference.md)；需要新定位或采集实现时查 [新模块开发](new-module.md)。不需要迁移旧工具配置，也不要求查阅维护者的历史工作目录。

主程序只处理统一协议和用户操作，游戏版本、定位和 ABI 在包内维护。公开协议确实不足时再提出通用扩展，不在宿主增加游戏名称判断。

协议实现见 src/ApexAudio.Contracts；Wwise、FMOD、Unreal 是采集实现参考，Mock 是合成声音的传输参考。Mock 通过不能作为真实游戏成功的依据。

构建使用 tools/Setup.ps1、tools/Build-GamePack.ps1 和 tools/Pack.ps1；完整命令见 [环境说明](build-environment.md)。构建目录 artifacts/、dist/、bin/、obj/ 和本机 .cache/ 是生成物，不作为对方开发依赖。

交付修改源码、完整包和实际验证范围。独立音轨、开关、退出与反馈的检查方式见 [验证说明](validation.md)。
