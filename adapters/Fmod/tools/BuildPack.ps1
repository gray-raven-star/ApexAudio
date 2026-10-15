param([Parameter(Mandatory)][string]$Compiler,[Parameter(Mandatory)][string]$Profile)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$build=Join-Path $root 'artifacts/fmod'
New-Item -ItemType Directory -Force (Join-Path $build 'native') | Out-Null
& $Compiler cc -target x86_64-windows-gnu -shared -O2 (Join-Path $root 'adapters/Fmod/native/ApexFmodCapture.c') -o (Join-Path $build 'native/ApexFmodCapture-v2.dll')
if($LASTEXITCODE -ne 0){throw 'FMOD native build failed'}
& dotnet publish (Join-Path $root 'adapters/Fmod/ApexAudio.Fmod.csproj') -c Release -o $build --configfile (Join-Path $root 'NuGet.Config') --nologo
if($LASTEXITCODE -ne 0){throw 'FMOD adapter build failed'}
$manifest=Get-Content (Join-Path $Profile 'manifest.json') -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
if($manifest.id -notmatch '^[a-z0-9][a-z0-9.-]{2,79}$'){throw 'Invalid package ID'}
$stage=Join-Path $root ('artifacts/fmod-packs/'+$manifest.id+'-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $stage,(Join-Path $stage 'native') | Out-Null
Get-ChildItem $build -File | Copy-Item -Destination $stage -Force
Get-ChildItem $Profile | Copy-Item -Destination $stage -Recurse -Force
$definition=Get-Content (Join-Path $stage 'game.json') -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
$compiled=Join-Path $build 'native/ApexFmodCapture-v2.dll'
$definition.captureSha256=(Get-FileHash $compiled).Hash
$definition.captureFile='native/ApexFmodCapture-'+$definition.captureSha256.Substring(0,12)+'.dll'
Get-ChildItem (Join-Path $stage 'native') -File -Filter 'ApexFmodCapture-*.dll' | ForEach-Object {Remove-Item -LiteralPath $_.FullName}
Copy-Item $compiled (Join-Path $stage $definition.captureFile) -Force
$definition | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $stage 'game.json') -Encoding utf8
& (Join-Path $root 'tools/Pack.ps1') -Directory $stage -Output (Join-Path $root ('dist/packages/'+$manifest.id+'.gpack'))
