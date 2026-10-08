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
| `PAVETEXT` | Converts a spec note that's already in the drawing. Select the MText (or the lines of text), check the result in the dialog, then place the detail. The dialog's **From drawing note…** button does the same. |
| `PAVEEDIT` | Edits a detail that's already in the drawing. Select it (or pre-select it), and the dialog opens with its build-up, reinforcement and settings filled in. Change anything, click **Update detail**, and it's redrawn in place. A detail drawn as a block updates **every copy** of that block, including ones on other layouts; a detail drawn as loose entities is redrawn where it currently sits, even if you've moved or rotated it. |
| `PAVESTANDARD` | Edits the **CAD standard**: which layers everything goes on, what each material's hatch looks like, text and dimension styles, and the wording of labels. Also available from **Edit standard…** in the `PAVEBUILDUP` dialog. |

### Entering a build-up

Type or paste the build-up **the way it's written in your specs and notes**, top to bottom. All
thicknesses are in **millimetres**.

```
60mm THICK PAVERS (REFER TO LANDSCAPE SPECIFICATIONS), 30mm THICK MORTAR, 2 x 150mm THICK LAYERS OF DGB20 ROAD BASE, SUBGRADE COMPACTED TO 98% STANDARD MDD.
```

draws:

```
60mm THICK PAVERS (REFER TO LANDSCAPE SPECIFICATIONS)   ← pavers hatch
30mm THICK MORTAR                                       ← sand/mortar hatch
2 x 150mm THICK LAYERS OF DGB20 ROAD BASE               ← one 300mm course, line between the two layers,
                                                          dimensioned 150 + 150
SUBGRADE COMPACTED TO 98% STANDARD MDD                  ← the subgrade label
TOTAL CONSTRUCTION DEPTH = 390mm
```

- Courses can be separated by commas, `;`, new lines, `|` or ` / `. A comma inside brackets
  `( )`, or in a number like `37,5`, doesn't split.
- **The wording is kept exactly as you wrote it.** Labels read `{thickness}mm {your text}`, so
  `60mm THICK PAVERS …` comes out as written.
- `2 x 150mm …` is one course of two equal layers. The table has a **No. of layers** column for this.
- **Anything in (round brackets) is a note.** It's ignored when reading the course (thickness,
  splitting, hatch) and appears on the label as written.
- An item with **no thickness after a course** (`REFER TO LANDSCAPE SPECIFICATIONS`, `REFER TO
  DRAWING CV-TTW-1030`) becomes a second line on that course's label.
- **ON / OVER / LAID ON / BEDDED ON …** before a course are linking words: `ON 30mm VARIABLE SCREED`
  is a 30mm screed course, and `60mm PAVERS ON 30mm SCREED` is two courses.
- An item about what the pavement sits on (**subgrade**, formation, natural ground, or anything
  **existing**, e.g. `ON EXISTING CONCRETE SLAB (LEVELS TBC ON SITE)`) becomes the bottom strip's label.
  It's hatched by its wording (an existing slab gets the concrete hatch; subgrade gets the subgrade hatch).
- `2 x 150mm` (or `2x 150mm`) has a leader branch to **each** layer.
- **Existing work** (anything whose wording says EXISTING, e.g. the existing slab underneath, or an
  `EXISTING ASPHALT` course) is hatched on **`PEN-GREY-H`** and outlined with a closed polyline on
  **`PEN-GREY-C`**. When the base is existing, the new pavement's side edges and break lines stop on top of it.
- **Anything in [square brackets] is the title**, e.g. `[PAVEMENT TYPE A] 60mm THICK PAVERS, …`.
  Square brackets after a course that hold bar notation (`[H16@150 c50]`) are still reinforcement.
  A heading before the first course (`PAVEMENT TYPE A: 60mm …`, or a heading line on its own) also
  becomes the detail name. Note numbering like `1.` or `a)` is ignored, and when the note uses
  commas, line breaks are treated as wrapping, so a note copied from a drawing reads correctly.
