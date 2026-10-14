[CmdletBinding()]
param([switch]$Ssh, [switch]$GitHub)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Use-Environment.ps1')
$workspace = Split-Path $PSScriptRoot -Parent
$app = Join-Path $env:LPLK_PORTAL_DIR 'CS2ServerPortal.exe'
if (!(Test-Path $app)) { throw 'Portal has not been built yet.' }
$arguments = @('--workspace',('"' + $workspace + '"'))
if ($Ssh) { $arguments += '--ssh' }
if ($GitHub) { $arguments += '--github' }
Start-Process -FilePath $app -ArgumentList $arguments -WorkingDirectory $workspace -Wait
