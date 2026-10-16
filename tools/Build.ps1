param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Environment.ps1')
$null = Initialize-ApexEnvironment $projectRoot
$outputRoot = Join-Path $projectRoot 'dist'
$buildRoot = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Force $outputRoot,$buildRoot | Out-Null
function Publish-Project($project, $destination) {
    & dotnet restore (Join-Path $projectRoot $project) --configfile (Join-Path $projectRoot 'NuGet.Config') --nologo
    if ($LASTEXITCODE -ne 0) { throw "Restore failed: $project" }
    & dotnet publish (Join-Path $projectRoot $project) -c Release --no-self-contained --no-restore -o $destination --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}
Publish-Project 'src/ApexAudio.Desktop/ApexAudio.Desktop.csproj' $outputRoot
# Publishing into an existing directory does not remove files from older releases.
Get-ChildItem -LiteralPath (Join-Path $outputRoot 'native') -File -Filter 'ApexGroupsBridge*' |
    Where-Object Name -ne 'ApexGroupsBridge-v5.dll' |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName }
Publish-Project 'adapters/Mock/ApexAudio.Mock.csproj' (Join-Path $buildRoot 'mock')
Publish-Project 'adapters/Wwise/ApexAudio.Wwise.csproj' (Join-Path $buildRoot 'wwise')
Copy-Item (Join-Path $projectRoot 'vendor/webview2/LICENSE.txt') (Join-Path $outputRoot 'WebView2-LICENSE.txt')
# Replace generated documentation so older development notes cannot remain in dist.
$docsOutput = Join-Path $outputRoot 'docs'
if (Test-Path -LiteralPath $docsOutput) {
    $resolvedOutput = (Resolve-Path -LiteralPath $outputRoot).Path
    $resolvedDocs = (Resolve-Path -LiteralPath $docsOutput).Path
    if ($resolvedDocs -ne (Join-Path $resolvedOutput 'docs') -or
        ((Get-Item -LiteralPath $outputRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        ((Get-Item -LiteralPath $docsOutput).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Documentation cleanup must stay inside the build output directory.'
    }
    Remove-Item -LiteralPath $resolvedDocs -Recurse -Force
}
foreach ($name in @('README.en.md','START-HERE.md','AI-INSTRUCTIONS.md')) {
    $obsolete = Join-Path $outputRoot $name
    if (Test-Path -LiteralPath $obsolete) { Remove-Item -LiteralPath $obsolete }
}
Copy-Item (Join-Path $projectRoot 'packaging/README.release.md') (Join-Path $outputRoot 'README.md') -Force
foreach ($name in @('THIRD-PARTY.md','LICENSE')) {
    Copy-Item (Join-Path $projectRoot $name) $outputRoot
}
New-Item -ItemType Directory -Path $docsOutput -Force | Out-Null
Copy-Item (Join-Path $projectRoot 'docs/user') $docsOutput -Recurse -Force
Copy-Item (Join-Path $projectRoot 'schemas') $outputRoot -Recurse -Force
& (Join-Path $PSScriptRoot 'MakeDemoPack.ps1')
$oldDemo = Join-Path $outputRoot 'packages/demo.gpack'
if (Test-Path -LiteralPath $oldDemo) { Remove-Item -LiteralPath $oldDemo }
if ($Test) {
    Publish-Project 'tests/ApexAudio.Tests/ApexAudio.Tests.csproj' (Join-Path $buildRoot 'tests')
    & dotnet (Join-Path $buildRoot 'tests/ApexAudio.Tests.dll') $projectRoot
    if ($LASTEXITCODE -ne 0) { throw 'Acceptance tests failed.' }
}
Write-Host "Ready: $outputRoot/ApexAudio.exe"