- `40mm SMA`, `40 SMA` and `SMA 40mm` all work. `40/60` bitumen grades are left alone.
- A **0mm** course (geotextile, DPM, slip membrane) is drawn as a heavy line with a label.
- **Reinforcement** (optional) goes in square brackets after a course, or in the table's
  *Reinforcement* column. See [Reinforcement](#reinforcement).
- The hatch is chosen from the wording by the CAD standard's hatch rules. The built-in rules know
  UK and Australian terms (pavers, mortar, DGB/DGS, crushed rock, road base, CTB, …). The table
  shows which rule each course matched, and you can override the pattern, scale and angle for any
  course (`NONE` leaves it unhatched).

### Drawing settings (dialog → *Drawing*)

These change from drawing to drawing, so they live in the dialog rather than in the standard.

| Setting | Default | Notes |
|---|---|---|
| Scale 1: | 10 | Detail scale. Text, arrows, leaders and hatch density are sized so they plot correctly at this scale. |
| Width | 1000mm | Width of the section strip. |
| Drawing units | STANDARD | Uses the CAD standard's units (default **metres**: 40mm draws as 0.04). You can override per drawing with M/MM/CM, or AUTO to read `INSUNITS`. Thicknesses are always typed in mm. |
| Hatch scale × | 1.0 | Multiplies every hatch scale, for a one-off adjustment. |
| Show dimensions | on | Thickness, total and reinforcement cover dimensions. Untick for none. |
| Create as block | on | The whole detail becomes one block, named `TTW_pavement-profile_1`, `_2`, … (set by the standard's *Detail name*), that is easy to move or copy to a sheet. With it off, the pieces are put in a group with the same kind of name. |

The geometry is drawn at **true size** in model space. In a metre drawing a 40mm course is 0.04
units tall; text, dimensions and hatching are scaled to match. Put a 1:10 viewport over it on your sheet.

### Editing details already in the drawing

Every detail stores its build-up and settings inside the drawing: on the block definition, or on an
(unnamed) group when *Create as block* is off. That's what lets `PAVEEDIT` reopen it. Things to know:

- Details keep the drawing units they were drawn in, and stay a block or a group.
- Editing uses the CAD standard as it is **now**, so `PAVEEDIT` → **Update detail** is also how you
  bring an old detail up to a changed company standard.
- Details drawn with versions of the plugin before `PAVEEDIT` don't carry this data. Redraw them
  once with `PAVEBUILDUP` and they become editable.
- Exploding a block detail throws its data away (the pieces become plain lines and hatches).

## Reinforcement

Add bars to any course, either in the table's **Reinforcement** column or in square brackets in
quick entry:

```
250 PQC C32/40 [H16@150 c50]                                   bottom mat, 50mm cover
250 PQC C32/40 [top H12@200 c40, H16@150 c50 + H10@300]        top and bottom mats, transverse H10s
250 CRCP [top H16@150 c90 + H12@600]
```

- `H16@150 c50` means H16 bars at 150 centres with 50mm cover. The cover is measured to the outside of
  the main bars. It is the bottom mat unless you start with `top`.
- `+ H10@300` adds transverse bars, drawn as a line on the inside of the main bars.
- `c/c`, `crs`, `cover 50`, `cover=50mm`, `&` and `and` are all accepted. The bar prefix is optional;
  if you leave it out, the standard's prefix (default `H`) is used.
- Bars cut by the section are drawn as filled circles at true diameter, centred across the detail
  width at the given spacing. Each mat gets a leader label and a cover dimension.
- If the bars don't fit in the course, or the top and bottom mats overlap, the plugin tells you
  instead of drawing them.

The bars go on the standard's **Reinforcement** layer. The label wording, bar prefix, TOP/BTM text
and cover dimension are set on the standard's *Text, labels & bars* tab, for example
`{bars} {face} + {transverse} - {cover}mm COVER` → `H16 @ 150 c/c BTM + H10 @ 300 c/c - 50mm COVER`.

Presets and your last settings are saved to `%APPDATA%\PavementBuildup\presets.json`. Five example
build-ups (flexible carriageway, footway, block paving, rigid concrete, reinforced CRCP) are included the first time
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
| Reinforcement | Bars (circles) and transverse bars (lines) |
| ExistingHatch | Hatch of anything existing (default `PEN-GREY-H`) |
| ExistingOutline | Closed polyline round anything existing (default `PEN-GREY-C`) |

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

**Text, labels & bars.** Title, scale-line, label and dimension text heights and the arrow size (all
plotted mm; the title block is centred under the build-up; dimensions are made non-annotative so
these sizes always apply), drawing units (default **M**: 1 unit = 1 metre), detail name (default `TTW_pavement-profile_#`, where `#` is the next unused number and `{name}` is the build-up name), text style and dimension style (they must exist in the drawing, which normally
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

### Standard drawing (pull layers, styles and blocks from a company .dwg)

Set **Standard drawing** (*Text, labels & bars* tab) to your company template or standards drawing
(`.dwg` or `.dwt`, for example on the shared drive). Before each detail is drawn or updated, anything
the standard names that the current drawing doesn't have yet is copied in from it:

- every layer the standard uses (element layers and hatch-rule layers), with its own colour, linetype and lineweight
- the text style and the dimension style
- the break line block (`TTW_stdCountry_Block_Break`)

Definitions already in the drawing are never overwritten. The command line lists what was imported.

### Break lines

By default the side break lines are built the way Express Tools `_BREAKLINE` builds them, using the
**`TTW_stdCountry_Block_Break`** block. The block is placed at the middle of each side edge, aligned
with it and scaled by **Size**. Its two POINT objects mark where the line stops and restarts. If the
block has no points, its ends along the line are used instead. Everything goes on the Outline layer,
inside the detail's block or group, so it moves, copies and updates with the detail. Nothing is
queued on the command line, so Express Tools doesn't need to be installed.

Settings (*Text, labels & bars* tab): **Break lines** (`BREAKLINE` or `BUILTIN` for a simple Z
break), **Break line block**, **Size** and **Extension** (plotted mm, scaled by the detail scale).
The block must be in the drawing, the standard drawing, or on the support path as `NAME.dwg`;
otherwise you get a warning and simple break lines.

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

## AI (free, through Claude.ai)

Under the quick-entry box:

1. Type, paste or pick (**From drawing note…**) the build-up, in any form.
2. **Ask AI (free)…** copies the AI Pavement Build-up Prompt plus your text and opens claude.ai
   (a free Claude.ai account is enough).
3. In the browser, press Ctrl+V and send, then copy Claude's reply.
4. **Paste AI reply** puts it in the box and fills the table. **Undo AI** goes back to your text.

Nothing is paid and nothing needs setting up. Without the AI, the plugin still reads the text itself.

### Optional: automatic AI (paid Claude API)

Tick **Auto AI (paid API)** to skip the copy/paste: entered text is sent to the Claude API
automatically (also for `PAVETEXT` / `PAVEQUICK`). This needs a pay-as-you-go API key or an
Anthropic account sign-in, set up once in **`PAVEAI`**; it's off by default. The order used is
saved key, then `ANTHROPIC_API_KEY`, then account sign-in (`ant auth login`, which renews itself).
A company can change how the AI writes build-ups (both routes) by pointing the standard's
**AI prompt file** at an edited copy of the prompt.

## AI prompt

[`AI Pavement Build-up Prompt.txt`](AI%20Pavement%20Build-up%20Prompt.txt) (also in the zip) turns
any AI chat into a converter. Upload or paste it, then paste a build-up in whatever form you have it,
and the AI replies with a line the plugin accepts.

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
