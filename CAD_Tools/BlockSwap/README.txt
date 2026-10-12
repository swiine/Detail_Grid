BLOCKSWAP

Setup (once):
1. Copy BlockSwap.lsp and RunAll.lsp to C:\CAD\
2. Options > Files > Trusted Locations: add C:\CAD
   (otherwise AutoCAD asks to confirm loading on every drawing).

One drawing:  APPLOAD BlockSwap.lsp, type BLOCKSWAP.

All sheets in a folder:
1. Close all drawings except a blank one (Drawing1). Close the template.
2. APPLOAD RunAll.lsp, type RUNALL.
3. Pick any drawing in the sheets folder, answer Yes.
   It opens each sheet, runs BLOCKSWAP, saves, closes - one by one.

Log: C:\CAD\BlockSwap.log
The template drawing is skipped automatically.
