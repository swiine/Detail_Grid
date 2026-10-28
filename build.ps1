<#
.SYNOPSIS
  Tests ET Walls and builds the release folder dist\ET_Wall_Tool V1.0.0 (and a .zip of it).
.DESCRIPTION
  ET_Wall_Tool V1.0.0\
    _support\_Standards\TTW.json          company CAD standard (layers, text, hatches)
    Standalone Tool\ET Walls.exe          the Windows app (.NET 8 Desktop Runtime, comes with Civil 3D 2026)
    Standalone Tool\ET Walls Standalone - Instructions.txt
    Standalone Tool\_Exports\             everything the app saves: _Exports\date\job\wall name
.PARAMETER SelfTest
  Also runs the app's self test (opens the window, designs every wall type, writes reports and DXFs).
.EXAMPLE
  .\build.ps1 -SelfTest
#>
param([switch]$SelfTest, [string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

dotnet test tests\ETWalls.Core.Tests -c $Configuration
if ($LASTEXITCODE) { throw "Tests failed" }

$publish = "dist\publish"
dotnet publish src\ETWalls.Standalone\ETWalls.Standalone.csproj -c $Configuration -o $publish
if ($LASTEXITCODE) { throw "Standalone publish failed" }

$release = "dist\ET_Wall_Tool V1.0.0"
if (Test-Path $release) { Remove-Item $release -Recurse -Force }
New-Item -ItemType Directory "$release\_support\_Standards", "$release\Standalone Tool\_Exports" | Out-Null
Copy-Item "standards\TTW.json" "$release\_support\_Standards"
Copy-Item "$publish\ET Walls.exe", "ET Walls Standalone - Instructions.txt" "$release\Standalone Tool"
Compress-Archive -Path $release -DestinationPath "$release.zip" -Force
Write-Host "Release ready: $release.zip"

if ($SelfTest) {
    $out = Join-Path $PSScriptRoot "dist\selftest"
    $p = Start-Process -FilePath "$release\Standalone Tool\ET Walls.exe" -ArgumentList "--selftest", "`"$out`"" -Wait -PassThru
    Get-Content "$out\selftest.log"
    if ($p.ExitCode -ne 0) { throw "Self test failed (exit code $($p.ExitCode))" }
    Write-Host "Self test passed: $out"
}
