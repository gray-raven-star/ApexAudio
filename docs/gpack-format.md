# .gpack 格式（V1）

`.gpack` 是普通 ZIP，根目录直接放 manifest.json，不能多套一层文件夹。目录内可包含 EXE、DLL、私有运行环境、资源、作者预设及说明。单包最大 1 GiB、10000 条归档记录。

```text
manifest.json
adapter.exe
adapter-private-runtime/  # 可选
modules/                 # 可选
presets/author.json
resources/               # 可选
```

## manifest.json

```json
{
  "schemaVersion": 1,
  "id": "author.game-name",
  "name": "游戏名称",
  "version": "1.0.0",
  "author": "发布者",
  "source": "发布页面或来源说明",
  "description": "此包实际支持的版本和能力",
  "protocolVersion": 1,
  "entryPoint": "adapter.exe",
  "demo": false,
  "games": [{"executable": "Game.exe", "sha256": "程序SHA256"}],
  "presets": ["presets/author.json"],
  "files": {"adapter.exe": "文件SHA256", "presets/author.json": "文件SHA256"}
}
```

| 字段 | 规则 |
|---|---|
| schemaVersion / protocolVersion | V1 都为 1，不兼容版本在启动前拒绝 |
| id | 3～80 字符，首字符小写字母／数字，其余小写字母、数字、点、短横线；发布后固定 |
| version | .NET Version 格式，如 1.0.0 |
| name | 非空显示名称 |
| author / source / description | 作者、来源及支持范围，导入和首次执行时展示；普通字符串，不执行内容 |
| entryPoint | 包内相对 EXE 路径，不接受外部路径或命令行 |
| demo | 仅演示使用 true，允许不匹配游戏进程，不自动连接 |
| games | 真实包至少一条；executable 为文件名，sha256 可省略，省略表示由适配器自行核对版本 |
| presets | 作者预设相对路径，可为空；无作者预设时建立全关闭的空用户配置 |
| files | 所有文件（除 manifest.json）相对路径与 SHA256；缺失、变更、未登记文件均拒绝 |

JSON Schema 位于 `schemas/manifest.schema.json`。运行时还做文件完整性、路径、格式及预设语义校验。

路径统一用 `/`，禁止绝对路径、盘符、反斜线和越过根目录的 `..`。完整性摘要不等于作者身份签名。宿主展示来源，并按包清单摘要保存首次运行许可。

## 更新与卸载

相同 id 的新包替换程序及作者预设，用户预设、录音保留。更新已连接包前先停止当前会话。卸载删除包目录，不删除用户数据。V1 不自动下载更新、不处理共享前置依赖。

## 识别

以进程主程序名称筛选，再按可选 SHA256 匹配。文件名比较不区分大小写，摘要比较不区分大小写。进程不可访问且包要求摘要时，不猜测匹配。多个包匹配时用户选择。

作者应避免把机器上的固定安装路径写进包。未知程序版本不能套用旧内存地址；版本拒绝和访问失败须由 Adapter 返回清楚原因。

## 引擎专用配置

宿主不解释 game.json 的引擎字段。Wwise、FMOD、Unreal 等适配器各自解释自己的配置，并输出同一协议。

Unreal AudioMixer 示例和字段格式见 `adapters/Unreal/README.md`。游戏差异包含设备地址链、名称池布局、监听方法地址、稳定分组名称和初始预设；只支持包声明并核对的游戏文件，不代表所有虚幻版本已覆盖。

普通包卡片只显示名称、版本和操作按钮。author、source、description 仍保存在清单中用于包身份与首次运行确认，不参与音频逻辑。
