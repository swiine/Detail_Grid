# Detail_Grid

## SplitDrawings

Makes one copy of a drawing folder for each DWG in it.

Going down the list of DWG files directly in the folder, each copy:
- keeps every subfolder exactly as it is (DWGs in subfolders are never
  removed or split) and every non-DWG file,
- keeps that one drawing plus `TTW_stdCountry_A1L_Frame.dwg`, and deletes all
  other DWGs, so it ends up with 2 DWG files,
- has no `.bak` files,
- is named after the drawing it kept,
- is also zipped on its own (`D-101` -> `D-101.zip`).

The original folder is not changed. Copies go into `<folder>_Split` next to it:

```
Details\              ->   Details_Split\D-101\  (D-101.dwg + frame + everything else)
                           Details_Split\D-101.zip
                           Details_Split\D-102\  (D-102.dwg + frame + everything else)
                           Details_Split\D-102.zip
                           ...
```

### Run it

Double-click `SplitDrawings.bat` and pick the drawing folder when asked
(or drag the folder onto it). Keep the `.bat` and `.ps1` in the same folder.

### eTransmit each drawing (Batch Save Utility)

`BatchSave/ETransmitEach.scr` runs AutoCAD's own eTransmit on every drawing,
so each zip holds only the xrefs, images, fonts etc. that drawing uses.

1. In AutoCAD, run `ETRANSMIT` > Transmittal Setups > Modify "Standard" once:
   package type Zip, file format Keep existing, folder structure "Keep files
   and folders as is", and leave "Bind external references" off.
2. In Batch Save Utility, add the drawings, pick `ETransmitEach.scr`, run.
3. Packages are saved as `C:\eTransmit\<drawing>.zip`.

### Or with Autodesk Batch Save Utility

1. Open Batch Save Utility (Standalone) and add the drawings to split
   (adding the frame too is fine, it gets skipped).
2. Choose `BatchSave\SplitDrawings.scr` as the script and run.

Each drawing gets the same `<folder>_Split\<drawing>` copy and `.zip` as
above. Notes:
- Batch Save Utility saves every drawing it opens, so your original drawings
  are re-saved (no content changes).
- The zip is made with Windows' built-in `tar.exe`. If no zips appear, the
  copied folders are still there; run `SplitDrawings.bat` instead.
- The job folder is taken to be the drawing's folder, or the folder above it
  if the frame isn't found there.

Advanced, from PowerShell:

```
powershell -ExecutionPolicy Bypass -File SplitDrawings.ps1 -Source "C:\Jobs\Details"
```

Options:
- `-Out "C:\Jobs\Split"`: put the copies somewhere else
- `-WhatIf`: list what would be made without copying anything
- `-Overwrite`: replace copies and zips that already exist (otherwise they are skipped)
- `-Frame "OtherFrame"`: keep a different frame file
- `-List "drawings.txt"`: only split the drawings named in this text file, one
  per line (`.dwg` on the end is optional)
