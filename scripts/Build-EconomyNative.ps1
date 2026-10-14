[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Use-Environment.ps1')
$workspace=Split-Path $PSScriptRoot -Parent
$nativeRoot=Join-Path $env:LPLK_BUILD_ROOT 'EconomyNative'
New-Item -ItemType Directory -Path $nativeRoot -Force | Out-Null
$zigUrl='https://ziglang.org/download/0.14.1/zig-x86_64-windows-0.14.1.zip'
$zigHash='554F5378228923FFD558EAC35E21AF020C73789D87AFEABF4BFD16F2E6FEED2C'
$sqliteUrl='https://www.sqlite.org/2026/sqlite-amalgamation-3530400.zip'
$sqliteHash='1E71DDF93849C6A6ECF58B827C0692073D2DD7EE40196158068F7B29F422E87D'
function Get-CheckedArchive([string]$Url,[string]$Path,[string]$Hash) {
    if(!(Test-Path -LiteralPath $Path)){Invoke-WebRequest -Uri $Url -OutFile $Path}
    if((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Hash){throw ('Download checksum mismatch: '+$Path)}
}
$zigArchive=Join-Path $nativeRoot 'zig.zip'
$sqliteArchive=Join-Path $nativeRoot 'sqlite-amalgamation.zip'
Get-CheckedArchive $zigUrl $zigArchive $zigHash
Get-CheckedArchive $sqliteUrl $sqliteArchive $sqliteHash
$zig=Join-Path $nativeRoot 'zig-x86_64-windows-0.14.1/zig.exe'
if(!(Test-Path -LiteralPath $zig)){Expand-Archive -LiteralPath $zigArchive -DestinationPath $nativeRoot}
$source=Join-Path $nativeRoot 'sqlite-amalgamation-3530400/sqlite3.c'
if(!(Test-Path -LiteralPath $source)){Expand-Archive -LiteralPath $sqliteArchive -DestinationPath $nativeRoot}
$output=Join-Path $nativeRoot 'libe_sqlite3.so'
# CS2's runtime can expose an older glibc than the Debian host. Build this
# dependency for glibc 2.17; never replace a system library to load a plugin.
$previousCache=$env:ZIG_GLOBAL_CACHE_DIR
try {
    $env:ZIG_GLOBAL_CACHE_DIR=Join-Path $nativeRoot 'zig-cache'
    & $zig cc -target x86_64-linux-gnu.2.17 -shared -fPIC -O2 -DSQLITE_THREADSAFE=1 -DSQLITE_ENABLE_COLUMN_METADATA -DSQLITE_ENABLE_FTS5 -DSQLITE_ENABLE_RTREE -DSQLITE_ENABLE_MATH_FUNCTIONS -DSQLITE_ENABLE_DBSTAT_VTAB $source -o $output -lm -ldl -lpthread
    if($LASTEXITCODE -ne 0){throw 'SQLite native compilation failed'}
} finally { $env:ZIG_GLOBAL_CACHE_DIR=$previousCache }
$bytes=[IO.File]::ReadAllBytes($output)
if($bytes[0] -ne 127 -or $bytes[1] -ne 69 -or $bytes[2] -ne 76 -or $bytes[3] -ne 70){throw 'Not an ELF library'}
$versions=@([regex]::Matches([Text.Encoding]::ASCII.GetString($bytes),'GLIBC_([0-9]+\.[0-9]+(?:\.[0-9]+)?)')|ForEach-Object {$_.Groups[1].Value}|Sort-Object -Unique)
if(!$versions.Count -or @($versions|Where-Object {[version]$_ -gt [version]'2.17'}).Count){throw 'Unsupported glibc requirement'}
$destination=Join-Path $workspace 'Addons/LPLK_Economy/Rohskripte/native'
New-Item -ItemType Directory -Path $destination -Force|Out-Null
Copy-Item -LiteralPath $output -Destination (Join-Path $destination 'libe_sqlite3.so') -Force
$hash=(Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
if((Get-FileHash -LiteralPath (Join-Path $destination 'libe_sqlite3.so')).Hash -ne $hash){throw 'Native copy checksum mismatch'}
[ordered]@{sqliteVersion='3.53.4';sourceUrl=$sqliteUrl;sourceArchiveSha256=$sqliteHash;compilerUrl=$zigUrl;compilerArchiveSha256=$zigHash;target='x86_64-linux-gnu.2.17';glibcVersions=$versions;sha256=$hash}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $destination 'manifest.json') -Encoding utf8
Write-Output ('SQLite 3.53.4; target glibc 2.17; SHA-256 '+$hash)
