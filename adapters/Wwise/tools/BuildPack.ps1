param([Parameter(Mandatory)][string]$DefinitionDirectory,[Parameter(Mandatory)][string]$Compiler,[string]$AdapterProject,[string]$NativeSource)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$definitionRoot = (Resolve-Path -LiteralPath $DefinitionDirectory).Path
$game = Get-Content -Encoding utf8 -LiteralPath (Join-Path $definitionRoot 'game.json') -Raw | ConvertFrom-Json
$manifest = Get-Content -Encoding utf8 -LiteralPath (Join-Path $definitionRoot 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.id -notmatch '^[a-z0-9][a-z0-9.-]{2,79}$') { throw 'Invalid package ID.' }
$stage = Join-Path $root ('artifacts/gamepacks/' + $manifest.id + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $stage,(Join-Path $stage 'modules') | Out-Null
if (-not $AdapterProject) { $AdapterProject = Join-Path $root 'adapters/Wwise/ApexAudio.Wwise.csproj' }
& dotnet publish $AdapterProject -c Release --no-restore -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw 'Wwise adapter build failed.' }
$module = Join-Path $stage $game.captureFile
$flags = @()
if (-not $NativeSource) {
    $NativeSource = Join-Path $root 'adapters/Wwise/native/ApexWwiseCapture.c'
    $flags = @("-DCAPTURE_BANK_VERSION=$($game.bankVersion)", "-DCAPTURE_SDK_YEAR=$($game.sdkYear)", "-DCAPTURE_SDK_SERIES=$($game.sdkSeries)")
}
& $Compiler cc -target x86_64-windows-gnu -shared -O2 @flags $NativeSource -o $module
if ($LASTEXITCODE -ne 0) { throw 'Native capture build failed.' }
& (Join-Path $root 'tools/Copy-GamePackDefinition.ps1') -DefinitionDirectory $definitionRoot -Destination $stage
$game.captureSha256 = (Get-FileHash -LiteralPath $module).Hash
$game | ConvertTo-Json -Depth 30 | Set-Content -Encoding utf8 -LiteralPath (Join-Path $stage 'game.json')
& (Join-Path $root 'tools/Pack.ps1') -Directory $stage -Output (Join-Path $root ('dist/packages/' + $manifest.id + '.gpack'))
