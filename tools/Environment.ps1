function Initialize-ApexEnvironment([string]$ProjectRoot) {
    $cache = Join-Path ([IO.Path]::GetFullPath($ProjectRoot)) '.cache'
    $paths = [ordered]@{
        DOTNET_CLI_HOME = Join-Path $cache 'dotnet-home'
        NUGET_PACKAGES = Join-Path $cache 'nuget'
        ZIG_GLOBAL_CACHE_DIR = Join-Path $cache 'zig-global'
        ZIG_LOCAL_CACHE_DIR = Join-Path $cache 'zig-local'
        APEX_AUDIO_TEMP = Join-Path $cache 'tmp'
    }
    foreach ($name in @($paths.Keys)) {
        $explicit = [Environment]::GetEnvironmentVariable($name, 'Process')
        if ($explicit) { $paths[$name] = [IO.Path]::GetFullPath($explicit) }
        else { [Environment]::SetEnvironmentVariable($name, $paths[$name], 'Process') }
        [void][IO.Directory]::CreateDirectory($paths[$name])
    }
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    $env:DOTNET_NOLOGO = '1'
    return $paths
}
