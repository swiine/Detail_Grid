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
New-Item -ItemType Directory "$bundle\Contents\_support\_Standards" -Force | Out-Null
Copy-Item "standards\TTW.json", "standards\TTW_Pave_Temp.dwg" "$bundle\Contents\_support\_Standards"

dotnet publish src\PavementBuildup.Ai\PavementBuildup.Ai.csproj -c $Configuration -o "$bundle\Contents\ai"
if ($LASTEXITCODE) { throw "AI component publish failed" }
Get-ChildItem "$bundle\Contents\ai" -Filter *.pdb | Remove-Item
Copy-Item "AI Pavement Build-up Prompt.txt", "TTW_Pvmt - Instructions.txt" "dist"
Write-Host "Bundle ready: $bundle"

$standalone = "dist\TTW Pavement Standalone"
dotnet publish src\PavementBuildup.Standalone\PavementBuildup.Standalone.csproj -c $Configuration -o $standalone
if ($LASTEXITCODE) { throw "Standalone publish failed" }
Get-ChildItem $standalone -Filter *.pdb | Remove-Item
Copy-Item "TTW Pavement Standalone - Instructions.txt", "AI Pavement Build-up Prompt.txt" $standalone
Write-Host "Standalone app ready: $standalone"

$version = ([xml](Get-Content "Directory.Build.props")).Project.PropertyGroup.Version
$release = "dist\TTW_Pave_Tool V$version"
if (Test-Path $release) { Remove-Item $release -Recurse -Force }
New-Item -ItemType Directory "$release\Civil3D Tool", "$release\_support\_Standards", "$release\Standalone Tool\_Exports" | Out-Null
Copy-Item $bundle "$release\Civil3D Tool" -Recurse
Copy-Item "TTW_Pvmt - Instructions.txt" "$release\Civil3D Tool"
Copy-Item "AI Pavement Build-up Prompt.txt" "$release\_support"
Copy-Item "standards\TTW.json", "standards\TTW_Pave_Temp.dwg", "standards\TTW_Rates.json", "standards\TTW_JointRules.json", "standards\TTW_TimeSaved.json" "$release\_support\_Standards"
@{ Version = $version; Date = (Get-Date -Format "yyyy-MM-dd"); Notes = "" } | ConvertTo-Json | Set-Content "$release\_support\version.json" -Encoding UTF8
@{ ReleaseFolder = "" } | ConvertTo-Json | Set-Content "$release\_support\update.json" -Encoding UTF8
Copy-Item "$standalone\TTW Pavement.exe", "src\PavementBuildup.Standalone\TTW Pavement - Training.cmd", "TTW Pavement Standalone - Instructions.txt" "$release\Standalone Tool"
Compress-Archive -Path $release -DestinationPath "$release.zip" -Force
Write-Host "Release ready: $release.zip"

if ($Install) {
    $target = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\PavementBuildup.bundle"
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Copy-Item $bundle $target -Recurse
    Write-Host "Installed to $target - restart Civil 3D 2026, then type TTWPVMT."
}
