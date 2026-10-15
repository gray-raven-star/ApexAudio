param([Parameter(Mandatory)][string]$Profile,[Parameter(Mandatory)][string]$LegacyRoot,[Parameter(Mandatory)][string]$Id,[string]$Author='本地配置迁移')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if ($Id -notmatch '^[a-z0-9][a-z0-9.-]{2,79}$') { throw 'Invalid package ID.' }
$profileData = Get-Content -LiteralPath $Profile -Raw | ConvertFrom-Json -AsHashtable
if (-not $profileData.canCapture) { throw 'This profile has no capture capability.' }
$catalog = Get-Content -LiteralPath (Join-Path $LegacyRoot 'capture-adapters.json') -Raw | ConvertFrom-Json
$adapter = $catalog.adapters | Where-Object { $_.id -eq $profileData.captureAdapter } | Select-Object -First 1
if (-not $adapter) { throw 'No matching capture module.' }
$stage = Join-Path $projectRoot "artifacts/packs/$Id"
New-Item -ItemType Directory -Force $stage,(Join-Path $stage 'modules'),(Join-Path $stage 'presets') | Out-Null
Copy-Item (Join-Path $projectRoot 'artifacts/wwise/*') $stage -Force
$modulePath = Join-Path $LegacyRoot $adapter.file
$captureName = 'modules/' + [IO.Path]::GetFileName($modulePath)
Copy-Item -LiteralPath $modulePath -Destination (Join-Path $stage 'modules')
$definition = @{
    title=$profileData.title; gameSha256=$profileData.gameSha256; engineModule=$profileData.engineModule; engineSha256=$profileData.engineSha256
    bankVersion=$profileData.bankVersion; bankHeader=$profileData.bankHeader; bankId=$profileData.bankId
    captureFile=$captureName; captureSha256=(Get-FileHash -LiteralPath $modulePath -Algorithm SHA256).Hash
    sdkYear=$adapter.sdkYear; sdkSeries=$adapter.sdkSeries
    buses=@($profileData.buses | ForEach-Object { @{id=$_.id;parent=$_.parent;name=$_.name;nameSource=$_.nameSource;category=$_.category;auxiliary=$_.auxiliary;slot=$_.slot} })
}
$definition | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $stage 'game.json') -Encoding utf8
$groups = @{}
foreach ($bus in $profileData.buses) { if (-not $bus.auxiliary) { $groups[[string]$bus.id] = @{name=$bus.name;enabled=[bool]$bus.include;order=0} } }
@{schemaVersion=1;id='migrated';name='迁移的初始配置';packId=$Id;folders=@();groups=$groups} | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $stage 'presets/author.json') -Encoding utf8
@{
    schemaVersion=1;id=$Id;name=$profileData.title;version='1.0.0';author=$Author;source='从旧声音工具的本地配置迁移'
    description='复用已有 Wwise 采集配置；仅匹配清单中记录的程序版本。不是新的通用扫描结果。'
    protocolVersion=1;entryPoint='ApexAudio.Wwise.exe';demo=$false
    games=@(@{executable=[IO.Path]::GetFileName($profileData.gamePath);sha256=$profileData.gameSha256})
    presets=@('presets/author.json');files=@{}
} | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $stage 'manifest.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'Pack.ps1') -Directory $stage -Output (Join-Path $projectRoot "dist/packages/$Id.gpack")
