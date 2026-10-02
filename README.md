# Detail_Grid

## Split a master DWG with Batch Save Utility

Makes one DWG per drawing (layout tab) in a master file, each named after its
drawing and holding only that layout. The master is not changed.
Files are in `BatchSave/`.

**Step 1: make the copies (in AutoCAD / Civil 3D)**
1. Open and save the master drawing.
2. `APPLOAD` and load `SplitCopy.lsp`.
3. Type `SPLITCOPY`. It copies the master once per layout into a `Split`
   folder next to the master, e.g. `Split\A-101.dwg`, `Split\A-102.dwg`, ...
4. Close AutoCAD (or at least don't open the copies).

**Step 2: strip each copy (Autodesk Batch Save Utility)**
1. Open Autodesk Batch Save Utility (Standalone).
2. Add the `Split` folder (or all the DWGs in it).
3. Under the script option, choose `KeepOneLayout.scr`.
4. Run it. For each file, the script keeps the layout whose name matches the
   file name, deletes every other layout, and the utility saves the file.
   A file with no matching layout is left unchanged and noted in the log.

## split_drawings.py (alternative)

Does both steps in one go by driving full AutoCAD through COM instead of
Batch Save Utility. Requires Windows, AutoCAD, and `pip install pywin32`.

```
python split_drawings.py "C:\Jobs\Master.dwg"                  # output to C:\Jobs\Split
python split_drawings.py "C:\Jobs\Master.dwg" --out "C:\Sheets"
python split_drawings.py "C:\Jobs\Master.dwg" --dry-run        # preview names only
```

Add `--overwrite` to replace files that already exist.
