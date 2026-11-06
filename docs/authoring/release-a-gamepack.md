# 发布适配包

用户可只下载应用和自己需要的 .gpack；通用开发套件不混入具体游戏定义和个人数据。版本、初始预设及元数据更新仍须重新生成 manifest.files。

## 游戏包更新

保持包 ID，提升相应版本号，统一文件名为 ApexAudio-英文游戏名-版本.gpack，显示名称使用简洁游戏名。只修改名称或初始预设时保留已有采集二进制，核对摘要未变，不重复声称做过新游戏测试。

以当前选中的已保存预设为来源，核对 packId 与分组 ID。导出 groups、folders、排序及开关，去掉用户副本编号和 basePresetId / basePackVersion，作为包内稳定的初始预设。如果只按文件名或“1”这样的显示名搜索，可能拿错预设；不要打包整个 data/。

打包后做完整性和预设一致性检查。原始采集报告应保留实际测试版本与范围，不能把换预设写成重新验证所有场景。运行时不需要的 PDB、导入库和本机路径不放入用户包。

## 分发与下载目录

发布前准备完整 `.gpack`、SHA-256、支持的游戏构建及[实际验证结果](verify-a-gamepack.md)。保留该版本的游戏定义和专用采集器源码，并记录构建命令和依赖来源，方便后续更新。发布后核对公开附件和摘要，再更新[适配包下载目录](../user/gamepack-downloads.md)。

现有公开包在 [ApexAudio 2026.10.18](https://github.com/gray-raven-star/ApexAudio/releases/tag/bundle-2026.10.18) 的附件中。早期的 `bundle-*` 工作流从发布标签的快照上传附件，龙之信条 2 则由[专用工作流](https://github.com/gray-raven-star/ApexAudio/blob/main/.github/workflows/publish-gamepack.yml)校验仓库中的输入后追加到该 Release。应用和 DevKit 的构建见[发行版构建](../development/release-builds.md)。
