param([Parameter(Mandatory)][string]$DefinitionDirectory,[Parameter(Mandatory)][string]$Compiler)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$definitionRoot = (Resolve-Path -LiteralPath $DefinitionDirectory).Path
$game = Get-Content -Encoding utf8 -LiteralPath (Join-Path $definitionRoot 'game.json') -Raw | ConvertFrom-Json
$manifest = Get-Content -Encoding utf8 -LiteralPath (Join-Path $definitionRoot 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.id -notmatch '^[a-z0-9][a-z0-9.-]{2,79}$') { throw 'Invalid package ID.' }
$stage = Join-Path $root ('artifacts/gamepacks/' + $manifest.id + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $stage,(Join-Path $stage 'modules') | Out-Null
& dotnet restore (Join-Path $root 'adapters/Unreal/ApexAudio.Unreal.csproj') --configfile (Join-Path $root 'NuGet.Config') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Adapter restore failed.' }
& dotnet publish (Join-Path $root 'adapters/Unreal/ApexAudio.Unreal.csproj') -c Release --no-restore -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw 'Adapter build failed.' }
$compiled = Join-Path $stage 'modules/ApexUnrealCapture.dll'
& $Compiler cc -target x86_64-windows-gnu -shared -O2 (Join-Path $root 'adapters/Unreal/native/ApexUnrealCapture.c') -o $compiled
if ($LASTEXITCODE -ne 0) { throw 'Native capture build failed.' }
$hash = (Get-FileHash -LiteralPath $compiled).Hash
$game.captureFile = 'modules/ApexUnrealCapture-' + $hash.Substring(0,12) + '.dll'
$game.captureSha256 = $hash
Move-Item -LiteralPath $compiled -Destination (Join-Path $stage $game.captureFile)
Get-ChildItem -LiteralPath $definitionRoot | Copy-Item -Destination $stage -Recurse -Force
$game | ConvertTo-Json -Depth 30 | Set-Content -Encoding utf8 -LiteralPath (Join-Path $stage 'game.json')
& (Join-Path $root 'tools/Pack.ps1') -Directory $stage -Output (Join-Path $root ('dist/packages/' + $manifest.id + '.gpack'))
