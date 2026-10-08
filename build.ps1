<#
.SYNOPSIS
  Builds the Pavement Build-up plugin and assembles dist\PavementBuildup.bundle.
.PARAMETER Install
  Also copies the bundle to %APPDATA%\Autodesk\ApplicationPlugins so Civil 3D 2026 autoloads it.
.EXAMPLE
  .\build.ps1 -Install
#>
param([switch]$Install, [string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

dotnet test tests\PavementBuildup.Core.Tests -c $Configuration
if ($LASTEXITCODE) { throw "Tests failed" }

dotnet build src\PavementBuildup\PavementBuildup.csproj -c $Configuration
if ($LASTEXITCODE) { throw "Build failed" }

$out = "src\PavementBuildup\bin\$Configuration\net8.0-windows"
$bundle = "dist\PavementBuildup.bundle"
if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
New-Item -ItemType Directory "$bundle\Contents" | Out-Null
Copy-Item "bundle\PavementBuildup.bundle\PackageContents.xml" $bundle
Copy-Item "$out\PavementBuildup.dll", "$out\PavementBuildup.Core.dll" "$bundle\Contents"
Copy-Item "standards" "$bundle\Contents\standards" -Recurse

# Interpret with AI: published with its own dependencies into Contents\ai (loaded in an isolated context).
dotnet publish src\PavementBuildup.Ai\PavementBuildup.Ai.csproj -c $Configuration -o "$bundle\Contents\ai"
if ($LASTEXITCODE) { throw "AI component publish failed" }
Get-ChildItem "$bundle\Contents\ai" -Filter *.pdb | Remove-Item
Copy-Item "AI Pavement Build-up Prompt.txt" "dist"
Write-Host "Bundle ready: $bundle"

if ($Install) {
    $target = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\PavementBuildup.bundle"
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Copy-Item $bundle $target -Recurse
    Write-Host "Installed to $target - restart Civil 3D 2026, then run PAVEBUILDUP."
}
