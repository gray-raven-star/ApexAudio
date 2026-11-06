param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9.-]{2,79}$')][string]$Id,
    [Parameter(Mandatory)][string]$Name,
    [Parameter(Mandatory)][ValidateSet('Wwise','Fmod','Unreal','Custom')][string]$Backend,
    [Parameter(Mandatory)][string]$Directory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$starter = Join-Path $root 'gamepack-starter'
$destination = [IO.Path]::GetFullPath($Directory)
if (Test-Path -LiteralPath $destination) { throw 'Choose a new directory; existing files will not be overwritten.' }
New-Item -ItemType Directory -Path $destination,(Join-Path $destination 'presets') | Out-Null
$manifest = Get-Content -LiteralPath (Join-Path $starter 'manifest.json') -Raw | ConvertFrom-Json -AsHashtable
$manifest.id = $Id
$manifest.name = $Name
$manifest.entryPoint = if ($Backend -eq 'Custom') { 'YourAdapter.exe' } else { "ApexAudio.$Backend.exe" }
$definitionPath = if ($Backend -eq 'Wwise') { Join-Path $starter 'game.json' } else { Join-Path $root "templates/$Backend/game.json" }
$definition = Get-Content -LiteralPath $definitionPath -Raw | ConvertFrom-Json -AsHashtable
if ($Backend -eq 'Wwise') { $definition.title=$Name } else { $definition.name=$Name }
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $destination 'manifest.json') -Encoding utf8
$definition | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $destination 'game.json') -Encoding utf8
$preset = Get-Content -LiteralPath (Join-Path $starter 'presets/initial.json') -Raw | ConvertFrom-Json -AsHashtable
$preset.packId = $Id
$preset | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $destination 'presets/initial.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $root 'templates/PACK-README.md') -Destination (Join-Path $destination 'README.md')
Copy-Item -LiteralPath (Join-Path $starter 'AGENTS.md') -Destination $destination
$validation = Get-Content -LiteralPath (Join-Path $starter 'validation-report.json') -Raw | ConvertFrom-Json -AsHashtable
$validation.packId = $Id
$validation.packVersion = $manifest.version
$validation | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $destination 'validation-report.json') -Encoding utf8
$evidence = Get-Content -LiteralPath (Join-Path $starter 'group-evidence.json') -Raw | ConvertFrom-Json -AsHashtable
$evidence.packId = $Id
$evidence.packVersion = $manifest.version
$evidence | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $destination 'group-evidence.json') -Encoding utf8
if ($Backend -eq 'Custom') {
    $adapter = Join-Path $destination 'adapter'
    New-Item -ItemType Directory -Path $adapter | Out-Null
    Copy-Item (Join-Path $root 'templates/CustomAdapter/*') -Destination $adapter
    $relative = [IO.Path]::GetRelativePath($adapter,(Join-Path $root 'src/ApexAudio.Contracts/ApexAudio.Contracts.csproj')).Replace('\','/')
    (Get-Content -LiteralPath (Join-Path $adapter 'YourAdapter.csproj') -Raw).Replace('__CONTRACTS_PROJECT__',$relative) | Set-Content -LiteralPath (Join-Path $adapter 'YourAdapter.csproj') -Encoding utf8
}
Write-Host "Created incomplete definition: $destination. Fill REQUIRED fields and verified groups before building. No game was scanned or connected."
