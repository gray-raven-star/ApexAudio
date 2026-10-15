param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9.-]{2,79}$')][string]$Id,
    [Parameter(Mandatory)][string]$Name,
    [Parameter(Mandatory)][ValidateSet('Wwise','Fmod','Unreal','Custom')][string]$Backend,
    [Parameter(Mandatory)][string]$Directory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$destination = [IO.Path]::GetFullPath($Directory)
if (Test-Path -LiteralPath $destination) { throw 'Choose a new directory; existing files will not be overwritten.' }
New-Item -ItemType Directory -Path $destination,(Join-Path $destination 'presets') | Out-Null
$entry = if ($Backend -eq 'Custom') { 'YourAdapter.exe' } else { "ApexAudio.$Backend.exe" }
$manifest = [ordered]@{ schemaVersion=1; id=$Id; name=$Name; version='0.1.0'; author='__REQUIRED__'; source='__REQUIRED__'; description='Unverified development package'; protocolVersion=1; entryPoint=$entry; demo=$false; games=@(@{executable='__REQUIRED__.exe';sha256='__REQUIRED__'}); presets=@('presets/initial.json'); files=@{} }
$definition = Get-Content -LiteralPath (Join-Path $root "templates/$Backend/game.json") -Raw | ConvertFrom-Json -AsHashtable
if ($Backend -eq 'Wwise') { $definition.title=$Name } else { $definition.name=$Name }
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $destination 'manifest.json') -Encoding utf8
$definition | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $destination 'game.json') -Encoding utf8
@{schemaVersion=1;id='initial';name='初始配置';packId=$Id;folders=@();groups=@{}} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $destination 'presets/initial.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $root 'templates/PACK-README.md') -Destination (Join-Path $destination 'README.md')
$validation = Get-Content -LiteralPath (Join-Path $root 'templates/validation-report.json') -Raw | ConvertFrom-Json -AsHashtable
$validation.packId = $Id; $validation.packVersion = $manifest.version; $validation.groupEvidenceFile = 'group-evidence.json'
$validation | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $destination 'validation-report.json') -Encoding utf8
@{schemaVersion=1;packId=$Id;packVersion=$manifest.version;groups=@()} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $destination 'group-evidence.json') -Encoding utf8
if ($Backend -eq 'Custom') {
    $adapter = Join-Path $destination 'adapter'
    New-Item -ItemType Directory -Path $adapter | Out-Null
    Copy-Item (Join-Path $root 'templates/CustomAdapter/*') -Destination $adapter
    $relative = [IO.Path]::GetRelativePath($adapter,(Join-Path $root 'src/ApexAudio.Contracts/ApexAudio.Contracts.csproj')).Replace('\','/')
    (Get-Content -LiteralPath (Join-Path $adapter 'YourAdapter.csproj') -Raw).Replace('__CONTRACTS_PROJECT__',$relative) | Set-Content -LiteralPath (Join-Path $adapter 'YourAdapter.csproj') -Encoding utf8
}
Write-Host "Created incomplete definition: $destination. Fill REQUIRED fields and verified groups before building. No game was scanned or connected."
