# ET Walls

Retaining wall design tool by **Engineering Technologies (ET)**. Enter the retained height and the soil;
the tool sizes the cheapest wall that passes every check, designs the reinforcement, estimates the cost,
draws the typical section to the company CAD standard (TTW.json) and writes the calculation report
(HTML + Excel).

Wall types: cantilever concrete (footing under the soil or in front only), block (gravity), mass gravity,
sleeper and post.

> The design method is a placeholder (Rankine earth pressure, factors of safety, AS 3600-style reinforcement,
> Broms for posts) until the company spreadsheets are added, and the cost rates are placeholders.
> An engineer must check and sign off every result.

## What's here

| Folder | What it is |
| --- | --- |
| `src/ETWalls.Core` | The calculations (checks, reinforcement, costs, cost-based sizing, what-if) and the section drawing model. No Windows or AutoCAD, so it's tested anywhere and shared with the coming Civil 3D plugin. |
| `src/ETWalls.Export` | DXF of the section in metres on the standard's layers (netDxf). |
| `src/ETWalls.Reports` | The calculation report: HTML (8 sections, with the section drawing) and an Excel workbook with live cost formulas. |
| `src/ETWalls.Standalone` | The Windows app, `ET Walls.exe`: Detail tab (section, scale, standard, Save DXF) and Design tab (4-step wizard). |
| `tests/ETWalls.Core.Tests` | Tests. Every check, bar schedule, cost and automatic sizing is compared with the preview page's results (`golden-preview.json`). |
| `docs/ET Wall Tool preview.html` | The clickable design preview (open in a browser). Source in `docs/preview`. |
| `standards/TTW.json` | The company CAD standard (shared with the TTW pavement tool). |

## Build

Needs the .NET 8 SDK.

```powershell
.\build.ps1 -SelfTest      # tests, release folder dist\ET_Wall_Tool V1.0.0 (+ .zip), then the app's self test
```

Or by hand:

```
dotnet test tests/ETWalls.Core.Tests
dotnet publish src/ETWalls.Standalone -c Release
```

The app needs the .NET 8 Desktop Runtime (it comes with Civil 3D 2026). `ET Walls.exe --selftest <folder>`
opens the window, designs every wall type and writes the reports and DXFs into the folder (the Windows CI build
runs it).

Release folder:

```
ET_Wall_Tool V1.0.0\
  _support\_Standards\TTW.json
  Standalone Tool\ET Walls.exe
  Standalone Tool\ET Walls Standalone - Instructions.txt
  Standalone Tool\_Exports\        <- saves go to _Exports\date\job number\wall name
```

## Keeping the preview and the app in step

The app's calculations are a line-for-line port of `docs/preview/calc.js`. After changing the preview's
calculations, regenerate the reference results and run the tests:

```
node tools/golden.js > tests/ETWalls.Core.Tests/golden-preview.json
dotnet test tests/ETWalls.Core.Tests
```
