[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z][A-Za-z0-9_]{2,63}$')][string]$Name,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$ApiVersion = '1.4.14',
    [string]$Author = 'Server Administration'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Use-Environment.ps1')
$workspace = Split-Path $PSScriptRoot -Parent
$addonFolder = Join-Path $workspace ('Addons/' + $Name)
$destination = Join-Path $addonFolder 'Rohskripte'
if (Test-Path $addonFolder) { throw 'The addon folder already exists; no files were overwritten.' }
& dotnet new swplugin -n $Name --PluginName $Name --PluginVersion '1.0.0' --PluginAuthor $Author -o $destination
if ($LASTEXITCODE -ne 0) { throw 'Plugin template creation failed.' }
$project = Join-Path $destination ($Name + '.csproj')
[xml]$xml = Get-Content -LiteralPath $project -Raw
foreach ($outputProperty in @($xml.SelectNodes('//PropertyGroup/OutputPath | //PropertyGroup/PublishDir'))) {
    $outputProperty.SetAttribute('Condition', "'`$(LPLK_BUILD_ROOT)' == ''")
}
$reference = $xml.SelectSingleNode('//PackageReference[@Include="SwiftlyS2.CS2"]')
if (!$reference) { throw 'The template did not contain the expected SwiftlyS2.CS2 package reference.' }
$reference.SetAttribute('Version',$ApiVersion)
$propertyGroup = $xml.SelectSingleNode('//PropertyGroup')
$lockProperty = $xml.CreateElement('RestorePackagesWithLockFile')
$lockProperty.InnerText = 'true'
[void]$propertyGroup.AppendChild($lockProperty)
$xml.Save($project)
New-Item -ItemType Directory -Path (Join-Path $addonFolder 'Build') -Force | Out-Null
Write-Output "Created $project with SwiftlyS2 API $ApiVersion. Confirm server compatibility before deployment."
