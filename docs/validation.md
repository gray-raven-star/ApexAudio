# 验证工具和结果范围

以下命令用于分阶段验证适配包。开发套件的发布构建记录保存在 release-manifest.json。

## 工具位置

开发套件的工具位于 `prebuilt/devtools/ApexAudio.DevTools.exe`；Git 源码需按[环境说明](build-environment.md)单独构建 tools/DevTools。报告支持 `--output <文件.json>`，分享前检查其中的本机游戏路径。

### 只读调查

```powershell
prebuilt/devtools/ApexAudio.DevTools.exe inspect --exe "D:/Games/Example/Game.exe" --resources "D:/Games/Example" --output evidence/static.json
prebuilt/devtools/ApexAudio.DevTools.exe inspect --exe "D:/Games/Example/Game.exe" --pid 1234 --module "Audio.dll" --output evidence/runtime.json
```

不写进程、不注入、不加载游戏代码；运行时检查需要游戏已启动且当前权限允许。工具只检查选定模块的导出，命名为空不证明引擎不存在。

### 完整性与协议

```powershell
prebuilt/devtools/ApexAudio.DevTools.exe check --pack dist/packages/author.example.gpack --output evidence/package.json
prebuilt/devtools/ApexAudio.DevTools.exe check --pack dist/packages/author.example.gpack --launch --output evidence/protocol.json
```

第一条只校验并解压，不执行适配器；第二条执行包里的适配器并读取信息及分组，不连接游戏。只能对来源可信的包使用执行选项。临时目录在报告 scratch 字段中，检查后由评审者整理。

### 开发示例音轨

先构建 `tools/Build.ps1`，生成的开发示例在 artifacts/packages/demo.gpack。它不随用户版自动安装。

```powershell
prebuilt/devtools/ApexAudio.DevTools.exe check --pack artifacts/packages/demo.gpack --launch --demo-audio --seconds 3 --output evidence/demo.json
```

只证明协议与生成的不同波形可传输，不证明任何真实游戏已适配。

### 实际游戏采集

```powershell
prebuilt/devtools/ApexAudio.DevTools.exe capture --pack dist/packages/author.example.gpack --pid 1234 --allow-game-connection --seconds 5 --output evidence/capture.json
```

这个命令执行包的正常 Connect，可能加载采集 DLL 或挂接 DSP，存在游戏崩溃风险，应在已授权的测试目标上执行；与 inspect 不同。它不启动 X-Haptics，也不发送物理震动。也可以由用户在应用中导入、连接和录音。

报告包含实际分组、收到包数、帧数、各组峰值、首个有效数据块摘要、状态和错误。支持公共 realtimeMix 的包还报告映射 active 与发布块数；没有选中反馈组时混音静音是正常的，不能由此否定独立录音。

不同组摘要相同可能来自父子内容或真实重复，也可能是错误复制，必须结合路由与试听。非零 PCM 不证明命名和语义正确。

### 原生模块自己的离线测试

```powershell
prebuilt/devtools/ApexAudio.DevTools.exe native-check --module "your-module.dll" --entry "YourSelfTest"
```

在诊断工具自身进程加载并执行指定的单参数 uint32 自测导出，不连接游戏。仅用于你自己或来源可信的模块。不是每个后端都有自测导出；没有时不能编造通过记录。

## 发布报告模板

复制 `templates/validation-report.json`，填写环境、目标版本、证据文件、已完成阶段和未验证项目。至少区分：

1. 完整包结构和摘要；
2. 源码构建与协议；
3. 对象解析、监听注册和回调；
4. 真实非零 PCM；
5. 独立性、语义与父子关系；
6. 选择切换、停止、退出和重连；
7. 反馈源、延迟与可选物理手柄。

每一步可写通过、失败或未测试，附证据。不能用上一阶段的通过填下一阶段。未运行游戏时不得写“游戏兼容已验证”。
