# AutoPlot – plot every layout in a folder of drawings

Plots **every paper-space layout** (never Model) of every `.dwg` in a folder.
Each layout plots with **its own page setup** (device, paper size, scale, CTB),
unless you force one named page setup for all.

| File | Purpose |
|---|---|
| `PlotAllLayouts.lsp` | The plotting routine. Also adds a `PLOTALL` command inside Civil 3D. |
| `Plot-Folder.ps1` | Runs the whole folder through the Core Console that comes with Civil 3D 2026. **Recommended.** |
| `PlotFolder.bat` | Double-click/drag-a-folder launcher for `Plot-Folder.ps1`. |
| `BatchSaveUtility-PlotAll.scr` | Script for the Autodesk Batch Save Utility. |

Copy the whole folder somewhere permanent, e.g. `C:\CAD\AutoPlot\`.

## Output

* **File devices** (`DWG To PDF.pc3`, `AutoCAD PDF (…).pc3`, DWF6/DWFx, PNG/JPG `.pc3`)
  write `<OutputFolder>\<DrawingName>-<LayoutName>.pdf`. An existing file with the same name is overwritten.
* **Real printers/plotters** get the job sent straight to them.
* **Skipped:** empty layouts (e.g. an unused `Layout1`), layouts whose page setup is `None`, and layouts
  set to Windows printers like *Microsoft Print to PDF*/*Adobe PDF*. Those printers open a Save
  dialog, which would hang an unattended run. Change those page setups to `DWG To PDF.pc3`.
* Nothing is saved back to the drawings.

## Option 1 – Civil 3D 2026 Core Console (recommended)

Drag the drawings folder onto `PlotFolder.bat`, or from PowerShell:

```powershell
cd C:\CAD\AutoPlot
.\Plot-Folder.ps1 -Folder "P:\Job123\Sheets"
```

Useful options:

```powershell
# Include subfolders, put PDFs somewhere else
.\Plot-Folder.ps1 -Folder "P:\Job123\Sheets" -Recurse -OutputFolder "P:\Job123\Issued\PDF"

# Ignore each layout's page setup and use one from the company template for everything
.\Plot-Folder.ps1 -Folder "P:\Job123\Sheets" -PageSetup "PDF 22x34" -PageSetupFile "C:\CAD\Templates\Company.dwt"

# Also plot empty layouts
.\Plot-Folder.ps1 -Folder "P:\Job123\Sheets" -IncludeEmptyLayouts
```

* Runs `accoreconsole.exe` with `/product C3D` so Civil 3D objects (alignments, profiles, labels…) plot properly.
  It finds `C:\Program Files\Autodesk\AutoCAD 2026\accoreconsole.exe` automatically.
  Use `-AccoreConsole <path>` if yours is installed elsewhere.
* No Civil 3D window opens. You don't need Civil 3D running, and you can keep working while it plots.
* `plot-results-<timestamp>.csv` in the output folder lists every layout with PLOTTED / SENT TO PLOTTER / SKIPPED / FAILED.
  Full console output for each drawing is in `_logs\<timestamp>\`.
* A drawing that takes longer than `-TimeoutMinutes` (default 15) is killed and logged as FAILED.
* If you get *"running scripts is disabled"*, use `PlotFolder.bat`, which bypasses the policy for this run only.

## Option 2 – Autodesk Batch Save Utility (standalone)

1. Edit `BatchSaveUtility-PlotAll.scr` and change the `load` path to where you put `PlotAllLayouts.lsp`.
   Use forward slashes.
2. In the Batch Save Utility, add the drawings or the folder.
3. Under the script option, select `BatchSaveUtility-PlotAll.scr`.
4. Run it. PDFs go to `<each drawing's folder>\Plots\`.
   To use a fixed folder, change the first line to e.g. `(setq *plotall-outdir* "P:/Job123/PDF")`.

> The Batch Save Utility re-saves every drawing after running the script, in the format/version you pick in it.
> The plot routine itself changes nothing in the drawing, but if you don't want the files touched, use Option 1.

Add the AutoPlot folder to **Options → Files → Trusted Locations** (or the `TRUSTEDPATHS` system variable).
Otherwise the security prompt for loading an untrusted LISP can stall the batch.
`Plot-Folder.ps1` adds the folder to the trusted paths for you.

## Option 3 – inside Civil 3D (one drawing at a time)

`APPLOAD` → `PlotAllLayouts.lsp` (or add it to the Startup Suite), then type **`PLOTALL`**.
PDFs go to `<drawing folder>\Plots\`.

## Tips

* The output is only as good as the page setups. Set each layout to the right device/paper/CTB once.
  Or keep a named page setup in your template and use `-PageSetup` / `-PageSetupFile`.
* The page setup used with `-PageSetup` must be a **layout** (paper-space) page setup, not a Model one.
* Layouts plot in tab order.
