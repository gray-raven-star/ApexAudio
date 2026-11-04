<#
.SYNOPSIS
从干净源码构建一次 V2 Alpha 交付物。

.DESCRIPTION
复现 outputs/alpha 的 Alpha 包结构，对应原本地打包步骤，但不依赖任何本机预构建
产物：
1. 通过 tools/TestV2.ps1 -Compiler 重建 FMOD 0.3.0 后端、把 tests/fixtures/v2 的
   Owner 包重写成 0.3.0 并运行 V2 协议测试；
2. 用 Release.ps1 相同的参数以 framework-dependent 方式发布桌面程序；
3. 组装 ApexAudio-<Version>/ 目录（程序、docs、schemas、ui、native、assets、
   LICENSE 等、README、Start-Alpha.cmd，以及 packages/ 下的后端与 STS2 配置）；
4. 写入 alpha-manifest.json（sourceCommit、pr、全部文件 SHA256）；
5. 生成 ApexAudio-Windows-x64-<Version>.zip 与 SHA256SUMS-<Version>.txt；
6. 解压回读校验全部摘要，并用 PackV2 validate 复核配置包与后端的依赖关系。

.PARAMETER Version
Alpha 版本，用于压缩包内层目录、zip 名和校验和文件名。

.PARAMETER Compiler
zig 可执行文件（或 PATH 中的命令名），用于编译 FMOD 采集 DLL。

.PARAMETER OutputDirectory
输出目录；默认 ../outputs/alpha。目录中的同名 Alpha 压缩包不会被覆盖。

.PARAMETER SourceCommit
写入 alpha-manifest.json 的提交；默认取 git HEAD，并与实际 HEAD 比较。

.PARAMETER PullRequestUrl
写入 alpha-manifest.json 的评审 PR 链接。
#>
param(
    [ValidatePattern('^[a-zA-Z0-9.-]+$')][string]$Version = '0.3.0-alpha.1',
    [string]$Compiler = 'zig',
    [string]$OutputDirectory = '',
    [string]$SourceCommit = '',
    [string]$PullRequestUrl = 'https://github.com/gray-raven-star/ApexAudio/pull/4'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Environment.ps1')
$null = Initialize-ApexEnvironment $projectRoot
if (-not ('System.IO.Compression.ZipFile' -as [type])) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}

# Directory.Build.props 固定主程序版本 0.3.0；TestV2.ps1 同步把后端与配置包重写成 0.3.0。
$backendName = 'ApexAudio-Fmod-0.3.0.gbackend'
$packName = 'ApexAudio-SlayTheSpire2-V2-0.3.0.gpack'

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot '../outputs/alpha' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$archive = Join-Path $output "ApexAudio-Windows-x64-$Version.zip"
if (Test-Path -LiteralPath $archive) { throw "Alpha archive already exists: $archive" }

$head = (& git -C $projectRoot rev-parse HEAD).Trim()
if (-not $SourceCommit) { $SourceCommit = $head }
if ($SourceCommit -ne $head) { throw "SourceCommit $SourceCommit does not match checkout $head." }
if ((& git -C $projectRoot status --porcelain)) { throw 'Package only a clean source checkout.' }

Write-Host 'Build the V2 backend, repackage the V2 fixtures and run the protocol tests.'
$artifacts = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$previous = @(Get-ChildItem -LiteralPath $artifacts -Directory -Filter 'source-v2-*' -ErrorAction SilentlyContinue |
    ForEach-Object FullName)
& (Join-Path $PSScriptRoot 'TestV2.ps1') -Compiler $Compiler
if ($LASTEXITCODE -ne 0) { throw 'V2 source build and protocol tests failed.' }
$sources = @(Get-ChildItem -LiteralPath $artifacts -Directory -Filter 'source-v2-*' -ErrorAction SilentlyContinue |
    Where-Object { $previous -notcontains $_.FullName } |
    ForEach-Object FullName)
