<#
    build.ps1 - one-shot build for FieldJobs

    Steps:
      1. dotnet publish the WPF app as a self-contained win-x64 folder  -> dist\app
      2. compile the Inno Setup installer                               -> dist\FieldJobs-Setup-0.1.0.exe

    Requirements (installed once via winget):
      - .NET 8 SDK        winget install Microsoft.DotNet.SDK.8
      - Inno Setup 6      winget install JRSoftware.InnoSetup

    Usage:
      pwsh -File build.ps1            # full build + installer
      pwsh -File build.ps1 -NoInstaller
#>
param(
    [switch]$NoInstaller,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root      = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj      = Join-Path $root "src\FieldJobs\FieldJobs.csproj"
$distDir   = Join-Path $root "dist"
$appOut    = Join-Path $distDir "app"

Write-Host "== Cleaning dist ==" -ForegroundColor Cyan
if (Test-Path $distDir) { Remove-Item $distDir -Recurse -Force }
New-Item -ItemType Directory -Path $appOut -Force | Out-Null

Write-Host "== Publishing app (self-contained win-x64) ==" -ForegroundColor Cyan
dotnet publish $proj `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:SatelliteResourceLanguages=en `
    -o $appOut
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "== Published to $appOut ==" -ForegroundColor Green

if ($NoInstaller) {
    Write-Host "Skipping installer (-NoInstaller)." -ForegroundColor Yellow
    return
}

# locate ISCC.exe
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) { throw "ISCC.exe not found. Install with: winget install JRSoftware.InnoSetup" }

Write-Host "== Compiling installer with $iscc ==" -ForegroundColor Cyan
& $iscc (Join-Path $root "installer\installer.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed" }

Write-Host ""
Write-Host "== DONE ==" -ForegroundColor Green
Get-ChildItem $distDir -Filter *.exe | ForEach-Object { Write-Host "  Installer: $($_.FullName)" -ForegroundColor Green }
