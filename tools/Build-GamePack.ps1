param([Parameter(Mandatory)][string]$DefinitionDirectory,[string]$Compiler)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Environment.ps1')
$null = Initialize-ApexEnvironment $root
$definitionRoot = (Resolve-Path -LiteralPath $DefinitionDirectory).Path
$gameText = Get-Content -LiteralPath (Join-Path $definitionRoot 'game.json') -Raw
$manifestText = Get-Content -LiteralPath (Join-Path $definitionRoot 'manifest.json') -Raw
if (($gameText + $manifestText).Contains('__REQUIRED__')) { throw 'Template is incomplete. Fill REQUIRED fields using verified evidence.' }
$game = $gameText | ConvertFrom-Json
$manifest = $manifestText | ConvertFrom-Json
if ($manifest.id -notmatch '^[a-z0-9][a-z0-9.-]{2,79}$' -or $manifest.demo -or -not $manifest.games.Count) { throw 'Invalid real-game manifest.' }
foreach ($match in $manifest.games) { if ($match.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or $match.sha256 -match '^0+$') { throw 'Supply a verified executable SHA256.' } }
if ($game.backend -ne 'Custom' -and (-not $game.buses.Count)) { throw 'No verified audio groups were supplied.' }
foreach ($presetPath in $manifest.presets) {
    $preset = Get-Content -LiteralPath (Join-Path $definitionRoot $presetPath) -Raw | ConvertFrom-Json
    if ($preset.packId -ne $manifest.id) { throw 'Preset packId does not match the manifest.' }
}
if ($game.backend -eq 'Custom') {
    $stage = Join-Path $root ('artifacts/custom-packs/' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    & dotnet publish (Join-Path $definitionRoot 'adapter/YourAdapter.csproj') -c Release --no-self-contained -o $stage --configfile (Join-Path $root 'NuGet.Config') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Custom adapter build failed.' }
    Get-ChildItem -LiteralPath $definitionRoot | Where-Object Name -ne 'adapter' | Copy-Item -Destination $stage -Recurse -Force
    & (Join-Path $PSScriptRoot 'Pack.ps1') -Directory $stage -Output (Join-Path $root ('dist/packages/' + $manifest.id + '.gpack'))
    return
}
. (Join-Path $PSScriptRoot 'FindCompiler.ps1')
$zig = Resolve-ApexCompiler $Compiler
switch ($game.backend) {
    'Wwise' {
        if ($game.sdkYear -le 0 -or $game.sdkSeries -le 0 -or $game.bankVersion -le 0) { throw 'Verify Wwise SDK series and bank structure first.' }
        $header = [Convert]::FromBase64String($game.bankHeader)
        if ($header.Length -lt 28 -or [BitConverter]::ToInt32($header) -ne $game.bankVersion) { throw 'Invalid Wwise bank header.' }
        $evidenceFile = Join-Path $definitionRoot 'group-evidence.json'
        if (Test-Path -LiteralPath $evidenceFile) {
            $evidence = Get-Content -LiteralPath $evidenceFile -Raw | ConvertFrom-Json
            if ($evidence.packId -ne $manifest.id -or $evidence.packVersion -ne $manifest.version) { throw 'Group evidence identity does not match the package.' }
            foreach ($bus in @($game.buses | Where-Object { -not $_.auxiliary -and $_.slot -ge 0 })) {
                $entries = @($evidence.groups | Where-Object { "$($_.id)" -eq "$($bus.id)" })
                if ($entries.Count -ne 1) { throw "Supply one effect-slot evidence record for bus $($bus.id). See docs/group-evidence.md." }
                $effect = $entries[0].effects
                if ($bus.slot -gt 3 -or $effect.selectedSlot -ne $bus.slot -or $effect.occupiedSlots -contains $bus.slot) { throw "Effect-slot conflict for bus $($bus.id). Full slots must use slot=-1." }
                if ($effect.verification -notin @('static-only','runtime-verified') -or -not $effect.source -or $effect.sourceSha256 -notmatch '^[0-9a-fA-F]{64}$' -or $effect.targetSha256 -ne $game.engineSha256) { throw "Effect-slot evidence is incomplete for bus $($bus.id). Static evidence does not guarantee runtime ownership." }
            }
        } else { Write-Warning 'Legacy definition has no group-evidence.json. Static/runtime slot verification is not recorded; see docs/group-evidence.md.' }
        & dotnet restore (Join-Path $root 'adapters/Wwise/ApexAudio.Wwise.csproj') --configfile (Join-Path $root 'NuGet.Config') --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Adapter restore failed.' }
        & (Join-Path $root 'adapters/Wwise/tools/BuildPack.ps1') -DefinitionDirectory $definitionRoot -Compiler $zig
    }
    'Fmod' {
        if (-not $game.wrapperRva -or -not $game.runtimeVersion -or -not $game.modules.PSObject.Properties.Count) { throw 'Verify FMOD modules, system locator and runtime version first.' }
        & (Join-Path $root 'adapters/Fmod/tools/BuildPack.ps1') -Profile $definitionRoot -Compiler $zig
    }
    'Unreal' {
        foreach ($field in @('engineRva','nameBlocksRva','vtableRva','registerRva','unregisterRva')) { if (-not $game.$field) { throw "Missing verified Unreal locator: $field" } }
        & (Join-Path $root 'adapters/Unreal/tools/BuildPack.ps1') -DefinitionDirectory $definitionRoot -Compiler $zig
    }
    default { throw 'Unknown backend. Use Wwise, Fmod, Unreal or Custom.' }
}
