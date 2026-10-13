<#
.SYNOPSIS
  Tests and builds the Profile View Stacker plugin and assembles dist\ProfileStacker.bundle.
.PARAMETER Install
  Also copies the bundle to %APPDATA%\Autodesk\ApplicationPlugins so Civil 3D 2026 autoloads it.
.EXAMPLE
  .\build.ps1 -Install
#>
param([switch]$Install, [string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

dotnet test tests\ProfileStacker.Core.Tests -c $Configuration
if ($LASTEXITCODE) { throw "Tests failed" }

dotnet build src\ProfileStacker\ProfileStacker.csproj -c $Configuration
if ($LASTEXITCODE) { throw "Build failed" }

$out = "src\ProfileStacker\bin\$Configuration\net8.0-windows"
$bundle = "dist\ProfileStacker.bundle"
if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
New-Item -ItemType Directory "$bundle\Contents" | Out-Null
Copy-Item "bundle\ProfileStacker.bundle\PackageContents.xml" $bundle
Copy-Item "$out\ProfileStacker.dll", "$out\ProfileStacker.Core.dll" "$bundle\Contents"
Write-Host "Bundle ready: $bundle"

if ($Install) {
    $target = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\ProfileStacker.bundle"
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Copy-Item $bundle $target -Recurse
    Write-Host "Installed to $target - restart Civil 3D 2026, then type TTWPROFSTACK."
}
