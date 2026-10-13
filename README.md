# Detail_Grid

Civil 3D 2026 tools.

## Catchment Area Boundary Visualiser (`CATCHMENTVIS`)

Click any point on a Civil 3D surface and the catchment that drains to that point is highlighted straight away.

1. `CATCHMENTVIS` → select a surface → accept or change the analysis cell size.
   The surface is sampled to a grid, depressions are filled, and D8 flow directions and flow
   accumulation are computed **once** (about 1 s for 1M cells).
2. Click pour points. Each click is answered from the prepared flow model (a few ms for typical
   catchments, about 150 ms for a whole 1M-cell surface) and shown as transient graphics:
   - red outline and cyan fill: the catchment boundary
   - blue line: the flow path from the outlet down to where water leaves the surface
   - circle: the pour point actually used
   The command line reports outlet, area (plus ha or ac when `INSUNITS` is metres or feet), cell count and relief.
3. Options at the prompt:
   - **Snap** turns snap-to-channel on or off. When on, the click moves to the highest-flow cell within 5 cells, so a click near a channel captures the channel's catchment instead of a sliver of hillside.
   - **Keep** writes the current boundary as a closed polyline on layer `C-HYDR-CTCH`.
   - **Exit** (or Enter/Esc) clears the highlight.

### Method

| Step | Algorithm |
|---|---|
| Sampling | TIN triangles are rasterised directly with linear interpolation (identical to the TIN). Other surface types use `FindElevationAtXY` per cell. |
| Conditioning | Priority-Flood + ε (Barnes et al., 2014). Pits and flats get a minimal gradient so every cell drains to the edge. |
| Flow direction | D8 steepest descent (diagonal distance weighted) |
| Catchment | Reverse walk of the D8 tree from the pour point |
| Boundary | Cell-edge tracing into closed rings. A D8 catchment always gives one outer ring. |

Accuracy is limited by the cell size: boundaries follow cell edges, so use a smaller cell for small catchments.

### Build and install

Requires Civil 3D 2026 and the .NET 8 SDK (Windows).

```
dotnet build src/DetailGrid.Civil3D -c Release
# non-default install folder:
dotnet build src/DetailGrid.Civil3D -c Release -p:Civil3DPath="D:\Autodesk\AutoCAD 2026"
```

In Civil 3D run `NETLOAD` and pick `src/DetailGrid.Civil3D/bin/Release/net8.0-windows/DetailGrid.Civil3D.dll`.
`DetailGrid.Hydrology.dll` must sit next to it, which the build does for you.

### Layout

- `src/DetailGrid.Hydrology`: pure C# hydrology (no Autodesk dependencies), unit tested
- `src/DetailGrid.Civil3D`: Civil 3D command, surface sampling, transient display
- `tests/DetailGrid.Hydrology.Tests`: run with `dotnet test tests/DetailGrid.Hydrology.Tests` (works on any OS)

### Known limitations

- TIN rasterisation uses every triangle. Areas removed by hide boundaries may still get elevations. If that matters, raise the cell size or use a surface without hide boundaries.
- The boundary is a raster (stair-step) outline at the analysis cell size.
