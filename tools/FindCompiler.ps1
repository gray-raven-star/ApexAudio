function Resolve-ApexCompiler([string]$Compiler) {
    $projectRoot = Split-Path $PSScriptRoot -Parent
    $candidates = @($Compiler, $env:APEX_ZIG, (Join-Path $projectRoot 'toolchain/zig/zig.exe'))
    $command = Get-Command zig -ErrorAction SilentlyContinue
    if ($command) { $candidates += $command.Source }
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            $resolved = (Resolve-Path -LiteralPath $candidate).Path
            $version = & $resolved version
            if ($LASTEXITCODE -ne 0) { throw 'Compiler is not a working Zig executable.' }
            return $resolved
        }
    }
    throw 'Zig not found. Extract the supplied toolchain into toolchain/zig, set APEX_ZIG, or pass -Compiler <zig.exe>. See START-HERE.md.'
}
