# 读取容器中的音频元数据

`tools/Metadata/` 提供两种有限格式读取器。先确认容器版本和所需资源，再执行读取；若使用 Wwise，可把输出的裸银行继续交给 DevTools `bank-index` 查看 chunk 与对象目录。

## 格式范围与输入

| 工具 | 支持范围 | 需要提供 |
|---|---|---|
| `read_re_pak.py` | RE Pak v4.0、48 字节索引项、flags 0 或 8；未压缩、DEFLATE、可选 Zstd | 1～256 条精确资源路径；flags 8 另需格式匹配的公开 `PakCipher.cs` 参考 |
| `read_ue_pak.py` | UE Pak v11、标准 221 字节 footer、完整目录索引；未压缩、Zlib、可选 Oodle 解码器 | 资源路径后缀；使用 Oodle 时提供解码库及 SHA256 |

每个资源的压缩及解压大小上限为 16 MiB。UE 的加密索引、加密资源、IoStore（`.utoc` / `.ucas`）和未知格式不在当前读取器范围内。RE 的资源级加密也会被拒绝。

## 读取指定资源

以下路径均为示例，需要替换为实际容器与资源名。默认依赖 Python 标准库：

```powershell
uv run tools/Metadata/read_re_pak.py "D:/Games/Example/re_chunk.pak" --resource "实际资源路径/Init.bnk" --layer base --output evidence/re-base
uv run tools/Metadata/read_ue_pak.py "D:/Games/Example/content.pak" --target "Init.bnk" --layer base --output evidence/ue-base
```

RE 读取器按输入路径的小写／大写哈希匹配索引，不能枚举未知资源名。flags 8 要求 `--cipher-reference <PakCipher.cs路径>`，从所给参考中读取 `lpModulus` 的 128 字节公开模数；报告会记录参考文件摘要。此参考文件需自行取得，与当前格式核对。

若指定的 RE 资源采用 Zstd：

```powershell
uv run --with zstandard==0.25.0 tools/Metadata/read_re_pak.py "D:/Games/Example/re_chunk.pak" --resource "实际资源路径/Init.bnk" --output evidence/re-zstd
```

UE 读取器的 `--target` 按路径后缀匹配，最多输出 256 个资源。Oodle 路径需同时提供 `--ooz <解码库路径>` 与 `--decoder-sha256 <摘要>`；脚本会在自身进程加载该库并调用 `OozDecompress` 包装接口。普通 Oodle DLL 不一定提供该接口，应先核对库的来源和调用签名。

## 阅读报告与处理补丁

每次读取在输出目录写 `metadata-report.json`。核对：

- `outcome` 和 `error`：读取是否完成，失败发生在哪项格式检查。
- `missingRequests`：请求但未匹配的路径或后缀。
- `resources`：实际匹配路径、源层、输出文件、大小和摘要。
- `indexSha256` 或 `indexSha1`：本次读取的索引身份。

输出文件名带路径哈希，`outputPath` 才是本地实际文件位置。同一输出名已有不同内容时工具拒绝覆盖，换一个输出目录保留两次结果。

多个基础包／补丁报告按实际加载优先级传入，后者覆盖前者：

```powershell
uv run tools/Metadata/merge_layers.py --report evidence/re-base/metadata-report.json --report evidence/re-patch/metadata-report.json --output evidence/merged.json
```

`overrides` 记录替换关系。优先级由开发者依据游戏加载规则提供，脚本不自行推断。

## Wwise 名称候选

已有 Bus 编号和有限的名称列表时，可查找匹配候选：

```powershell
uv run tools/Metadata/name_candidates.py --definition gamepacks/my-game/game.json --candidates evidence/names.txt --output evidence/name-candidates.json
```

工具保留多个候选和来源文件摘要，结果的 `auditionConfirmed` 初始为 false。随后在游戏录音中确认各组实际内容，再填写显示名称和证据记录。
