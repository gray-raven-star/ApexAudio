param([Parameter(Mandatory)][string]$InstalledData)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$adapterRoot = Join-Path $projectRoot 'artifacts/wwise'
foreach ($packFolder in Get-ChildItem -LiteralPath (Join-Path $InstalledData 'packs') -Directory) {
    if (-not (Test-Path -LiteralPath (Join-Path $packFolder.FullName 'ApexAudio.Wwise.exe'))) { continue }
    $manifest = Get-Content -LiteralPath (Join-Path $packFolder.FullName 'manifest.json') -Raw | ConvertFrom-Json -AsHashtable
    $stage = Join-Path $projectRoot ('artifacts/connection-update/' + $manifest.id)
    New-Item -ItemType Directory -Force $stage | Out-Null
    Get-ChildItem -LiteralPath $packFolder.FullName | Copy-Item -Destination $stage -Recurse -Force
    Get-ChildItem -LiteralPath $adapterRoot -File | Copy-Item -Destination $stage -Force
    $definitionPath = Join-Path $stage 'game.json'
    $definition = Get-Content $definitionPath -Raw | ConvertFrom-Json -AsHashtable
    if ($definition.sdkYear -eq 2023 -and $definition.sdkSeries -eq 1) {
        $definition.captureFile = 'modules/ApexWwiseCapture-2023.1-v5.dll'
        $nativePath = Join-Path $projectRoot 'artifacts/ApexWwiseCapture-2023.1-v5.dll'
        Copy-Item $nativePath (Join-Path $stage $definition.captureFile) -Force
        $definition.captureSha256 = (Get-FileHash $nativePath).Hash
        $definition | ConvertTo-Json -Depth 30 | Set-Content $definitionPath -Encoding utf8
    }
    $manifest.version = '1.1.0'
    $manifest | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $stage 'manifest.json') -Encoding utf8
    & (Join-Path $PSScriptRoot 'Pack.ps1') -Directory $stage -Output (Join-Path $projectRoot ('dist/packages/' + $manifest.id + '.gpack'))
}
