<#
.SYNOPSIS
    Plots every paper-space layout of every DWG in a folder, unattended.

.DESCRIPTION
    Runs each drawing through the AutoCAD Core Console (accoreconsole.exe) that
    is installed with Civil 3D 2026, and runs PlotAllLayouts.lsp on it.
    Model space is never plotted. Each layout uses its own page setup unless
    -PageSetup is given. Drawings are opened read-only in effect: nothing is saved.

.EXAMPLE
    .\Plot-Folder.ps1 -Folder "P:\Job123\Sheets"

.EXAMPLE
    .\Plot-Folder.ps1 -Folder "P:\Job123\Sheets" -Recurse -OutputFolder "P:\Job123\PDF"

.EXAMPLE
    # Force every layout to use the "PDF 11x17" page setup stored in the company template
    .\Plot-Folder.ps1 -Folder "P:\Job123\Sheets" -PageSetup "PDF 11x17" -PageSetupFile "C:\CAD\Templates\Company.dwt"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Folder,

    # Where PDF/DWF files go. Default: <Folder>\Plots
    [string]$OutputFolder,

    # Also process drawings in subfolders.
    [switch]$Recurse,

    # Named page setup to apply to every layout (default: each layout's own).
    [string]$PageSetup,

    # DWG/DWT to import -PageSetup from when a drawing doesn't contain it.
    [string]$PageSetupFile,

    # Also plot layouts that contain nothing (default: skipped).
    [switch]$IncludeEmptyLayouts,

    # Path to accoreconsole.exe (auto-detected for 2026 if omitted).
    [string]$AccoreConsole,

    # C3D loads the Civil 3D object enablers so Civil objects plot correctly.
    # Use '' for plain AutoCAD.
    [string]$Product = 'C3D',

    [string]$Language = 'en-US',

    # Kill the console if a single drawing takes longer than this.
    [int]$TimeoutMinutes = 15
)

$ErrorActionPreference = 'Stop'

function ConvertTo-LispString([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return 'nil' }
    return '"' + ($s -replace '\\', '/' -replace '"', '') + '"'
}

# --- Locate things ----------------------------------------------------------
$Folder = (Resolve-Path -LiteralPath $Folder).Path
$lisp   = Join-Path $PSScriptRoot 'PlotAllLayouts.lsp'
if (-not (Test-Path -LiteralPath $lisp)) { throw "PlotAllLayouts.lsp not found next to this script ($lisp)." }

if (-not $AccoreConsole) {
    $AccoreConsole = @(
        "$env:ProgramFiles\Autodesk\AutoCAD 2026\accoreconsole.exe",
        "$env:ProgramFiles\Autodesk\Autodesk Civil 3D 2026\accoreconsole.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $AccoreConsole -or -not (Test-Path -LiteralPath $AccoreConsole)) {
    throw "accoreconsole.exe not found. Pass -AccoreConsole 'C:\Program Files\Autodesk\AutoCAD 2026\accoreconsole.exe'."
}

if ($PageSetupFile) { $PageSetupFile = (Resolve-Path -LiteralPath $PageSetupFile).Path }

if (-not $OutputFolder) { $OutputFolder = Join-Path $Folder 'Plots' }
New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
$OutputFolder = (Resolve-Path -LiteralPath $OutputFolder).Path

$stamp      = Get-Date -Format 'yyyyMMdd-HHmmss'
$logDir     = Join-Path $OutputFolder "_logs\$stamp"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$resultsCsv = Join-Path $OutputFolder "plot-results-$stamp.csv"
Set-Content -LiteralPath $resultsCsv -Value '"Drawing","Layout","Device","Status","Output"' -Encoding ASCII

# --- Build the script the console runs on every drawing ---------------------
$scriptDir = $PSScriptRoot -replace '\\', '/'
$scr = Join-Path $logDir 'plotall.scr'
@"
(if (not (vl-string-search (strcase "$scriptDir") (strcase (vl-string-translate "\\" "/" (getvar "TRUSTEDPATHS"))))) (setvar "TRUSTEDPATHS" (strcat (getvar "TRUSTEDPATHS") ";$scriptDir")))
(setq *plotall-outdir* $(ConvertTo-LispString $OutputFolder))
(setq *plotall-pagesetup* $(ConvertTo-LispString $PageSetup))
(setq *plotall-pagesetup-dwg* $(ConvertTo-LispString $PageSetupFile))
(setq *plotall-include-empty* $(if ($IncludeEmptyLayouts) { 'T' } else { 'nil' }))
(setq *plotall-log* $(ConvertTo-LispString $resultsCsv))
(load $(ConvertTo-LispString $lisp))
(plotall:run)
"@ | Set-Content -LiteralPath $scr -Encoding ASCII

# --- Collect drawings -------------------------------------------------------
$drawings = Get-ChildItem -LiteralPath $Folder -Filter '*.dwg' -File -Recurse:$Recurse |
    Where-Object { $_.Extension -eq '.dwg' -and
                   -not $_.FullName.StartsWith($OutputFolder, [StringComparison]::OrdinalIgnoreCase) } |
    Sort-Object FullName

if (-not $drawings) { Write-Warning "No .dwg files found in $Folder"; return }

Write-Host "Plotting $($drawings.Count) drawing(s) from $Folder"
Write-Host "Output  : $OutputFolder"
Write-Host "Console : $AccoreConsole`n"

# --- Plot -------------------------------------------------------------------
$i = 0
foreach ($dwg in $drawings) {
    $i++
    Write-Host ("[{0}/{1}] {2}" -f $i, $drawings.Count, $dwg.FullName)

    $consoleLog = Join-Path $logDir ($dwg.BaseName + '.log')
    $argList = @('/i', "`"$($dwg.FullName)`"", '/s', "`"$scr`"")
    if ($Product)  { $argList += @('/product',  $Product) }
    if ($Language) { $argList += @('/language', $Language) }

    $proc = Start-Process -FilePath $AccoreConsole -ArgumentList $argList -NoNewWindow -PassThru `
                          -RedirectStandardOutput $consoleLog
    if (-not $proc.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        $proc.Kill()
        Write-Warning "  Timed out after $TimeoutMinutes min - killed. See $consoleLog"
        Add-Content -LiteralPath $resultsCsv -Value "`"$($dwg.FullName)`",`"*`",`"`",`"FAILED (timed out)`",`"`"" -Encoding ASCII
    }

    # accoreconsole writes UTF-16 with stray NULs; clean it up so it is readable.
    if (Test-Path -LiteralPath $consoleLog) {
        $text = [IO.File]::ReadAllText($consoleLog) -replace "`0", ''
        [IO.File]::WriteAllText($consoleLog, $text)
        $text -split "`r?`n" | Where-Object { $_ -match 'PLOTALL' } | ForEach-Object { Write-Host "  $($_.Trim())" }
    }
}

# --- Summary ----------------------------------------------------------------
$results = Import-Csv -LiteralPath $resultsCsv
$ok      = @($results | Where-Object { $_.Status -in 'PLOTTED', 'SENT TO PLOTTER' }).Count
$skipped = @($results | Where-Object { $_.Status -like 'SKIPPED*' }).Count
$failed  = @($results | Where-Object { $_.Status -like 'FAILED*' })

Write-Host "`nDone: $ok plotted, $skipped skipped, $($failed.Count) failed."
Write-Host "Results: $resultsCsv"
if ($failed) {
    Write-Warning 'Failures:'
    $failed | ForEach-Object { Write-Warning "  $($_.Drawing) [$($_.Layout)] $($_.Status)" }
}
