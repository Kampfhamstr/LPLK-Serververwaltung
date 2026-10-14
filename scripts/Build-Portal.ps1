[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Use-Environment.ps1')
$workspace = Split-Path $PSScriptRoot -Parent
$project = Join-Path $workspace 'portal/CS2ServerPortal.csproj'
& dotnet restore $project --locked-mode --configfile (Join-Path $workspace 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Portal dependency restore failed.' }
& dotnet publish $project -c Release --no-restore -o $env:LPLK_PORTAL_DIR
if ($LASTEXITCODE -ne 0) { throw 'Portal build failed. Close the running portal before rebuilding.' }
& dotnet (Join-Path $env:LPLK_PORTAL_DIR 'CS2ServerPortal.dll') --workspace $workspace --self-test
if ($LASTEXITCODE -ne 0) { throw 'Portal verification failed.' }
