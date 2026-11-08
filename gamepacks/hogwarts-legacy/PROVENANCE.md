# 霍格沃茨之遗适配包来源

## 游戏构建与接口

Epic 安装清单标记构建 `2153883`。从运行进程只读核对的主模块为 `Phoenix/Binaries/Win64/HogwartsLegacy.exe`，其摘要与 [manifest.json](manifest.json) 一致。该模块导出的 `IsInitialized`、`GetAudioSettings`、`SetBusEffect`、`LoadBankMemoryCopy`、`RegisterPlugin`、`RegisterGlobalCallback` 和 `UnregisterGlobalCallback` 修饰名均匹配通用 Wwise 采集器使用的签名。游戏的 Wwise 插件描述符标记版本 `2021.1.13.8036.2580`。

## 银行与总线

使用仓库的 `tools/Metadata/read_ue_pak.py` 和 [powzix/ooz](https://github.com/powzix/ooz) 从 `pakchunk0-WindowsNoEditor.pak` 读取 `Init.bnk`，再用 [wwiser](https://github.com/bnnm/wwiser) `v20260808` 解析路由。银行格式为 `140`，SHA-256 为 `4eefb9350c8516662d19b974a2a88df43a6dde95220fd81bcadcf5a29fa1c539`。这些解包和解析工具仅用于离线取证，运行包不包含银行、游戏媒体或第三方解包器。

[路由证据](evidence/bus-routing.json)记录总线 ID、父总线与已有的效果槽。[分组证据](group-evidence.json)引用该文件，为主音频树中可采集的普通总线记录静态确认的空槽。实际音轨验证见 [validation-report.json](validation-report.json)。

## 初始预设

[初始预设](presets/initial.json)使用用户确认的已保存预设，按普通总线的稳定 ID 核对并导出 69 项反馈勾选。宿主按父子关系合并后选用音效总线 `393239870` 和界面总线 `1551306167`。

## 重建

使用 .NET 8 SDK 和 Zig 0.15.2，从仓库根目录运行下列命令。构建复用通用 Wwise 适配器，生成 `dist/packages/local.hogwarts-legacy.gpack`。

```powershell
pwsh -File tools/Build-GamePack.ps1 -DefinitionDirectory gamepacks/hogwarts-legacy
```
