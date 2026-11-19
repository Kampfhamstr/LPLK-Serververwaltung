[CmdletBinding()]
param([string]$Candidate)
$ErrorActionPreference='Stop'
$python=Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
if(-not(Test-Path -LiteralPath $python -PathType Leaf)) {
    $python=Get-Command python.exe,python3.exe -CommandType Application -ErrorAction SilentlyContinue |
        Where-Object { $_.Source -notlike '*WindowsApps*' } | Select-Object -First 1 -ExpandProperty Source
}
if(-not $python){throw 'Python-Laufzeit für die Befehlsprüfung fehlt. Paketfreigabe bleibt gesperrt.'}
$arguments=@('-X','utf8',(Join-Path $PSScriptRoot 'Check-FleetCommands.py'),'--source')
if($Candidate){$arguments+=@('--candidate',$Candidate)}
& $python @arguments
if($LASTEXITCODE -ne 0){throw 'Befehlsregistrierung enthält Kollisionen oder ungeprüfte dynamische Registrierungen. Paket nicht freigegeben.'}
