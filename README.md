# Detail_Grid — Pavement Build-up for Civil 3D 2026

A Civil 3D 2026 (AutoCAD 2026, .NET 8) plugin. You type in a pavement build-up and it draws the
section detail for you: hatched courses, leader labels, thickness dimensions, break lines, subgrade
and a title with the scale and total construction depth.

```
 40mm ┬ ░░░░░░░░░░░░░░░░░░░░░░░░░•───── 40mm SMA 10 SURFACE COURSE
 60mm ┼ ░░░░░░░░░░░░░░░░░░░░░░░░░•───── 60mm AC 20 DENSE BIN 40/60 BINDER COURSE
150mm ┼ ////////////////////////•───── 150mm AC 32 DENSE BASE 40/60 BASE COURSE
225mm ┼ ∘ ∘ ∘ ∘ ∘ ∘ ∘ ∘ ∘ ∘ ∘ ∘ •───── 225mm TYPE 1 SUB-BASE
      ┴ ━━━━━━━━━━━━━━━━━━━━━━━━•───── GEOTEXTILE SEPARATOR
        \\\\\\\\\\\\\\\\\\\\\\\\•───── SUBGRADE
        TYPE A - CARRIAGEWAY  ·  SCALE 1:10  ·  TOTAL CONSTRUCTION DEPTH = 475mm
```

## Commands

| Command | What it does |
|---|---|
| `PAVEBUILDUP` | Opens the dialog: enter the courses, pick or save presets, change the settings, check the live preview, then click **Draw detail** and pick the top-left corner. |
| `PAVEQUICK` | Command-line version. Type the build-up (or a preset name), give it a name, pick a point. It uses the settings you last used in the dialog. |

### Entering a build-up

All thicknesses are in **millimetres**, listed **top (surface) to bottom**. In the quick-entry box or
at the `PAVEQUICK` prompt, put one course per item, separated by `;`:

```
40 SMA 10 surface course; 60 AC 20 dense bin 40/60 binder course; 150 AC 32 dense base 40/60 base course; 225 Type 1 sub-base; 0 Geotextile separator
```

- `40mm SMA`, `40 SMA` and `SMA 40mm` all work. A spaced slash (`a / b`), `|` or a new line also separates courses. `40/60` bitumen grades are left alone.
- A **0mm** course (geotextile, DPM, slip membrane) is drawn as a heavy line with a label.
- The hatch is chosen from the description: asphalt surface and binder courses → `AR-SAND`, asphalt base → `ANSI31`, Type 1 / granular / capping → `GRAVEL`, concrete / CBGM / PQC → `AR-CONC`, block paving → `ANSI37`, bedding sand → `AR-SAND`, soils → `EARTH`. You can override the pattern, scale and angle for each course in the table (`NONE` leaves a course unhatched).

### Settings (dialog → *Drawing*)

| Setting | Default | Notes |
|---|---|---|
| Scale 1: | 10 | Detail scale. Text, arrows, leaders and hatch density are sized so they plot correctly at this scale. |
| Width | 1000mm | Width of the section strip. |
| Text height | 2.5mm | Plotted height. |
| Drawing units | AUTO | Reads `INSUNITS`, so it works in mm or metre drawings. You can also force MM/CM/M. |
| Hatch scale × | 1.0 | Global multiplier. If hatches look too dense or too sparse in your template, change this once and it is remembered. |
| Create as block | on | The whole detail becomes one block (`PAV_<name>`) that is easy to move or copy to a sheet. |
| Layer prefix | `PAV-` | Draws onto `PAV-OUTLINE`, `PAV-HATCH`, `PAV-TEXT` and `PAV-DIM`, creating them if needed. |

The geometry is drawn at **true size** in model space (a 40mm course is 40mm tall in a mm drawing), so
put a 1:10 viewport over it on your sheet. Dimensions use the current dimension style with `DIMSCALE`
set to match the detail scale, and text uses the current text style.

Presets and your last settings are saved to `%APPDATA%\PavementBuildup\presets.json`. Four example
build-ups (flexible carriageway, footway, block paving, rigid concrete) are included the first time
you open it.

## Install

### Option 1: build on your PC (needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0))

```powershell
git clone https://github.com/swiine/detail_grid
cd detail_grid
.\build.ps1 -Install
```

This runs the tests, builds the plugin, and copies `PavementBuildup.bundle` to
`%APPDATA%\Autodesk\ApplicationPlugins`. Restart Civil 3D 2026 and it loads automatically.

### Option 2: download from CI

Every push builds the bundle on GitHub Actions (**Actions → build → artifact `PavementBuildup.bundle`**).
Unzip it and copy the `PavementBuildup.bundle` folder to `%APPDATA%\Autodesk\ApplicationPlugins`.

### Option 3: NETLOAD

Run `NETLOAD` in Civil 3D and choose `PavementBuildup.dll`. `PavementBuildup.Core.dll` must be in the same folder.

> If Windows blocks a downloaded DLL, right-click it → Properties → **Unblock**. Civil 3D may also
> ask you to trust the folder (`TRUSTEDPATHS`) the first time it loads.

## Project layout

```
src/PavementBuildup.Core     Models, build-up parser, hatch library, layout engine, presets (no AutoCAD refs)
src/PavementBuildup          Civil 3D / AutoCAD 2026 plugin: commands, dialog + preview, CAD drawer
tests/...Core.Tests          xUnit tests for the core
bundle/                      Autoloader PackageContents.xml (R25.1 = 2026)
build.ps1                    Test, build, assemble dist\PavementBuildup.bundle, optional -Install
```

The plugin references the AutoCAD 2026 API from NuGet (`AutoCAD.NET` 25.1.0, compile-time only), so you
do not need Civil 3D installed to build it. It only uses the AutoCAD API, so it also runs in plain
AutoCAD 2026.
