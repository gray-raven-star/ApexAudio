param([string]$Compiler, [string]$Output, [int]$TargetProcessId = 0)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$checks = [ordered]@{}; $problems = [Collections.Generic.List[string]]::new()
try {
    $checks.platform = [ordered]@{ windows = [bool]$IsWindows; x64 = [Environment]::Is64BitProcess; powerShell = $PSVersionTable.PSVersion.ToString() }
    if (-not $IsWindows -or -not [Environment]::Is64BitProcess -or $PSVersionTable.PSVersion.Major -lt 7) { throw 'Use Windows x64 and PowerShell 7 (pwsh).' }
    . (Join-Path $PSScriptRoot 'Environment.ps1')
    $paths = Initialize-ApexEnvironment $root
    $checks.directories = @($paths.GetEnumerator() | ForEach-Object {
        $probe = Join-Path $_.Value ('apex-write-check-' + [Guid]::NewGuid().ToString('N') + '.tmp')
        try { [IO.File]::WriteAllText($probe, 'writable'); [ordered]@{ purpose = $_.Key; path = $_.Value; writable = $true } }
        finally { if ([IO.File]::Exists($probe)) { [IO.File]::Delete($probe) } }
    })
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) { throw 'Install .NET 8 SDK; a Desktop Runtime alone cannot build adapters.' }
    $sdks = @(& $dotnet.Source --list-sdks)
    if ($LASTEXITCODE -ne 0 -or -not ($sdks -match '^8\.')) { throw '.NET 8 SDK is required.' }
    $checks.dotnet = [ordered]@{ path = $dotnet.Source; sdks = $sdks }
    . (Join-Path $PSScriptRoot 'FindCompiler.ps1')
    $zig = Resolve-ApexCompiler $Compiler
    $checks.compiler = [ordered]@{ path = $zig; version = (& $zig version); sha256 = (Get-FileHash -LiteralPath $zig -Algorithm SHA256).Hash }
    if ($TargetProcessId -gt 0) {
        Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ApexSetupReadAccess {
 [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
 [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
}
'@
        $handle = [ApexSetupReadAccess]::OpenProcess(0x410, $false, $TargetProcessId)
        $errorCode = if ($handle -eq [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::GetLastWin32Error() } else { 0 }
        if ($handle -ne [IntPtr]::Zero) { [void][ApexSetupReadAccess]::CloseHandle($handle) }
        $checks.processAccess = [ordered]@{ pid = $TargetProcessId; queryAndReadAllowed = ($errorCode -eq 0); nativeErrorCode = $errorCode; writeOrLoadAttempted = $false; note = 'A read-access check does not prove that module loading or an audio ABI works.' }
    }
} catch { $problems.Add($_.Exception.Message) }
$report = [ordered]@{ reportVersion = 1; command = 'setup'; outcome = $(if ($problems.Count) { 'failed' } else { 'completed' }); checks = $checks; errors = @($problems.ToArray()); networkDownloadPerformed = $false; systemPermissionsChanged = $false }
$json = $report | ConvertTo-Json -Depth 8
if ($Output) { $destination = [IO.Path]::GetFullPath($Output); [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)); [IO.File]::WriteAllText($destination, $json) }
$json
if ($problems.Count) { exit 1 }
