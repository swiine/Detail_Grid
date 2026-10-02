# Detail_Grid
## split_drawings.py

Splits a master DWG into one file per drawing (layout tab). Going down the
layout list in tab order, it copies the master file, deletes every layout
except one, and saves the copy named after that drawing. The master is not
changed.

Requires Windows, AutoCAD, and `pip install pywin32`.

```
python split_drawings.py "C:\Jobs\Master.dwg"                  # output to C:\Jobs\Split
python split_drawings.py "C:\Jobs\Master.dwg" --out "C:\Sheets"
python split_drawings.py "C:\Jobs\Master.dwg" --dry-run        # preview names only
```

Add `--overwrite` to replace files that already exist.
