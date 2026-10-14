[CmdletBinding()]
param([switch]$StatusOnly, [switch]$TestOnly)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Use-Environment.ps1')
$workspace = Split-Path $PSScriptRoot -Parent
$mode = if ($StatusOnly) { '--ssh-status' } elseif ($TestOnly) { '--ssh-test' } else { '--ssh-diagnostics' }
& dotnet (Join-Path $env:LPLK_PORTAL_DIR 'CS2ServerPortal.dll') --workspace $workspace $mode
exit $LASTEXITCODE
