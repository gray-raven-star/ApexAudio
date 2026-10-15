param([Parameter(Mandatory)][string]$Compiler)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Environment.ps1')
$null = Initialize-ApexEnvironment $root
$capture = Join-Path $root 'artifacts/ApexWwiseCapture-2023.1-v5.dll'
$bridge = Join-Path $root 'providers/XHaptics/native/ApexGroupsBridge-v5.dll'
& $Compiler cc -target x86_64-windows-gnu -shared -O2 -DCAPTURE_BANK_VERSION=150 -DCAPTURE_SDK_YEAR=2023 -DCAPTURE_SDK_SERIES=1 (Join-Path $root 'adapters/Wwise/native/ApexWwiseCapture.c') -o $capture
if ($LASTEXITCODE -ne 0) { throw 'Capture build failed' }
& $Compiler cc -target x86_64-windows-gnu -shared -O2 (Join-Path $root 'providers/XHaptics/native/ApexGroupsBridge.c') -lole32 -luuid -o $bridge
if ($LASTEXITCODE -ne 0) { throw 'Bridge build failed' }
$configPath = Join-Path $root 'providers/XHaptics/native/xh-support.json'
$config = Get-Content $configPath -Raw | ConvertFrom-Json
$config.bridgeSha256 = (Get-FileHash $bridge).Hash
$config | ConvertTo-Json | Set-Content $configPath
