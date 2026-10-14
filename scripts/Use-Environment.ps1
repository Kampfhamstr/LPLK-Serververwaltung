$workspace = Split-Path $PSScriptRoot -Parent
$machineSettings = Join-Path $env:LOCALAPPDATA 'LPLK-Serververwaltung\toolchain.json'
if (!(Test-Path -LiteralPath $machineSettings)) { throw 'Local toolchain missing. Run scripts/Install-Toolchain.ps1 on this PC first.' }
$toolchain = Get-Content -LiteralPath $machineSettings -Raw | ConvertFrom-Json
$dotnetLocation = if ([IO.Path]::IsPathRooted($toolchain.dotnetRoot)) { $toolchain.dotnetRoot } else { Join-Path $workspace $toolchain.dotnetRoot }
$gitLocation = if ([IO.Path]::IsPathRooted($toolchain.gitRoot)) { $toolchain.gitRoot } else { Join-Path $workspace $toolchain.gitRoot }
$env:DOTNET_ROOT = $dotnetLocation
$env:DOTNET_ROOT_X64 = $dotnetLocation
$env:DOTNET_CLI_HOME = Join-Path $toolchain.cacheRoot 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $toolchain.cacheRoot 'nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $toolchain.cacheRoot 'nuget-http'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $toolchain.cacheRoot 'nuget-plugins'
$env:LPLK_PORTAL_DIR = $toolchain.portalRoot
$env:LPLK_BUILD_ROOT = $toolchain.buildRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:PATH = "$dotnetLocation;$gitLocation\cmd;$env:PATH"
$env:GIT_SSH_COMMAND = 'C:/Windows/System32/OpenSSH/ssh.exe'
New-Item -ItemType Directory -Path $env:DOTNET_CLI_HOME,$env:NUGET_PACKAGES -Force | Out-Null
