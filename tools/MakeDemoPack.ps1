$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $projectRoot 'artifacts/demo-pack'
New-Item -ItemType Directory -Force $stage,(Join-Path $stage 'presets') | Out-Null
Copy-Item (Join-Path $projectRoot 'artifacts/mock/*') $stage -Force
Copy-Item (Join-Path $projectRoot 'packs/demo/manifest.json') $stage
Copy-Item (Join-Path $projectRoot 'packs/demo/presets/author.json') (Join-Path $stage 'presets')
& (Join-Path $PSScriptRoot 'Pack.ps1') -Directory $stage -Output (Join-Path $projectRoot 'artifacts/packages/demo.gpack')
