param(
    [ValidatePattern('^[a-zA-Z0-9.-]+$')][string]$Version='0.1.1',
    [Parameter(Mandatory)][string]$CompilerDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath (Split-Path $PSScriptRoot -Parent)).Path
. (Join-Path $PSScriptRoot 'Environment.ps1')
$null = Initialize-ApexEnvironment $root
if (-not $OutputDirectory) { $OutputDirectory=Join-Path $root '../outputs/releases' }
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$buildRoot=Join-Path $root ('artifacts/release-'+[Guid]::NewGuid().ToString('N'))
$kit=Join-Path $buildRoot 'ApexAudio-DevKit'
$app=Join-Path $buildRoot 'ApexAudio-Windows-x64'
New-Item -ItemType Directory -Path $kit,$app -Force | Out-Null
function Copy-Tree([string]$Source,[string]$Destination) {
    $base=(Resolve-Path -LiteralPath $Source).Path
    foreach($file in Get-ChildItem -LiteralPath $base -File -Recurse) {
        $relative=[IO.Path]::GetRelativePath($base,$file.FullName)
        if($relative -match '(^|[\\/])(bin|obj|data|artifacts|dist|\.git)([\\/]|$)') { continue }
        if($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Do not release reparse-point files.' }
        $target=Join-Path $Destination $relative
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
        [IO.File]::Copy($file.FullName,$target,$true)
    }
}
Write-Host 'Stage the application and adapter development kit.'
foreach($directory in @('src','adapters','providers','tests','schemas','ui','vendor','templates','release-docs')) { Copy-Tree (Join-Path $root $directory) (Join-Path $kit $directory) }
# Source and runtime need one bridge revision, no old copies or debug artifacts.
$native=Join-Path $kit 'providers/XHaptics/native'
Get-ChildItem -LiteralPath $native -File | Where-Object Name -notin @('ApexGroupsBridge.c','ApexGroupsBridge-v5.dll','xh-support.json','xh-engines.json') | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
foreach($name in @('ApexAudio.sln','Directory.Build.props','NuGet.Config','.gitignore','START-HERE.md','AI-INSTRUCTIONS.md','LICENSE')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $kit }
foreach($name in @('Build.ps1','Pack.ps1','MakeDemoPack.ps1','BuildNative.ps1','FindCompiler.ps1','Setup.ps1','Environment.ps1','New-Pack.ps1','Build-GamePack.ps1','Release.ps1','Disassemble.py')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $kit 'tools') | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $kit 'tools')
}
Copy-Tree (Join-Path $root 'tools/DevTools') (Join-Path $kit 'tools/DevTools')
Copy-Tree (Join-Path $root 'tools/Metadata') (Join-Path $kit 'tools/Metadata')
Copy-Tree (Join-Path $root 'packs/demo') (Join-Path $kit 'packs/demo')
Copy-Tree (Join-Path $root 'docs') (Join-Path $kit 'docs')
foreach($name in @('README.md','THIRD-PARTY.md')) { Copy-Item -LiteralPath (Join-Path $root ('release-docs/'+$name)) -Destination $kit }
New-Item -ItemType Directory -Force -Path (Join-Path $kit 'gamepacks'),(Join-Path $kit 'assets') | Out-Null
'Create and maintain game definitions in this directory. Use tools/New-Pack.ps1 to start a package.' | Set-Content -LiteralPath (Join-Path $kit 'gamepacks/README.md') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $root 'release-docs/assets/apex.ico') -Destination (Join-Path $kit 'assets/apex.ico')
Copy-Item -LiteralPath (Join-Path $root 'release-docs/assets/apex.svg') -Destination (Join-Path $kit 'assets/apex.svg')
Write-Host 'Copy portable Zig with its complete library and license tree.'
$compiler=(Resolve-Path -LiteralPath $CompilerDirectory).Path
Copy-Tree $compiler (Join-Path $kit 'toolchain/zig')
Write-Host 'Compile source snapshot and diagnostic tool; do not execute acceptance tests or connect games.'
$desktop=Join-Path $kit 'src/ApexAudio.Desktop/ApexAudio.Desktop.csproj'
$diagnostics=Join-Path $kit 'tools/DevTools/ApexAudio.DevTools.csproj'
$pathMap=$kit+'=/_/ApexAudio'
foreach($project in @($desktop,$diagnostics)) {
    & dotnet restore $project --configfile (Join-Path $kit 'NuGet.Config') --nologo
    if($LASTEXITCODE -ne 0) { throw 'Release restore failed.' }
}
& dotnet publish $desktop -c Release --no-restore --no-self-contained -p:DebugType=None -p:DebugSymbols=false "-p:PathMap=$pathMap" -o $app --nologo
if($LASTEXITCODE -ne 0) { throw 'Application compile failed.' }
& dotnet publish $diagnostics -c Release --no-restore --no-self-contained -p:DebugType=None -p:DebugSymbols=false "-p:PathMap=$pathMap" -o (Join-Path $kit 'prebuilt/devtools') --nologo
if($LASTEXITCODE -ne 0) { throw 'Developer tool compile failed.' }
# Compile the included adapter backends.
foreach($backend in @('Wwise','Fmod','Unreal','Mock')) {
    & dotnet build (Join-Path $kit "adapters/$backend/ApexAudio.$backend.csproj") -c Release --configfile (Join-Path $kit 'NuGet.Config') --nologo
    if($LASTEXITCODE -ne 0) { throw "Adapter compile failed: $backend" }
}
Copy-Tree (Join-Path $kit 'docs') (Join-Path $app 'docs')
foreach($name in @('README.md','THIRD-PARTY.md','START-HERE.md','AI-INSTRUCTIONS.md','LICENSE')) { Copy-Item -LiteralPath (Join-Path $kit $name) -Destination $app }
Copy-Item -LiteralPath (Join-Path $kit 'vendor/webview2/LICENSE.txt') -Destination (Join-Path $app 'WebView2-LICENSE.txt')
# Clean compile intermediates from the source distribution, using only exact files.
foreach($base in @($kit,$app)) {
    $generated=@(Get-ChildItem -LiteralPath $base -File -Recurse | Where-Object {
        $relative=[IO.Path]::GetRelativePath($base,$_.FullName)
        $relative -match '(^|[\\/])(bin|obj)([\\/]|$)' -or $_.Extension -eq '.pdb'
    })
    foreach($file in $generated) { Remove-Item -LiteralPath $file.FullName }
    $empty=@(Get-ChildItem -LiteralPath $base -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending)
    foreach($directory in $empty) { if(-not (Get-ChildItem -LiteralPath $directory.FullName -Force | Select-Object -First 1)) { Remove-Item -LiteralPath $directory.FullName } }
}
foreach($base in @($kit,$app)) {
    $files=[ordered]@{}
    foreach($file in Get-ChildItem -LiteralPath $base -File -Recurse | Sort-Object FullName) { $files[[IO.Path]::GetRelativePath($base,$file.FullName).Replace('\','/')]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
    @{version=$Version;sourceCompiled=$true;files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $base 'release-manifest.json') -Encoding utf8
}
Write-Host 'Create application and developer archives.'
foreach($spec in @(@{root=$app;name="ApexAudio-Windows-x64-$Version.zip"},@{root=$kit;name="ApexAudio-DevKit-$Version.zip"})) {
    $archive=Join-Path $output $spec.name
    if(Test-Path -LiteralPath $archive) { throw "Release already exists: $archive" }
    [IO.Compression.ZipFile]::CreateFromDirectory($spec.root,$archive,[IO.Compression.CompressionLevel]::Optimal,$true)
}
Get-ChildItem -LiteralPath $output -File -Filter "*-$Version.zip" | ForEach-Object { "$( (Get-FileHash -LiteralPath $_.FullName).Hash )  $($_.Name)" } | Set-Content -LiteralPath (Join-Path $output "SHA256SUMS-$Version.txt") -Encoding utf8
@{version=$Version;stage=$buildRoot;output=$output;sourceCompiled=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output "build-report-$Version.json") -Encoding utf8
Write-Host "Release ready: $output"
