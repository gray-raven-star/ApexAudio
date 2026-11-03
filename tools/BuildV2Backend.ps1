param([string]$Compiler = 'zig', [string]$Destination = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Environment.ps1')
$null = Initialize-ApexEnvironment $projectRoot
if (!$Destination) { $Destination = Join-Path $projectRoot 'artifacts/ApexAudio-Fmod-0.3.0.gbackend' }
$Destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $Destination) { throw 'Output already exists; choose a new destination.' }
$stage = Join-Path $projectRoot ('artifacts/backend-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
& dotnet publish (Join-Path $projectRoot 'adapters/Fmod/ApexAudio.Fmod.csproj') -c Release --no-self-contained -m:1 -p:UseSharedCompilation=false -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw 'FMOD backend publish failed.' }
$native = Join-Path $stage 'capture.dll'
& $Compiler cc -target x86_64-windows-gnu -shared -O2 (Join-Path $projectRoot 'adapters/Fmod/native/ApexFmodCapture.c') -o $native
if ($LASTEXITCODE -ne 0) { throw 'Native FMOD backend build failed.' }
$nativeName = 'ApexFmodCapture-' + (Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash.Substring(0,12) + '.dll'
Move-Item -LiteralPath $native -Destination (Join-Path $stage $nativeName)
Get-ChildItem -LiteralPath $stage -File | Where-Object Extension -in '.pdb','.lib' | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
$files = [ordered]@{}
Get-ChildItem -LiteralPath $stage -File | Sort-Object Name | ForEach-Object { $files[$_.Name] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
$supportJson = & dotnet (Join-Path $stage 'ApexAudio.Fmod.dll') --describe
if ($LASTEXITCODE -ne 0) { throw 'FMOD support description failed.' }
$descriptor = [ordered]@{ id='apex.fmod'; version='0.3.0'; entryPoint='ApexAudio.Fmod.exe'; contextVersion=2; files=$files; variants=@{ 'studio-v1'=$nativeName }; support=($supportJson | ConvertFrom-Json) }
[IO.File]::WriteAllText((Join-Path $stage 'backend.json'), ($descriptor | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
New-Item -ItemType Directory -Path (Split-Path $Destination -Parent) -Force | Out-Null
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $Destination)
Write-Host "Backend: $Destination"
Write-Host 'Rebuilt descriptors have a new digest. Update profiles.backend.descriptorSha256 when creating configuration packages.'
