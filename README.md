# Detail_Grid

`attachttwxrefFORNOW.lsp` — AutoLISP command that attaches every `TTW_*.dwg` in a
folder (e.g. `...\Civil\AutoCAD\Global\Australia\NSW\_default\Details\Notes`) as an
xref at 0,0.

## Use

1. In AutoCAD / Civil 3D run `APPLOAD` and load `attachttwxrefFORNOW.lsp`.
2. Type `ATTACHTTWXREFFORNOW`.
3. Pick any DWG in the folder.

Drawings already in the file are skipped. The whole run is one undo step.

## Settings (top of the .lsp)

| Variable | Default | Meaning |
|---|---|---|
| `*ATX:Folder*` | `nil` | Fixed folder path; `nil` asks each time |
| `*ATX:Pattern*` | `"TTW_*.dwg"` | Files to attach |
| `*ATX:Overlay*` | `nil` | `T` attaches as overlay instead of attachment |
