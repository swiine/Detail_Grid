BLOCKSWAP

Setup (once):
1. Copy LoadAll.lsp, BlockSwap.lsp and RunAll.lsp (and any other tool .lsp) to C:\CAD\
2. Options > Files > Trusted Locations: add C:\CAD
   (otherwise AutoCAD asks to confirm loading on every drawing).
3. APPLOAD C:\CAD\LoadAll.lsp - loads all the tools at once.
   Add it to APPLOAD > Startup Suite to load it every time AutoCAD starts.

One drawing:  APPLOAD BlockSwap.lsp, type BLOCKSWAP.

All sheets in a folder:
1. Close all drawings except a blank one (Drawing1). Close the template.
2. APPLOAD RunAll.lsp, type RUNALL.
3. Select the sheets folder.
4. A list of its drawings appears: Ctrl/Shift-click the ones to run
   (or "Select all"), click OK, then answer Yes.
   It opens each sheet, AUDITs (fix=Yes), runs BLOCKSWAP, AUDITs again,
   saves, closes - one by one.

What BLOCKSWAP does:
  1. Deletes every block starting with TTW_stdCountry_DrgSht_Att_
     (all other blocks are left alone).
  2. Purges.
  3. Copies the TTW_stdCountry_DrgSht_Att_ blocks from the template in
     at their exact location.

Log: C:\CAD\BlockSwap.log
The template drawing is skipped automatically.
