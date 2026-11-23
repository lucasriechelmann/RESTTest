#Requires -Version 7
<#
.SYNOPSIS
    Builds the RESTTest release files: a Windows installer, a portable zip and SHA-256 checksums.

.DESCRIPTION
    Runs the tests, publishes a self-contained win-x64 build, zips it, and compiles
    build/installer.iss with Inno Setup 6. Output goes to artifacts/<version>/.
    The version comes from <Version> in RESTTest/RESTTest.csproj; tag the release v<Version>.

.PARAMETER SkipTests
    Skip the test run (for a quick local rebuild only; never for a published release).

.PARAMETER Iscc
    Path to ISCC.exe. Found automatically in the usual per-user and machine-wide locations.

.EXAMPLE
    pwsh build/release.ps1
#>
param(
    [switch]$SkipTests,
    [string]$Iscc
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'RESTTest/RESTTest.csproj'

[xml]$csproj = Get-Content $project
$version = ($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw "No <Version> found in $project." }

$out = Join-Path $root "artifacts/$version"
$publish = Join-Path $out 'publish'
Write-Host "RESTTest $version -> $out"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $publish | Out-Null

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'RESTTest.Tests') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; no release built.' }
}

# Self-contained: users need no .NET runtime. Not single-file: WPF native libraries load more
# reliably from a folder, and the installer compresses the folder anyway.
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:DebugType=None -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$zip = Join-Path $out "RESTTest-$version-portable-x64.zip"
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal

if (-not $Iscc) {
    $Iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Iscc) { throw 'Inno Setup 6 (ISCC.exe) not found. Install it or pass -Iscc.' }

& $Iscc /Q "/DAppVersion=$version" "/DSourceDir=$publish" "/DOutputDir=$out" (Join-Path $PSScriptRoot 'installer.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }

$files = Get-ChildItem $out -File | Where-Object { $_.Extension -in '.exe', '.zip' }
$sums = $files | ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)" }
Set-Content -Path (Join-Path $out 'SHA256SUMS.txt') -Value $sums -Encoding utf8NoBOM

Write-Host ''
Get-ChildItem $out -File | ForEach-Object { '{0,-45} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) } | Write-Host
