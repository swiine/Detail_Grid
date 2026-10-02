<#
SplitDrawings.ps1

Makes one copy of a drawing folder per DWG.

Going down the list of DWG files directly in the source folder, for each one
it copies the whole folder, keeping every subfolder (and everything in them,
DWGs included) and every non-DWG file, but deletes the other top-level DWG
files, keeping only:
  - that one drawing, and
  - the frame (TTW_stdCountry_A1L_Frame.dwg)
.bak files are left out of the copies. The copy is named after the drawing it kept, and each copy is also
zipped on its own (D-101 -> D-101.zip). The source folder is never changed.

Usage: double-click SplitDrawings.bat and pick the folder
(or drag the folder onto SplitDrawings.bat), or:
  powershell -ExecutionPolicy Bypass -File SplitDrawings.ps1 -Source "C:\Jobs\Details"
  ... -Out "C:\Jobs\Split"        # where the copies go (default: "<Source>_Split" next to it)
  ... -WhatIf                     # list what would be made without copying
  ... -Overwrite                  # replace copies that already exist
  ... -List "drawings.txt"        # only split the drawings named in this file (one per line)
#>
param(
    [string]$Source,
    [string]$Out,
    [string]$Frame = "TTW_stdCountry_A1L_Frame",
    [string]$List,
    [switch]$Overwrite,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

# No folder given (double-clicked the .bat): ask for one.
if (-not $Source) {
    Add-Type -AssemblyName System.Windows.Forms
    $picker = New-Object System.Windows.Forms.FolderBrowserDialog
    $picker.Description = "Pick the drawing folder to split"
    if ($picker.ShowDialog() -ne "OK") { Write-Host "No folder picked."; return }
    $Source = $picker.SelectedPath
}

$Source = (Resolve-Path -LiteralPath $Source).Path.TrimEnd('\')
if (-not $Out) { $Out = "$Source`_Split" }
$Out = [System.IO.Path]::GetFullPath($Out).TrimEnd('\')

if (($Out + '\').StartsWith($Source + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The output folder can't be inside the source folder: $Out"
}

$folders  = @(Get-ChildItem -LiteralPath $Source -Recurse -Directory)
# .bak files are never copied.
$files    = @(Get-ChildItem -LiteralPath $Source -Recurse -File | Where-Object { $_.Extension -ne '.bak' })
# Only DWGs directly in the source folder are split; subfolder DWGs are copied as-is.
$isTopDwg = { $_.Extension -eq '.dwg' -and $_.DirectoryName.TrimEnd('\') -eq $Source }
$dwgs     = @($files | Where-Object $isTopDwg)
$nonDwgs  = @($files | Where-Object { -not (& $isTopDwg) })
$frameDwg = @($dwgs | Where-Object { $_.BaseName -eq $Frame })
$drawings = @($dwgs | Where-Object { $_.BaseName -ne $Frame } | Sort-Object FullName)

# Only the drawings named in the list file, if one was given.
if ($List) {
    $wanted = @(Get-Content -LiteralPath $List |
        ForEach-Object { ($_.Trim() -replace '\.dwg$', '') } |
        Where-Object { $_ })
    foreach ($w in $wanted) {
        if (-not ($drawings | Where-Object { $_.BaseName -eq $w })) {
            Write-Warning "In the list but not found: $w"
        }
    }
    $drawings = @($drawings | Where-Object { $wanted -contains $_.BaseName })
}

# A frame in a subfolder is copied along with that subfolder.
if (-not ($files | Where-Object { $_.Extension -eq '.dwg' -and $_.BaseName -eq $Frame })) {
    Write-Warning "$Frame.dwg was not found in $Source - copies will only have 1 DWG."
}
if ($drawings.Count -eq 0) { throw "No drawings found in $Source" }

Write-Host "Found $($drawings.Count) drawing(s) in $Source"
Write-Host "Copies go to $Out`n"

function Get-Relative($item) { $item.FullName.Substring($Source.Length + 1) }

$made = 0; $skipped = 0; $failed = @()
$i = 0
foreach ($dwg in $drawings) {
    $i++

    $name = $dwg.BaseName
    $dest = Join-Path $Out $name
    $zip  = "$dest.zip"
    Write-Host "[$i/$($drawings.Count)] $(Get-Relative $dwg) -> $dest"

    if ($WhatIf) { continue }
    if ((Test-Path -LiteralPath $dest) -or (Test-Path -LiteralPath $zip)) {
        if (-not $Overwrite) {
            Write-Host "    exists, skipping (use -Overwrite to replace)"
            $skipped++
            continue
        }
        if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
        if (Test-Path -LiteralPath $zip)  { Remove-Item -LiteralPath $zip -Force }
    }

    try {
        # Same folder structure, including empty subfolders.
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
        foreach ($f in $folders) {
            New-Item -ItemType Directory -Path (Join-Path $dest (Get-Relative $f)) -Force | Out-Null
        }
        # Everything except the other top-level drawings.
        foreach ($f in ($nonDwgs + $frameDwg + $dwg)) {
            Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $dest (Get-Relative $f)) -Force
        }
        # Zip this copy on its own; the zip opens to a folder named after the drawing.
        [System.IO.Compression.ZipFile]::CreateFromDirectory($dest, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)
        $made++
    }
    catch {
        Write-Host "    FAILED: $_" -ForegroundColor Red
        $failed += $dwg.FullName
    }
}

Write-Host "`nDone. Made $made, skipped $skipped, failed $($failed.Count)."
foreach ($f in $failed) { Write-Host "  failed: $f" -ForegroundColor Red }
