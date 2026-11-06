param(
    [Parameter(Mandatory)][string]$DefinitionDirectory,
    [Parameter(Mandatory)][string]$Destination
)

Get-ChildItem -LiteralPath $DefinitionDirectory |
    Where-Object { $_.Name -notin @('AGENTS.md', 'build.json', 'PROVENANCE.md') -and $_.Name -notlike '_*' } |
    Copy-Item -Destination $Destination -Recurse -Force