if ($sources.Count -ne 1) { throw 'Could not identify the V2 source build output.' }
$source = $sources[0]
$backend = Join-Path $source $backendName
$pack = Join-Path $source $packName
foreach ($input in @($backend, $pack)) {
    if (-not (Test-Path -LiteralPath $input)) { throw "Missing V2 build output: $input" }
}

Write-Host 'Publish the desktop application framework-dependently.'
$stage = Join-Path $artifacts ('alpha-stage-' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $stage "ApexAudio-$Version"
New-Item -ItemType Directory -Path $app, $output -Force | Out-Null
$desktop = Join-Path $projectRoot 'src/ApexAudio.Desktop/ApexAudio.Desktop.csproj'
& dotnet restore $desktop --configfile (Join-Path $projectRoot 'NuGet.Config') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Alpha restore failed.' }
& dotnet publish $desktop -c Release --no-restore --no-self-contained -p:DebugType=None -p:DebugSymbols=false "-p:PathMap=$($projectRoot)=/_/ApexAudio" -o $app --nologo
if ($LASTEXITCODE -ne 0) { throw 'Alpha application publish failed.' }

function Copy-Tree([string]$Source, [string]$Destination) {
    $base = (Resolve-Path -LiteralPath $Source).Path
    foreach ($file in Get-ChildItem -LiteralPath $base -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($base, $file.FullName)
        if ($relative -match '(^|[\\/])(bin|obj|data|artifacts|dist|\.git|\.cache)([\\/]|$)') { continue }
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Do not package reparse-point files.' }
        $target = Join-Path $Destination $relative
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
        [IO.File]::Copy($file.FullName, $target, $true)
    }
}

Write-Host 'Stage the Alpha layout.'
Copy-Tree (Join-Path $projectRoot 'docs') (Join-Path $app 'docs')
Copy-Tree (Join-Path $projectRoot 'schemas') (Join-Path $app 'schemas')
foreach ($name in @('LICENSE', 'THIRD-PARTY.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $app
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'vendor/webview2/LICENSE.txt') -Destination (Join-Path $app 'WebView2-LICENSE.txt')
New-Item -ItemType Directory -Path (Join-Path $app 'packages') | Out-Null
foreach ($input in @($backend, $pack)) { Copy-Item -LiteralPath $input -Destination (Join-Path $app 'packages') }

@'
@echo off
start "" "%~dp0ApexAudio.exe" --data "%~dp0data"
'@ | Set-Content -LiteralPath (Join-Path $app 'Start-Alpha.cmd') -Encoding ascii

$readme = @"
# ApexAudio $Version / Windows x64

这是由 CI 从干净源码构建的 Alpha 工程包，不是正式版。主程序与适配包/后端版本分开管理。

## 运行

1. 完整解压到可写目录，勿在压缩包内直接运行。
2. 需要 .NET 8 Desktop Runtime (x64) 与 Microsoft Edge WebView2 Runtime。本包不是自包含运行时发行版，也不包含 X-Haptics 驱动。
3. 双击 Start-Alpha.cmd，以本包目录下的 data 保存 Alpha 数据，避免与其他版本混用。也可手动运行 ApexAudio.exe --data <独立目录>。
4. 在“游戏适配包”页导入 packages/$backendName，再导入 packages/$packName，显式“设为当前默认”。
5. 启动匹配构建的 STS2，保持前台和正常声音，再连接。没有震动设备也可以采集、录音和逐组试听；物理反馈留给有设备的测试者。
6. 录音/预设保存在独立 data 目录。升级前保留旧包、后端和 data；不要覆盖正在被游戏加载的 DLL。提示 DLL 冲突时重启游戏。

## 已验证范围

以下实机证据来自本地 V2 验证轮次，CI 只从同一源码重新构建并核对包内摘要与依赖关系，不重新做实机采集。STS2 单构建的 EXE/实际加载 FMOD 模块摘要精确匹配，主菜单音乐及父混音有非零 PCM；停止12秒后可恢复。软件反馈选择/静音、PCM停止后混音保持、真实录音与移包后读取已观察。已有V1/Owner0.2协议和桌面模拟流程有回归证据。

## Alpha 反馈与限制

- 请继续核对菜单/战斗/SFX/环境等分组语义、长时采集和真实设备效果。主菜单音乐通过不等于全部声音分组正确。
- 后台可能没有声音；先确认游戏前台有声。严格构建不匹配应拒绝，不要修改摘要或猜地址绕过检查。
- 本包仅附STS2配置；KCD2本轮未实测。物理震动未验，软件混音通过不代表马达效果通过。
- 已知采集DLL族检查不保证识别任意改名模块；不提供跨进程同时采集的原子所有权保证。
- Lua、Wwise/Unreal共享后端、在线适配包目录不在本Alpha范围。程序不提供兼容认证或评分。
- 同ID/版本内容不可变。本地试验后端0.3.0如与已有同版本内容不同，请使用独立Alpha数据目录；切勿覆盖在用文件。

反馈请注明主程序/包/后端版本、游戏版本、操作步骤、实际/预期结果和错误文字；提供日志/录音前检查隐私。文档见 docs/README.md 与 docs/user/settings-recording.md。

代码评审：$PullRequestUrl
许可证见 LICENSE、THIRD-PARTY.md 和 WebView2-LICENSE.txt。
"@
[IO.File]::WriteAllText((Join-Path $app 'README.md'), $readme, [Text.UTF8Encoding]::new($false))

foreach ($required in @('ApexAudio.exe', 'ApexAudio.dll', 'ui/index.html', 'assets/apex.ico', 'native/ApexGroupsBridge-v5.dll', 'docs/README.md', 'schemas/manifest.schema.json', ('packages/' + $backendName), ('packages/' + $packName))) {
    if (-not (Test-Path -LiteralPath (Join-Path $app $required))) { throw "Alpha content is incomplete: $required" }
}

Write-Host 'Record the Alpha manifest.'
$files = [ordered]@{}
foreach ($file in Get-ChildItem -LiteralPath $app -File -Recurse | Sort-Object FullName) {
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'No reparse points in Alpha.' }
    $relative = [IO.Path]::GetRelativePath($app, $file.FullName).Replace('\', '/')
    if ($relative -match '(^|/)(data|\.cache|artifacts|bin|obj|\.git)/' -or $file.Extension -eq '.pdb') { throw "Unwanted Alpha file: $relative" }
    $files[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
}
$manifest = [ordered]@{
    version = $Version
    sourceCommit = $SourceCommit
    packageKind = 'framework-dependent-windows-x64'
    pr = $PullRequestUrl
    inputs = 'validated-ci-build'
    files = $files
}
[IO.File]::WriteAllText((Join-Path $app 'alpha-manifest.json'), ($manifest | ConvertTo-Json -Depth 7), [Text.UTF8Encoding]::new($false))

Write-Host 'Create the Alpha archive and checksum list.'
[IO.Compression.ZipFile]::CreateFromDirectory($app, $archive, [IO.Compression.CompressionLevel]::Optimal, $true)
foreach ($input in @($backend, $pack)) {
    $target = Join-Path $output ([IO.Path]::GetFileName($input))
    if (Test-Path -LiteralPath $target) {
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $input -Algorithm SHA256).Hash) { throw 'Conflicting output asset.' }
    }
    else { Copy-Item -LiteralPath $input -Destination $target }
}
$checksums = Join-Path $output "SHA256SUMS-$Version.txt"
@($archive, (Join-Path $output $backendName), (Join-Path $output $packName)) | ForEach-Object {
    "$((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash)  $([IO.Path]::GetFileName($_))"
} | Set-Content -LiteralPath $checksums -Encoding ascii

Write-Host 'Verify the archive, the checksum list and the packaged dependencies.'
$verify = Join-Path $stage 'verify'
[IO.Compression.ZipFile]::ExtractToDirectory($archive, $verify)
$verifiedApp = Join-Path $verify "ApexAudio-$Version"
$extracted = Get-Content -LiteralPath (Join-Path $verifiedApp 'alpha-manifest.json') -Raw | ConvertFrom-Json
if ($extracted.sourceCommit -ne $SourceCommit) { throw 'Alpha manifest source commit mismatch.' }
if ($extracted.version -ne $Version) { throw 'Alpha manifest version mismatch.' }
if ($extracted.packageKind -ne 'framework-dependent-windows-x64') { throw 'Alpha manifest package kind mismatch.' }
$listed = @{}
foreach ($entry in $extracted.files.PSObject.Properties) {
    $path = Join-Path $verifiedApp $entry.Name
    if (-not (Test-Path -LiteralPath $path)) { throw "Alpha manifest lists a missing file: $($entry.Name)" }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Value) { throw "Extracted Alpha hash mismatch: $($entry.Name)" }
    $listed[$entry.Name] = $true
}
foreach ($file in Get-ChildItem -LiteralPath $verifiedApp -File -Recurse) {
    $relative = [IO.Path]::GetRelativePath($verifiedApp, $file.FullName).Replace('\', '/')
    if ($relative -eq 'alpha-manifest.json') { continue }
    if (-not $listed.ContainsKey($relative)) { throw "Unlisted Alpha file: $relative" }
}
foreach ($line in Get-Content -LiteralPath $checksums) {
    if ($line -notmatch '^([A-F0-9]{64})  (.+)$') { throw "Invalid checksum record: $line" }
    $expected = $Matches[1]
    $name = $Matches[2]
    $path = Join-Path $output $name
    if (-not (Test-Path -LiteralPath $path)) { throw "Checksum record references a missing asset: $name" }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected) { throw "Checksum mismatch: $name" }
}
$backendRoot = Join-Path $stage 'backend-check'
[IO.Compression.ZipFile]::ExtractToDirectory($backend, $backendRoot)
$definitionRoot = Join-Path $stage 'pack-check'
[IO.Compression.ZipFile]::ExtractToDirectory($pack, $definitionRoot)
$descriptor = Get-Content -LiteralPath (Join-Path $backendRoot 'backend.json') -Raw | ConvertFrom-Json
if ($descriptor.version -ne '0.3.0') { throw 'Backend descriptor version mismatch.' }
$digest = (Get-FileHash -LiteralPath (Join-Path $backendRoot 'backend.json') -Algorithm SHA256).Hash
$definition = Get-Content -LiteralPath (Join-Path $definitionRoot 'manifest.json') -Raw | ConvertFrom-Json
if ($definition.version -ne '0.3.0') { throw 'Configuration manifest version mismatch.' }
$profiles = @($definition.profiles)
if ($profiles.Count -eq 0) { throw 'Configuration manifest has no profiles.' }
foreach ($profile in $profiles) {
    if ($profile.backend.version -ne '0.3.0') { throw 'Configuration profile backend version mismatch.' }
    if ($profile.backend.descriptorSha256 -ne $digest) { throw 'Configuration profile does not reference the built backend digest.' }
}
$withModules = @($definition.games | Where-Object { $_.PSObject.Properties.Name -contains 'modules' -and @($_.modules).Count -gt 0 })
if ($withModules.Count -eq 0) { throw 'Configuration games do not record modules.' }
$validation = & dotnet (Join-Path $projectRoot 'tools/PackV2/bin/Release/net8.0-windows/ApexAudio.PackV2.dll') validate $definitionRoot --backend $backendRoot --json 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $validation -notmatch '"valid":\s*true') { throw "Packaged Alpha dependency validation failed: $validation" }

$result = [ordered]@{
    version = $Version
    sourceCommit = $SourceCommit
    archive = $archive
    checksums = $checksums
    archiveSha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    backendSha256 = (Get-FileHash -LiteralPath (Join-Path $output $backendName) -Algorithm SHA256).Hash
    packSha256 = (Get-FileHash -LiteralPath (Join-Path $output $packName) -Algorithm SHA256).Hash
    filesVerified = $files.Count
    dependenciesValidated = $true
}
$result | ConvertTo-Json
Write-Host "Alpha ready: $output"
