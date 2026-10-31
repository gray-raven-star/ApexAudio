param([string]$Compiler = '', [string]$LegacyPack = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Environment.ps1')
$null = Initialize-ApexEnvironment $projectRoot
$packages = Join-Path $projectRoot 'tests/fixtures/v2'

# These archives are historical test inputs, not products of this checkout.
foreach ($line in Get-Content -LiteralPath (Join-Path $packages 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw 'Invalid fixture checksum record.' }
    if ((Get-FileHash -LiteralPath (Join-Path $packages $Matches[2]) -Algorithm SHA256).Hash -ne $Matches[1]) {
        throw 'V2 fixture checksum mismatch.'
    }
}

if ($Compiler) {
    # Rebuild into a separate test directory; never replace the historical inputs.
    $output = Join-Path $projectRoot ('artifacts/source-v2-' + [Guid]::NewGuid().ToString('N'))
    $backend = Join-Path $output 'ApexAudio-Fmod-0.2.0.gbackend'
    & (Join-Path $PSScriptRoot 'BuildV2Backend.ps1') -Compiler $Compiler -Destination $backend
    if ($LASTEXITCODE -ne 0) { throw 'Source backend build failed.' }
    & dotnet build (Join-Path $projectRoot 'tools/PackV2/ApexAudio.PackV2.csproj') -c Release -m:1 --nologo
    if ($LASTEXITCODE -ne 0) { throw 'PackV2 build failed.' }
    $descriptorRoot = Join-Path $output 'backend'
    [IO.Compression.ZipFile]::ExtractToDirectory($backend, $descriptorRoot)
    $digest = (Get-FileHash -LiteralPath (Join-Path $descriptorRoot 'backend.json') -Algorithm SHA256).Hash
    foreach ($pack in Get-ChildItem -LiteralPath $packages -Filter '*.gpack') {
        $definition = Join-Path $output ('definitions/' + $pack.BaseName)
        [IO.Compression.ZipFile]::ExtractToDirectory($pack.FullName, $definition)
        $manifestPath = Join-Path $definition 'manifest.json'
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        foreach ($profile in $manifest.profiles) { $profile.backend.descriptorSha256 = $digest }
        $manifest | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $manifestPath -Encoding utf8
        & dotnet (Join-Path $projectRoot 'tools/PackV2/bin/Release/net8.0-windows/ApexAudio.PackV2.dll') $definition (Join-Path $output $pack.Name)
        if ($LASTEXITCODE -ne 0) { throw 'Source test configuration packaging failed.' }
    }
    $packages = $output
    Write-Host 'Testing rebuilt backend and repackaged configurations (local verification only).'
} else {
    Write-Host 'Testing source host code against Owner-supplied backend fixtures.'
}

& dotnet build (Join-Path $projectRoot 'tests/ApexAudio.V2.Tests/ApexAudio.V2.Tests.csproj') -c Release -m:1 --nologo
if ($LASTEXITCODE -ne 0) { throw 'V2 test build failed.' }
$testArgs = @($projectRoot, '--packages', $packages)
if ($LegacyPack) { $testArgs += @('--legacy', [IO.Path]::GetFullPath($LegacyPack)) }
& dotnet (Join-Path $projectRoot 'tests/ApexAudio.V2.Tests/bin/Release/net8.0-windows/ApexAudio.V2.Tests.dll') @testArgs
if ($LASTEXITCODE -ne 0) { throw 'V2 protocol tests failed.' }
