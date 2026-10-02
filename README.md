# Detail_Grid

## SplitDrawings

Makes one copy of a drawing folder for each DWG in it.

Going down the list of DWG files (including ones in subfolders), each copy:
- keeps every subfolder and every non-DWG file,
- keeps that one drawing plus `TTW_stdCountry_A1L_Frame.dwg`, and deletes all
  other DWGs, so it ends up with 2 DWG files,
- is named after the drawing it kept.

The original folder is not changed. Copies go into `<folder>_Split` next to it:

```
Details\              ->   Details_Split\D-101\  (D-101.dwg + frame + everything else)
                           Details_Split\D-102\  (D-102.dwg + frame + everything else)
                           ...
```

### Run it

Drag the drawing folder onto `SplitDrawings.bat`, or from PowerShell:

```
powershell -ExecutionPolicy Bypass -File SplitDrawings.ps1 -Source "C:\Jobs\Details"
```

Options:
- `-Out "C:\Jobs\Split"`: put the copies somewhere else
- `-WhatIf`: list what would be made without copying anything
- `-Overwrite`: replace copies that already exist (otherwise they are skipped)
- `-Frame "OtherFrame"`: keep a different frame file

Two drawings with the same name in different subfolders get ` (2)`, ` (3)`...
added to the copy's name.
