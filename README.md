# TTW Linemarking Tool (V3)

A Civil 3D 2026 plugin for TTW drafters. You select polylines and pick what the line is *for*
("Give Way Line", "Edge Of Road"), and the tool applies the approved RMS/TfNSW linetype, width,
layer and paint colour. For paired barrier lines it draws both lines from a centreline.

This is a ground-up rebuild of the V2 .NET plugin (`swiine/Linemarking_LISP`, `dotnet/`). It does
everything V2 does, but the code is restructured so it can be compiled and tested off a Civil 3D
machine. The LISP/DCL tool (V1.8.4 / V1.9.1) in that repo is untouched and still the production
fallback.

## Commands

| Command | What it does |
| --- | --- |
| `LINEMARKING` or `8` | Opens the purpose list: grouped, searchable, with a live line preview. |
| `ROAD` | Same dialog, opened in Manual Override (the flat code list from the original tool). |

You can select polylines **before** running the command, or after picking the marking.

### What happens on Apply

- **Single lines:** each polyline goes on layer `geom-prop-lmk` with the linetype, constant
  width and linetype generation (PLINEGEN) set. Red and yellow markings get TTW's exact RGB colours
  (184,29,19 and 255,191,0). Everything else is ByLayer.
- **Paired lines** (BL1, BL2, BL5, BL6, which are RMS BS/BB/BS1/BB1): the selected polyline is the
  *centreline*. Two new lines are drawn either side of it, spaced to give the RMS clear gap between
  the painted edges. The centreline moves to `Defpoints`, so it stays visible but never plots. For
  dashed + solid pairs you click which side gets the line you picked.
- **PCW and TR3:** these are double lines, but no confirmed gap exists yet. One line is drawn and
  you're told to offset the second by hand.
- **Unsupported scale:** if `CANNOSCALE` isn't 1:100, 1:200, 1:250, 1:500 or 1:1000, a warning
  appears and nothing changes.
- **Scale changes:** if a linetype was loaded at a different scale, it's reloaded with the
  current scale's pattern.

## What's new compared with V2

- **Compiler-checked off-site.** The plugin builds against Autodesk's official `AutoCAD.NET`
  25.1.0 reference package (the AutoCAD 2026 / .NET 8 release on nuget.org), so it compiles on
  Linux, CI or any PC without Civil 3D. Nothing from Autodesk is stored in this repo. API mistakes
  like V2's `LinetypeGenerationOn` and `FromRGB` now fail here, before they reach your machine.
- **Tests against the real .lin files.** 49 unit tests check the catalogue against all five
  `.lin` files: codes, descriptions, RMS codes, width notes, sections, colours, and that every
  scale's dash pattern equals the 1:100 pattern scaled. They also cover scale parsing, offset
  maths, pair integrity and the Guided Questions tree.
- **Stricter scale matching.** `1:1000` can no longer be mistaken for `1:100`, and `1:10` or
  `1:2000` are rejected outright.
- **Stale linetypes are fixed.** Switching a drawing's scale used to keep the old dash lengths.
- **Fewer prompts.** Pre-selection works. Identical pairs (BL2, BL6) skip the side pick.
- **Heavy 2D polylines** now get their width set too, instead of a "check manually" warning.
- **Locked layers and failed offsets** are skipped and reported, instead of stopping the command.
- **The dialog remembers your last marking** for the rest of the session.
- **The preview** draws the marking on asphalt with true dash proportions, relative widths,
  colour and both lines of a pair.
- **Guided Questions is data-driven.** When the real question wording is ready, it goes in
  `GuidedQuestions.cs` as a tree. The wizard form needs no changes.
- **Linetype fallback.** If `A:\` isn't available, the tool also looks for a `Linetypes` folder
  next to the DLL.

## Layout

```
src/TTWLinemarking.Core/   pure logic, no Autodesk refs: catalogue, scales, offset maths,
                           .lin parser, Guided Questions tree
src/TTWLinemarking/        the plugin: commands, drawing changes, WinForms UI
                           (compiles Core in, so it still ships as ONE dll)
tests/                     xUnit tests for Core, run against linetypes/*.lin
linetypes/                 TTW_stdState_Linetype_Linemk [scale].lin (100/200/250/500/1000)
deploy/                    acaddoc.lsp snippet (draft) and .bundle manifest
build.bat                  double-click build on Windows -> out\TTWLinemarking.dll
```

## Building

**Option 1: double-click `build.bat`.** It uses `dotnet` from PATH, or your portable SDK at
`C:\dotnet-sdk\dotnet-sdk-8.0.424-win-x64\`. If AutoCAD 2026 is installed at
`C:\Program Files\Autodesk\AutoCAD 2026` it compiles against those DLLs and needs no internet.
Otherwise it downloads the `AutoCAD.NET` package from nuget.org. The output is
`out\TTWLinemarking.dll`. To point at another install folder:
`build.bat -p:AcadDir="D:\Your\Path"`.

**Option 2: GitHub Actions.** Every push builds the DLL, runs the tests and attaches
`TTWLinemarking.dll` to the run as an artifact (Actions tab > latest run > Artifacts).

**Option 3: command line:** `dotnet test tests/TTWLinemarking.Tests`, then
`dotnet build src/TTWLinemarking -c Release`.

## Installing

- **Test on one machine:** run `NETLOAD`, pick `TTWLinemarking.dll`, then type `8`. This lasts for
  the current session only. Build and load from a local folder, because endpoint security has
  blocked freshly built DLLs on the `A:\` drive before.
- **Autoload for everyone (proposed):** copy the DLL to the shared Scripts folder and append
  `deploy/acaddoc_snippet.lsp` to the existing `acaddoc.lsp`. *Draft, not yet tested live.*
- **.bundle:** see the comments in `deploy/PackageContents.xml`.

Optional: put `ttw_logo.png` next to the DLL and it shows in the dialogs.

## Status

Compiles cleanly against the AutoCAD 2026 API, and the 49 tests pass. **Not yet run inside Civil
3D.** The first live test should cover:

1. `8` opens the dialog at a sensible size. `Ui.Scale` in `UI/Ui.cs` is the one number to adjust.
2. Single line, e.g. EL1: layer, width, PLINEGEN, ByLayer colour. Then a red one (BU2) and a
   yellow one (NS1).
3. Paired lines: BL2 (no side prompt) and BL1 DASHED (side prompt), with the centreline on
   Defpoints.
4. Change `CANNOSCALE` from 1:100 to 1:500, re-apply, and check the command line says the
   linetype was updated.
5. Pre-select polylines, then run `8`.
6. A heavy 2D polyline (`PLINETYPE` 0, or a converted one) gets its width.

## Outstanding

- Guided Questions wording, from the RMS/TfNSW standards (placeholder tree in place).
- Confirmed gap distances for PCW and TR3.
- Deployment method (acaddoc snippet drafted).
- Widths: the tool applies the production LISP widths. These differ from the `.lin` notes for
  T1 (0.10 vs 0.2), PCW (0.10 vs 0.15), LL3 (0.10 vs 0.15) and PX (0.10 vs 3.6 TBA). Worth
  confirming which is right.

## Versioning

`ttw_linemarking V[major].[workplace].[testing]`. Major is the structural/engine version, the
second number is the release given to the workplace, and the third is a personal/testing
revision. The version is set once, in `Directory.Build.props`.

**Rule:** never remove working engine functionality. Always build from the last proven version.
