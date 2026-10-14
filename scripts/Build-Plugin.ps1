[CmdletBinding()]
param([Parameter(Mandatory)][string]$Project)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Use-Environment.ps1')
$workspace = Split-Path $PSScriptRoot -Parent
$projectPath = (Resolve-Path -LiteralPath $Project).ProviderPath
if ([IO.Path]::GetExtension($projectPath) -ne '.csproj') { throw 'Specify the plugin .csproj file.' }
[xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw
$reference = $projectXml.SelectSingleNode('//PackageReference[@Include="SwiftlyS2.CS2"]')
if (!$reference -or $reference.Version -match '\*|\[|\]|\(|\)' -or [string]::IsNullOrWhiteSpace($reference.Version)) {
    throw 'Pin SwiftlyS2.CS2 to one exact package version before publishing.'
}
$projectFolder = Split-Path $projectPath -Parent
$pluginId = [IO.Path]::GetFileNameWithoutExtension($projectPath)
$addonFolder = Join-Path $workspace ('Addons/' + $pluginId)
$expectedProject = Join-Path $addonFolder ('Rohskripte/' + $pluginId + '.csproj')
if (![string]::Equals($projectPath, [IO.Path]::GetFullPath($expectedProject), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build projects from Addons/<Name>/Rohskripte/<Name>.csproj.'
}
$lockPath = Join-Path $projectFolder 'packages.lock.json'
$restoreArgs = @('restore',$projectPath,'--configfile',(Join-Path $workspace 'NuGet.Config'))
if (Test-Path $lockPath) { $restoreArgs += '--locked-mode' } else { $restoreArgs += '--use-lock-file' }
& dotnet @restoreArgs
if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
$buildFolder = Join-Path $env:LPLK_BUILD_ROOT $pluginId
$publishFolder = Join-Path $buildFolder 'publish'
# Clear only this component's reproducible publish tree so removed source/resources
# cannot leak into the next ZIP. The previous delivered ZIP remains on the NAS.
if (Test-Path -LiteralPath $publishFolder) {
    $resolvedPublish = (Resolve-Path -LiteralPath $publishFolder).ProviderPath
    $expectedPublish = [IO.Path]::GetFullPath($publishFolder)
    $localRoot = [IO.Path]::GetFullPath($env:LPLK_BUILD_ROOT).TrimEnd('\')
    if ($resolvedPublish -ne $expectedPublish -or !$resolvedPublish.StartsWith($localRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe publish cleanup target.' }
    $entries = @(Get-Item -LiteralPath $resolvedPublish -Force) + @(Get-ChildItem -LiteralPath $resolvedPublish -Force -Recurse)
    if (@($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Linked publish paths are not cleaned.' }
    Remove-Item -LiteralPath $resolvedPublish -Recurse -ErrorAction Stop
}
& dotnet publish $projectPath -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Plugin compilation failed.' }
$packagePath = Join-Path $buildFolder ('bin/Release/' + $pluginId + '.zip')
if (!(Test-Path -LiteralPath $packagePath -PathType Leaf)) { throw 'The build did not produce the expected current plugin ZIP.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    if (!$zip.GetEntry($pluginId + '/' + $pluginId + '.dll')) { throw 'Plugin DLL missing from package.' }
    foreach ($entry in $zip.Entries) {
        if (!$entry.FullName.StartsWith($pluginId + '/', [StringComparison]::Ordinal) -or $entry.FullName.Contains('..') -or $entry.FullName.Contains('\')) { throw 'Unexpected path in plugin package.' }
    }
} finally { $zip.Dispose() }
# Stable names replace the previous successful package; never collect dated copies.
$artifactFolder = Join-Path $addonFolder 'Build'
New-Item -ItemType Directory -Path $artifactFolder -Force | Out-Null
$zipName = $pluginId + '.zip'
$destination = Join-Path $artifactFolder $zipName
$incoming = Join-Path $artifactFolder ($zipName + '.incoming')
$hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
Copy-Item -LiteralPath $packagePath -Destination $incoming -Force
if ((Get-FileHash -LiteralPath $incoming -Algorithm SHA256).Hash -ne $hash) { throw 'Package copy verification failed; previous ZIP retained.' }
Move-Item -LiteralPath $incoming -Destination $destination -Force
$manifest = [ordered]@{builtAt=[DateTimeOffset]::UtcNow.ToString('o');project=('Addons/' + $pluginId + '/Rohskripte/' + $pluginId + '.csproj');apiVersion=$reference.Version;sdk=(& dotnet --version);configuration='Release';deploymentStatus='not-deployed';files=@([ordered]@{name=$zipName;sha256=$hash})}
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $artifactFolder 'build-manifest.json') -Encoding utf8
Write-Output "Plugin packages: $artifactFolder"
