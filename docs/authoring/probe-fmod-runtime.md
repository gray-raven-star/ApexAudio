# 探查 FMOD 运行库与活动实例

开始扩展采集器前，把“未知”与“已经证实不兼容”分开。每轮只回答一个问题，先选能推翻当前假设的低成本观察；没有实验条件就记录缺失条件，不填成失败，也不为了填表强行测试。

适配时的一般故障分层见[排查采集与反馈问题](troubleshooting.md)。

## FMOD 自检工具

提供独立程序 prebuilt/fmod-probe/ApexAudio.FmodProbe.exe，Windows x64、.NET 8，不依赖 Python 或 REA。源码在 tools/FmodProbe，可用 dotnet publish 重建。显式指定可信的本地 FMOD 运行库；该脚本在自己的进程里加载和调用它们，不读写游戏进程，也不播放声音或启动手柄反馈。不要向它传不可信 DLL。

```powershell
prebuilt/fmod-probe/ApexAudio.FmodProbe.exe --core "D:/Example/fmod.dll" --studio "D:/Example/fmodstudio.dll" --header-version 0x00020306 --output evidence/fmod-probe.json
```

路径和 header-version 是示例，须替换为实际核对值。版本整数对应 FMOD_VERSION，不是插件 SDK 编号，也不能从名字猜版本。此工具显式覆盖 2.02/2.03 的 SDK-110 描述布局；遇到 2.04 不会把 110 改成 204 强行继续。

依次检查文件声明的版本族与请求一致、加载库、Studio System 创建、Core 查询、运行版本、无声输出初始化、DSP 创建和在自身主通道挂接，最后移除并释放。DSP 挂接期间挂起混音，移除后恢复，因此不验证实际音频回调。每一阶段保留返回码；清理结果也单独记录。报告含本机路径，分享前检查隐私。

带 --bank 可重复加载提取到的银行，只读元数据，不请求媒体采样加载或播放：

```powershell
prebuilt/fmod-probe/ApexAudio.FmodProbe.exe --core "D:/Example/fmod.dll" --studio "D:/Example/fmodstudio.dll" --header-version 0x00020306 --bank evidence/Master.bank --bank evidence/Master.strings.bank --output evidence/fmod-banks.json
```

文件名只是示例。脚本先加载全部传入银行，再枚举 Bus GUID 和路径；缺少 strings bank 时路径查询可能失败，GUID 仍有用。它不推断父子树，不找游戏实例，不验证实际音频回调。厂商库拒绝银行时先读返回码，检查版本、依赖、加密与必要银行，不能直接认定需要自写解析器。

FMOD 产品版本与 DSP SDK 编号是不同体系。官方从 2.03 到 2.04 的迁移说明记录插件编号由 110 变成 204；这不意味着所有 2.02/2.03 集成无需验证即可复用。来源：[官方迁移说明](https://fmod.com/docs/2.04/api/dsp-plugin-api-guide.html)。枚举接口见 [Studio Bank](https://fmod.com/docs/2.03/api/studio-api-bank.html)。

## 寻找活动实例

通过 API 返回的不透明指针或整数句柄，应按 API 约定使用，不能先假定它指向 C++ 虚表。先查创建、存储和释放路径，以及当前集成是否有公开源码；再决定使用导出、引擎单例、指针链或包内定位器。

多次运行出现同一个句柄只是一条观察。须核对有效性、所属 System、初始化和释放时机、多实例或设备重建行为；不能把“自己进程里两次相同”当成游戏全程固定的证据。不盲扫整数句柄、不把本轮结果硬编码成通用配置。

现有后端的 wrapperRva / stateOffset 是其实现假设，不是所有游戏都必须满足的结构。结构不符时在包内更换定位或验证逻辑，保留正式有效性与版本核对，不为了通过而取消验证。

## 把环境故障单列

工作区进程权限、Windows 完整性级别、编译器、临时目录、终端编码属于环境。容器版本超出现有读取器范围属于工具覆盖。分别记录它们，按 [排查表](troubleshooting.md)解决，不把它们当成游戏不支持音频接口的证明。

没有足够证据时报告“本轮未确认，下一步是什么”，而不是承诺一定兼容或直接宣布无法继续。物理手柄和完整游戏场景按任务约定验证，不为研究一个接口额外要求用户重复游戏操作。

## 相同插件编号不代表每个函数的签名都不变

例如 FMOD 2.03 的 C 接口 FMOD_System_GetVersion 接收 system、version、buildnumber 三个参数；旧 2.02 读取器只有前两个。漏掉输出指针可能写坏调用方内存，即使碰巧返回成功，也不能判定调用正确。工具按已核对的版本族分别处理，并在进入原生调用前核对 DLL 版本族。来源：[2.03 官方签名](https://www.fmod.com/docs/2.03/api/core-api-system.html#system_getversion)。

不能因为 DSP SDK 110 仍有效就跳过其他函数，也不能反过来把某个函数不同推断为全部采集实现都得重写。对实际用到的函数逐项核对。
