# 从空白开始制作适配包

## 0. 环境与代码结构

按 [START-HERE](../START-HERE.md) 和[环境说明](build-environment.md)准备 PowerShell 7、.NET 8 SDK 和 Zig。`src/` 包含宿主和协议，`adapters/` 提供后端，`templates/` 提供定义模板，`gamepacks/` 保存目标配置。开发套件的编译器和诊断工具分别在 `toolchain/` 和 `prebuilt/devtools/`；使用 Git 源码时按环境说明准备。

复用后端前，核对目标的实例访问方式、对象结构和接口 ABI。即使引擎相同，这些结构也可能不同，需要扩展包内代码或开发新后端，修改摘要无法解决结构差异。

## 1. 收集证据

```powershell
prebuilt/devtools/ApexAudio.DevTools.exe inspect --exe "D:/Games/Example/Game.exe" --resources "D:/Games/Example" --output evidence/static.json
```

将路径换成目标 EXE 和资源目录。工具列出命名导出、候选资源和裸银行头；容器内的资源需另行读取，分组需从引擎或元数据中取得。

启动游戏并初始化音频后，可加 `--pid <实际进程ID>` 只读核对实际加载模块和内存中的导出；`--module <模块文件名>` 指定要核对的模块。不传 module 时只检查主模块和名称明显的音频模块，其余模块列出名称与路径。

线索不足时记录“尚未确认”，按 investigation.md 继续检查模块、资源和运行时接口。

## 2. 创建定义

```powershell
pwsh -File tools/New-Pack.ps1 -Id author.example -Name "目标游戏" -Backend Wwise -Directory gamepacks/example
```

按后端选择 Wwise / Fmod / Unreal / Custom。填写 manifest 的作者、来源、游戏文件名和 SHA256；game.json 字段见 backend-configs.md。构建前补齐 `__REQUIRED__` 和分组，统一构建器会拒绝未完成的定义。

分组必须有稳定 ID、真实父子关系和实际独立 PCM 能力。作者命名及初始选择放 `presets/initial.json`；用户副本与原稿独立。界面文件夹不改变实际混音路由。

脚手架还生成 `group-evidence.json` 和 `validation-report.json`。Wwise 可采集 Bus 的效果槽记录按[分组证据说明](group-evidence.md)填写；验证报告按实际完成的阶段填写。

## 3. 适配采集实现

按目标游戏版本调查接口，再核对字段和后端源码；可参考已发布适配包的结构。不兼容时按 new-module.md 扩展代码。构建工具不自动发现接口。

Custom 模板生成 `adapter/YourAdapter.csproj` 和接口骨架，Connect 会抛出未实现异常。补全 IGameAdapter；需要低延迟补充震动时实现 IRealtimeMixAdapter。

## 4. 构建

```powershell
pwsh -File tools/Build-GamePack.ps1 -DefinitionDirectory gamepacks/example
```

输出 `dist/packages/author.example.gpack`。Wwise / FMOD / Unreal 各自编译适配器与原生模块，自动写入模块摘要和完整文件清单；Custom 发布目录不包含 adapter 源码。

非 C# 适配器先整理运行目录，再执行 `tools/Pack.ps1 -Directory <完整目录> -Output <输出.gpack>`。manifest.json 放在包根目录，运行依赖使用包内路径。

## 5. 分阶段验证

先 `check` 校验完整性，再 `check --launch` 检查适配器协议；这两步不连接真实游戏。准备好后按 validation.md 执行实际 capture，或由用户在应用中导入和录制。首次运行需要确认包来源。

分别记录非零音频、音轨独立性、选择切换和断开重连的测试结果，未测试项写“未验证”。随包 README 列出构建方法和依赖，区分示例声音、整体混音、资源分组发现与实际独立采集。

分别录制背景音乐／对白和枪声、脚步、撞击等动作场景，记录候选组包含的声音。启用 X-Haptics 后，在相同场景比较全组输入与所选动作组输入，检查背景音干扰、动作反馈和延迟。仍混在同一组、无法排除的背景声写入包的限制说明。
