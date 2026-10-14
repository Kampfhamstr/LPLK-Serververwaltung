[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Use-Environment.ps1')
$workspace = Split-Path $PSScriptRoot -Parent
& dotnet (Join-Path $env:LPLK_PORTAL_DIR 'CS2ServerPortal.dll') --workspace $workspace --ssh-panel-access
if ($LASTEXITCODE -ne 0) { throw 'Die Panel-Zugriffsdiagnose ist fehlgeschlagen.' }
