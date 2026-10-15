param([Parameter(Mandatory)][string]$DefinitionDirectory,[Parameter(Mandatory)][string]$Compiler)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$definitionRoot = (Resolve-Path -LiteralPath $DefinitionDirectory).Path
$game = Get-Content -Encoding utf8 -LiteralPath (Join-Path $definitionRoot 'game.json') -Raw | ConvertFrom-Json
$manifest = Get-Content -Encoding utf8 -LiteralPath (Join-Path $definitionRoot 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.id -notmatch '^[a-z0-9][a-z0-9.-]{2,79}$') { throw 'Invalid package ID.' }
$stage = Join-Path $root ('artifacts/gamepacks/' + $manifest.id + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $stage,(Join-Path $stage 'modules') | Out-Null
& dotnet publish (Join-Path $root 'adapters/Wwise/ApexAudio.Wwise.csproj') -c Release --no-restore -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw 'Wwise adapter build failed.' }
$module = Join-Path $stage $game.captureFile
& $Compiler cc -target x86_64-windows-gnu -shared -O2 "-DCAPTURE_BANK_VERSION=$($game.bankVersion)" "-DCAPTURE_SDK_YEAR=$($game.sdkYear)" "-DCAPTURE_SDK_SERIES=$($game.sdkSeries)" (Join-Path $root 'adapters/Wwise/native/ApexWwiseCapture.c') -o $module
if ($LASTEXITCODE -ne 0) { throw 'Native capture build failed.' }
Get-ChildItem -LiteralPath $definitionRoot | Copy-Item -Destination $stage -Recurse -Force
$game.captureSha256 = (Get-FileHash -LiteralPath $module).Hash
$game | ConvertTo-Json -Depth 30 | Set-Content -Encoding utf8 -LiteralPath (Join-Path $stage 'game.json')
& (Join-Path $root 'tools/Pack.ps1') -Directory $stage -Output (Join-Path $root ('dist/packages/' + $manifest.id + '.gpack'))
