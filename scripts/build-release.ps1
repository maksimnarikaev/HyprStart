#Requires -Version 5.1
<#
.SYNOPSIS
    Builds a portable, self-contained single-file HyprStart release.

.DESCRIPTION
    Publishes HyprStart.App for win-x64 with the .NET runtime bundled, then packs
    the result into dist\HyprStart-win-x64.zip. The zip contains HyprStart.exe plus
    the few files WPF needs at runtime, so the archive runs on a machine without
    any .NET installation.

.PARAMETER Configuration
    Build configuration. Release by default.

.PARAMETER Runtime
    Target runtime identifier. win-x64 by default.

.PARAMETER Clean
    Remove dist\ and the app's bin\/obj\ before publishing.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-release.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-release.ps1 -Clean
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [switch] $Clean
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\HyprStart.App\HyprStart.App.csproj'
$distDir = Join-Path $root 'dist'
$publishDir = Join-Path $distDir $Runtime
$zipPath = Join-Path $distDir "HyprStart-$Runtime.zip"

function Write-Step([string] $text) {
    Write-Host ''
    Write-Host "==> $text" -ForegroundColor Cyan
}

# --- проверки окружения ---------------------------------------------------
if (-not (Test-Path $project)) {
    throw "Project not found: $project"
}

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)
if (-not $dotnet) {
    throw 'dotnet CLI not found in PATH. Install the .NET 8 SDK: https://dotnet.microsoft.com/download'
}

Write-Step "dotnet $(dotnet --version)"

# --- очистка --------------------------------------------------------------
if ($Clean) {
    Write-Step 'Cleaning dist and build output'
    foreach ($path in @($distDir, (Join-Path $root 'src\HyprStart.App\bin'), (Join-Path $root 'src\HyprStart.App\obj'))) {
        if (Test-Path $path) { Remove-Item -Recurse -Force $path }
    }
}

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

# --- публикация -----------------------------------------------------------
Write-Step "Publishing self-contained single-file ($Runtime, $Configuration)"
$publishArgs = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none',
    '-p:EnableCompressionInSingleFile=true',
    '-o', $publishDir,
    '--nologo'
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $publishDir 'HyprStart.exe'
if (-not (Test-Path $exePath)) {
    throw "Publish did not produce $exePath"
}

# --- упаковка -------------------------------------------------------------
Write-Step "Packing $zipPath"
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

$exeSize = [math]::Round((Get-Item $exePath).Length / 1MB, 1)
$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)

Write-Host ''
Write-Host 'Release ready' -ForegroundColor Green
Write-Host "  exe : $exePath ($exeSize MB)"
Write-Host "  zip : $zipPath ($zipSize MB)"
Write-Host ''
Write-Host 'The archive is portable: unpack it anywhere and run HyprStart.exe.'
