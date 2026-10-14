#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$machineRuntime = Join-Path $env:LOCALAPPDATA 'LPLK-Serververwaltung'
$tools = Join-Path $machineRuntime 'tools'
$cache = Join-Path $machineRuntime 'cache/downloads'
New-Item -ItemType Directory -Path $tools,$cache -Force | Out-Null
$headers = @{'User-Agent'='CS2-Workspace/1.0'}
$metadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json' -Headers $headers -TimeoutSec 45
$sdkVersion = $metadata.'latest-sdk'
$sdk = $metadata.releases | ForEach-Object { $_.sdks } | Where-Object version -eq $sdkVersion | Select-Object -First 1
$sdkFile = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
if (!$sdkFile -or !$sdkFile.hash) { throw 'Microsoft SDK download metadata is incomplete.' }
$sdkFolder = Join-Path $tools "dotnet/$sdkVersion"
if (!(Test-Path (Join-Path $sdkFolder 'dotnet.exe'))) {
    $sdkArchive = Join-Path $cache "dotnet-sdk-$sdkVersion.zip"
    Write-Output "Downloading Microsoft .NET SDK $sdkVersion"
    if (!(Test-Path $sdkArchive)) { Invoke-WebRequest $sdkFile.url -OutFile $sdkArchive -TimeoutSec 900 }
    if ((Get-FileHash $sdkArchive -Algorithm SHA512).Hash -ne $sdkFile.hash) { throw 'Microsoft SDK checksum mismatch.' }
    New-Item -ItemType Directory -Path $sdkFolder -Force | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($sdkArchive,$sdkFolder,$true)
}
$release = Invoke-RestMethod 'https://api.github.com/repos/git-for-windows/git/releases/latest' -Headers $headers -TimeoutSec 45
$gitAsset = $release.assets | Where-Object { $_.name -match '^MinGit-[\d.]+-64-bit\.zip$' } | Select-Object -First 1
if (!$gitAsset -or $gitAsset.digest -notmatch '^sha256:[a-f0-9]{64}$') { throw 'Official Git download has no SHA256 digest.' }
$gitFolder = Join-Path $tools "git/$($release.tag_name)"
if (!(Test-Path (Join-Path $gitFolder 'cmd/git.exe'))) {
    $gitArchive = Join-Path $cache $gitAsset.name
    Write-Output "Downloading Git $($release.tag_name)"
    if (!(Test-Path $gitArchive)) { Invoke-WebRequest $gitAsset.browser_download_url -OutFile $gitArchive -TimeoutSec 600 }
    if ((Get-FileHash $gitArchive -Algorithm SHA256).Hash -ne $gitAsset.digest.Substring(7)) { throw 'Git checksum mismatch.' }
    New-Item -ItemType Directory -Path $gitFolder -Force | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($gitArchive,$gitFolder,$true)
}
$state = [ordered]@{dotnetVersion=$sdkVersion;dotnetRoot=$sdkFolder;gitVersion=$release.tag_name;gitRoot=$gitFolder;cacheRoot=(Join-Path $machineRuntime 'cache');portalRoot=(Join-Path $machineRuntime 'portal');buildRoot=(Join-Path $machineRuntime 'build');installedAt=[DateTimeOffset]::UtcNow.ToString('o')}
$state | ConvertTo-Json | Set-Content (Join-Path $machineRuntime 'toolchain.json') -Encoding utf8
$metadata = [ordered]@{dotnetVersion=$sdkVersion;gitVersion=$release.tag_name;installedAt=$state.installedAt;runtimeLocation='local-per-PC'}
New-Item -ItemType Directory -Path (Join-Path $workspace 'tools') -Force | Out-Null
$metadata | ConvertTo-Json | Set-Content (Join-Path $workspace 'tools/toolchain.json') -Encoding utf8
& (Join-Path $sdkFolder 'dotnet.exe') --version
& (Join-Path $gitFolder 'cmd/git.exe') --version
Write-Output 'Toolchain ready.'
