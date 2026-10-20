# Detail_Grid

`DetailGrid.lsp` — AutoLISP command that attaches every DWG in a details folder
(e.g. `...\Civil\AutoCAD\Global\Australia\NSW\_default\Details\Notes`) as an xref
and lays them out in a grid.

## Use

1. In AutoCAD / Civil 3D run `APPLOAD` and load `DetailGrid.lsp`.
2. Type `DETAILGRID`.
3. Pick any DWG in the folder, then the grid's top-left point, number of columns and gap.

Each xref is sized from its extents, so rows and columns don't overlap.
Files starting with `__` (e.g. `__becl.dwg`) and drawings already in the file are skipped.

## Settings (top of the .lsp)

| Variable | Default | Meaning |
|---|---|---|
| `*DG:Folder*` | `nil` | Fixed folder path; `nil` asks each time |
| `*DG:Pattern*` | `"*.dwg"` | Files to attach, e.g. `"TTW_stdState_Notes*.dwg"` |
| `*DG:SkipPrefix*` | `"__"` | Prefix of files to skip |
| `*DG:Overlay*` | `nil` | `T` attaches as overlay instead of attachment |
| `*DG:Columns*` / `*DG:Gap*` | `4` / `50.0` | Prompt defaults |
