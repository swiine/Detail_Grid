# Detail_Grid: Profile View Stacker for Civil 3D 2026

A Civil 3D 2026 (AutoCAD 2026, .NET 8) plugin. You pick one long, continuous profile view and a sheet
template. It splits the long view into a set of profile views that each **exactly fill a profile
viewport** on the template. It stacks them in sheet order, gives each one the matching station range
and a fitted datum, and creates the sheet layouts with every viewport already set to its view.

```
 Long profile view 0+000 ─────────────────────────────────────────────── 1+000

 Template layout "PROFILE A1"            Result
 ┌──────────────────────────────┐        PROFILE 01 (0+000 to 0+740)
 │ ┌──────────────────────────┐ │        ┌──────────────────────────────┐
 │ │   profile viewport 1     │ │        │ ┌ 0+000 ── 0+370, datum 10 ┐ │
 │ └──────────────────────────┘ │  ───►  │ └ 0+370 ── 0+740, datum 12 ┘ │
 │ ┌──────────────────────────┐ │        └──────────────────────────────┘
 │ │   profile viewport 2     │ │        PROFILE 02 (0+740 to 1+000)
 │ └──────────────────────────┘ │        ┌──────────────────────────────┐
 │                 title block  │        │ ┌ 0+740 ── 1+000, datum 30 ┐ │
 └──────────────────────────────┘        └──────────────────────────────┘
```

## Using it

1. Type **`TTWPROFSTACK`** and pick the long profile view. You can also pre-select it.
2. **Sheet template**: browse to the `.dwt` (or `.dwg`) and pick the layout that has the profile
   viewports. If the layout also has plan or key-plan viewports, enter text in **Viewport layer
   contains** (for example `PROF`) so that only the profile viewports are used.
3. Check the **stations**, the **datum** settings and the **names**. The right side of the window
   updates as you type. It shows a sketch of each sheet and a list of every view with its station
   range and datum.
4. Click **Create N views on M sheets**.

The new profile views are placed to the right of the long view, one column per sheet and stacked as
they appear on the sheet. Each new view has the same alignment, style, bands and profiles as the long
view. The long view is kept unless you tick **Erase the long profile view**. Your settings are saved
for next time in `%APPDATA%\TTW\ProfileStacker\settings.json`.

## How the views are sized

The tool measures the long view in the drawing: how much the axes, bands and title stick out around
the graph, and the vertical exaggeration. It then reads each template viewport's size and scale.
From those it works out the station length and elevation range that fit in each viewport:

```
station length   = (viewport width / scale  − left axis − right axis − 2 × clearance) / horizontal scale
elevation range  = (viewport height / scale − bands − title − 2 × clearance)          / vertical scale
```

The scale is the viewport's custom scale in paper units per drawing unit (the `XP` factor). For a
metre drawing on a millimetre sheet at 1:500, that's `2xp`.

- **Viewport order**: rows from the top of the sheet, left to right in each row. View 1 goes in
  viewport 1 of sheet 1, view 2 in viewport 2, and so on. When a sheet is full, the next view starts
  a new sheet. Viewports can differ in size, and each view gets the length of its own viewport.
- **Round lengths to**: each view's length is rounded down to a whole multiple (for example 10 m),
  so the views break on round stations. **Start on a round station** also rounds the first start
  down, but never before the start of the alignment.
- **Overlap**: repeats this much station at the start of each view, taken from the end of the
  previous one.
- **View lengths**:
  - **Fill each viewport** makes every view as long as its viewport allows.
  - **Even lengths** uses the same number of views but shares the length out evenly between them.
- **Trim the last view**: ends the last view at the end station. When this is off, the last view
  keeps its full length, up to the end of the alignment.

## How the datum is set

For each view, the tool finds the lowest and highest points of the profiles drawn in the long view
over that station range. It adds the **clearance below** and **above**. Then it sets the datum so
that the graph uses the full height of the viewport:

- **Under the low point**: the datum is the low point rounded down (to **Round datum to**).
- **Centred on the profile**: the profiles sit in the middle of the view height.

Where the profiles rise or fall more than the viewport can show, the view is made taller than its
viewport. Those views are marked *taller than viewport* in the list. To fix them, use shorter views,
a smaller scale or a taller viewport. A view with no profile in its range uses the datum from the
view before it.

## Sheet layouts

Each sheet is a new layout copied from the template layout, with the same page setup, title block
and viewports. Layers and styles that are already in the drawing are kept as they are. Every profile
viewport is centred on its view: the left edge of the view sits at the left edge of the viewport,
plus the clearance, and the view is centred vertically. The viewport keeps the template's scale, or
uses the scale override. It's locked if **Lock the viewports** is ticked. On the last sheet, any
viewports that don't have a view are removed.

**Names** are built from patterns with these tokens:

| Token | Meaning |
|---|---|
| `<N>` | Sheet number for layouts, view number for views (01, 02 …) |
| `<START>`, `<END>` | First and last station on that sheet or view (`0+740`) |
| `<VIEW>` | Name of the long profile view |
| `<S>` | Sheet number (view names only) |

Stations are written as `1+250` in metric drawings and `12+50` in imperial ones. In the dialog you
can type them either way.

## Building and installing

You need the .NET 8 SDK. The Autodesk API comes from Autodesk's official NuGet reference packages,
`AutoCAD.NET` 25.1 and `Civil3D.NET` 13.8 (Civil 3D 2026). They're used at compile time only, so you
don't need Civil 3D installed to build.

```powershell
.\build.ps1            # tests, builds, and assembles dist\ProfileStacker.bundle
.\build.ps1 -Install   # also copies the bundle to %APPDATA%\Autodesk\ApplicationPlugins
```

To install manually, copy `dist\ProfileStacker.bundle` to `%APPDATA%\Autodesk\ApplicationPlugins`
and restart Civil 3D 2026. You can also `NETLOAD` `ProfileStacker.dll`. CI builds the bundle on every
push and uploads it as an artifact.

## Project layout

| Path | What's in it |
|---|---|
| `src/ProfileStacker.Core` | Pure logic with no Autodesk references: viewport ordering, capacity, station partitioning, datum fitting, model-space placement, naming and settings. |
| `src/ProfileStacker` | The Civil 3D plugin: the `TTWPROFSTACK` command, reading the long view and the template, creating the profile views and layouts, and the dialog. |
| `tests/ProfileStacker.Core.Tests` | xUnit tests for the Core logic. |
| `bundle/` | The autoloader manifest (`PackageContents.xml`). |
