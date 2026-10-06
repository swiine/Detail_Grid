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
| `PAVESTANDARD` | Edits the **CAD standard**: which layers everything goes on, what each material's hatch looks like, text and dimension styles, and the wording of labels. Also available from **Edit standard…** in the `PAVEBUILDUP` dialog. |

### Entering a build-up

All thicknesses are in **millimetres**, listed **top (surface) to bottom**. In the quick-entry box or
at the `PAVEQUICK` prompt, put one course per item, separated by `;`:

```
40 SMA 10 surface course; 60 AC 20 dense bin 40/60 binder course; 150 AC 32 dense base 40/60 base course; 225 Type 1 sub-base; 0 Geotextile separator
```

- `40mm SMA`, `40 SMA` and `SMA 40mm` all work. A spaced slash (`a / b`), `|` or a new line also separates courses. `40/60` bitumen grades are left alone.
- A **0mm** course (geotextile, DPM, slip membrane) is drawn as a heavy line with a label.
- The hatch is chosen from the description by the CAD standard's hatch rules (see below). The table shows which rule each course matched. You can override the pattern, scale and angle for a single course (`NONE` leaves it unhatched).

### Drawing settings (dialog → *Drawing*)

These change from drawing to drawing, so they live in the dialog rather than in the standard.

| Setting | Default | Notes |
|---|---|---|
| Scale 1: | 10 | Detail scale. Text, arrows, leaders and hatch density are sized so they plot correctly at this scale. |
| Width | 1000mm | Width of the section strip. |
| Drawing units | AUTO | Reads `INSUNITS`, so it works in mm or metre drawings. You can also force MM/CM/M. |
| Hatch scale × | 1.0 | Multiplies every hatch scale, for a one-off adjustment. |
| Create as block | on | The whole detail becomes one block (`PAV_<name>`) that is easy to move or copy to a sheet. |

The geometry is drawn at **true size** in model space (a 40mm course is 40mm tall in a mm drawing), so
put a 1:10 viewport over it on your sheet.

Presets and your last settings are saved to `%APPDATA%\PavementBuildup\presets.json`. Four example
build-ups (flexible carriageway, footway, block paving, rigid concrete) are included the first time
you open it.

## CAD standard (your company's layers and hatches)

Everything about how the detail looks comes from a **CAD standard** file (JSON). Edit it with
`PAVESTANDARD`, or **Edit standard…** in the dialog. It has three tabs:

**Layers.** One row for each part of the detail:

| Element | What goes on it |
|---|---|
| Outline | Course interfaces and side edges / break lines |
| Hatch | Hatches, unless a hatch rule names its own layer |
| Membrane | 0mm courses (geotextile, DPM, slip membrane) |
| Leader | Label leaders and dots |
| Text | Course labels |
| Dimension | Thickness dimensions |
| Title | Title, scale and total-depth lines |

Each row sets the layer name, colour (ACI, `R,G,B`; double-click for AutoCAD's colour picker),
linetype, lineweight and plot. **Layers that already exist in the drawing are used as they are and
never modified**, so if your company template already has the layers, the template's settings win.
The colour, linetype and lineweight only apply when the plugin has to create the layer.

**Hatches.** An ordered list of rules: *if the course description contains any of these keywords,
use this pattern, scale, angle, layer, colour and background colour*. The first matching rule wins,
so put specific rules above general ones (▲/▼). There are also two fixed rows: the hatch used when
nothing matches, and the subgrade hatch.

- **Pattern** is any `acadiso.pat` pattern, `SOLID`, `NONE` (not hatched), or a **custom company
  pattern**. For a custom pattern, put `NAME.pat` in a folder on Civil 3D's support file search path
  and type `NAME`.
- **Scale** is the pattern scale for a 1:1 detail in a millimetre drawing. It is multiplied by the
  detail scale when drawn, so one standard works at 1:5, 1:10 or 1:20.
- **Pick from drawing…** copies an existing hatch into the selected rule. Open one of your company's
  standard details, set "picked hatch is at 1:" to that detail's scale, click the button and select
  the hatch. Its pattern, scale, angle, layer and colours are copied in.
- **Test a course description** shows which rule a description would hit.

**Text & labels.** Text style and dimension style (they must exist in the drawing, which normally
means your template), text and title heights, membrane line width, upper-case on/off, and the label
wording, for example:

| Setting | Default | Example alternative |
|---|---|---|
| Course label | `{thickness}mm {description}` | `{description} ({thickness}mm THK)` |
| Dimension text | `{thickness}mm` | `{thickness}` |
| Title | `{name}` | `DETAIL {name}` |
| Scale line | `SCALE 1:{scale}` | blank (no line) |
| Total depth line | `TOTAL CONSTRUCTION DEPTH = {total}mm` | `TOTAL DEPTH {total}` |

If something in the standard isn't in the drawing (a text style, a linetype, a custom pattern), the
detail is still drawn using the nearest fallback, and a warning is printed on the command line.

### Sharing one standard across the company

1. Set the standard up once with `PAVESTANDARD`, then **Save as…** to a shared location, e.g.
   `\\server\CAD\Standards\pavement-standard.json`. You can start from
   [`standards/example-company-standard.json`](standards/example-company-standard.json).
2. Everyone else clicks **Use another…** in the `PAVEBUILDUP` dialog and picks that file. Their
   choice is remembered.
3. Make the shared file read-only for everyone except the CAD manager. Users who try to change it
   are told they can't save it and are offered **Save as…** for a personal copy.

If the shared file can't be found (for example, when you're off the network), the plugin stops and
says so instead of quietly drawing to a different standard.

The file is plain JSON, so you can also edit it in a text editor and keep it under version control.
Comments (`//`) are allowed.

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
src/PavementBuildup.Core     Models, CAD standard, build-up parser, hatch rules, layout engine, presets (no AutoCAD refs)
src/PavementBuildup          Civil 3D / AutoCAD 2026 plugin: commands, dialogs + preview, CAD drawer
tests/...Core.Tests          xUnit tests for the core
standards/                   Example company CAD standard (JSON)
bundle/                      Autoloader PackageContents.xml (R25.1 = 2026)
build.ps1                    Test, build, assemble dist\PavementBuildup.bundle, optional -Install
```

The plugin references the AutoCAD 2026 API from NuGet (`AutoCAD.NET` 25.1.0, compile-time only), so you
do not need Civil 3D installed to build it. It only uses the AutoCAD API, so it also runs in plain
AutoCAD 2026.
