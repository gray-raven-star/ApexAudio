# 从空白开始制作适配包

## 0. 环境与代码结构

按 START-HERE 检查 PowerShell 7、.NET 8 SDK 和随附 Zig。`src/` 是宿主和协议；`adapters/` 是可复用后端；`templates/` 是不完整定义；`gamepacks/` 留给你创建目标配置；`toolchain/` 是编译器；`prebuilt/devtools/` 是已编译诊断工具。

现有后端有适用范围。相同引擎不代表相同实例访问方式、对象结构或接口 ABI；不匹配时修改包内后端或实现新的后端，不能仅改摘要蒙混过关。

## 1. 收集证据

```powershell
prebuilt/devtools/ApexAudio.DevTools.exe inspect --exe "D:/Games/Example/Game.exe" --resources "D:/Games/Example" --output evidence/static.json
```

路径是示例，先确认目标 EXE。工具列出命名导出、有限范围内的资源候选和可直接读取的银行头。它不解包任意容器，不通过最终混音猜测分组。

启动游戏并初始化音频后，可加 `--pid <实际进程ID>` 只读核对实际加载模块和内存中的导出；`--module <模块文件名>` 指定要核对的模块。不传 module 时只检查主模块和名称明显的音频模块，其余模块列出名称与路径。

没有证据时保持“尚未确认”，按 investigation.md 继续，不要跳到“自研音频、无法适配”的结论。

## 2. 创建定义

```powershell
pwsh -File tools/New-Pack.ps1 -Id author.example -Name "目标游戏" -Backend Wwise -Directory gamepacks/example
```

按实际后端选择 Wwise / Fmod / Unreal / Custom。填完 manifest 的作者、来源、游戏文件名、SHA256；game.json 各字段参阅 backend-configs.md。`__REQUIRED__` 和空分组是明确的未完成标记，统一构建器拒绝它们。

分组必须有稳定 ID、真实父子关系和实际独立 PCM 能力。作者命名及初始选择放 `presets/initial.json`；用户副本与原稿独立。界面文件夹不改变实际混音路由。

## 3. 适配采集实现

复用时核对字段和后端源码要求；不兼容时按 new-module.md 开发。模板不包含游戏定位答案，构建工具也不会自动发现接口。

Custom 模板会生成 `adapter/YourAdapter.csproj` 和接口骨架。Connect 明确抛出未实现；它不会产生假音频。完善 IGameAdapter；需要低延迟补充震动时另实现 IRealtimeMixAdapter。

## 4. 构建

```powershell
pwsh -File tools/Build-GamePack.ps1 -DefinitionDirectory gamepacks/example
```

输出 `dist/packages/author.example.gpack`。Wwise / FMOD / Unreal 各自编译适配器与原生模块，自动写入模块摘要和完整文件清单；Custom 发布目录不包含 adapter 源码。

自定义非 C# 技术可直接整理完整运行目录，再执行 `tools/Pack.ps1 -Directory <完整目录> -Output <输出.gpack>`。包根直接放 manifest.json；不要多套一层目录，不要依赖开发机上的固定安装路径。

## 5. 分阶段验证

先 `check` 校验完整性，再 `check --launch` 检查适配器协议；这两步不连接真实游戏。准备好后按 validation.md 执行实际 capture，或由用户在应用中导入和录制。首次运行需要确认包来源。

确认非零数据、独立性、选择变化、断开与重连。开发者没有实际测试的项目写“未验证”。完整记录构建和依赖，随包发布 README。示例声音、总混音、资源分组发现与真实独立采集是不同的验收结果。
