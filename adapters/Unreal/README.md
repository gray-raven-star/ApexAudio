# Unreal AudioMixer 适配器

独立进程和原生模块实现 V1 协议、独立音轨与实时混音。宿主只接收统一 PCM 和 `apex-f32-planar-ring-v1` 映射，不判断游戏或引擎类型。

原生模块通过 AudioMixer 的 Submix 监听接口复制音频，不改音量、音频数据、引擎配置或可执行代码。游戏对象、名称池和监听方法的位置由包内 game.json 提供，适配器先核对主程序 SHA256。地址只适用于对应的游戏文件，不能直接套到另一个版本。

## 配置

`name`、`executable`、`executableSha256` 是名称、程序文件名和摘要。

`engineRva` 是主引擎指针的模块相对位置；`holderOffset` 和 `deviceOffset` 指定引擎到音频设备的地址链。`vtableRva` 用于核对最终设备类型。

`nameBlocksRva` 指定名称池的块指针数组；当前读取器支持已核对的 FName 块索引／偏移布局。`registerRva`、`unregisterRva` 是该设备的 Submix 监听方法地址。此布局没有宣称覆盖所有虚幻版本。

`captureFile`、`captureSha256` 指定原生模块及摘要。构建工具使用带内容摘要的文件名，避免重连时复用旧模块。

`buses` 最多 64 项。每项包含稳定 `id`、引擎中的 `nativeName`、显示 `name`、`category`、`parentId`、`containsChildren`。运行时按 nativeName 匹配对象，不使用本次启动的对象地址作为编号。连接后读取当前混音父子关系；未找到的分组标为无独立音轨。

当前已核对的监听接口从虚函数槽 0 调用 OnNewSubmixBuffer，不包含前置虚析构函数。不能直接依据另一版 Unreal 头文件推断虚函数顺序；移植到其他接口布局时需修改包内原生适配器。自测通过此槽调用，覆盖监听器到独立录音和反馈混音的路径。

## 音频

当前处理 48 kHz、每块不超过 2048 帧、1～16 通道的交错 float PCM，按标准通道权重转成双声道。以引擎音频时钟归并同一次渲染的分组，下一个时钟到来时发布前一块混音，增加一个渲染块的等待；不会等待宿主录音或管道传输。

所有分组均可录制，反馈仅合成已选分组。父混音组与其子组的去重由宿主现有逻辑完成。采集接口不等于物理马达测量。

## 构建与验证

使用 `tools/BuildPack.ps1 -DefinitionDirectory gamepacks/stellarblade -Compiler <C编译器>` 构建。

本轮完成离线导入、协议和原生回调的人工数据自测；真实游戏中的监听注册、分组音频和反馈效果尚未验证。用户导入的实际测试不应被离线自测替代。

接口参考：[Epic Submix 文档](https://dev.epicgames.com/documentation/en-us/unreal-engine/submixes?application_version=4.27)、[ISubmixBufferListener](https://dev.epicgames.com/documentation/unreal-engine/API/Runtime/Engine/ISubmixBufferListener)。游戏布局来自本机只读核对。
