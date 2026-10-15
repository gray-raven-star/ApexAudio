param([Parameter(Mandatory)][string]$Directory,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path -LiteralPath $Directory).Path
$manifestPath = Join-Path $packageRoot 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
$manifest.files = @{}
foreach ($file in Get-ChildItem -LiteralPath $packageRoot -File -Recurse) {
    $relative = [IO.Path]::GetRelativePath($packageRoot,$file.FullName).Replace('\','/')
    if ($relative -eq 'manifest.json') { continue }
    $manifest.files[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
}
$manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding utf8
$resolvedOutput = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$temporary = $resolvedOutput + '.tmp'
if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
[IO.Compression.ZipFile]::CreateFromDirectory($packageRoot,$temporary)
Move-Item -LiteralPath $temporary -Destination $resolvedOutput -Force
