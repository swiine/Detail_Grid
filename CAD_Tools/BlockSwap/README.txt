BLOCKSWAP - delete blocks, purge, copy blocks in from a template

For: Autodesk Batch Save Utility

What it does in each drawing:
  1. Deletes EVERY block whose name starts with TTW_stdCountry_DrgSht_Att_
     - model space, every layout, and inside other blocks; locked layers too.
  2. Purges (x3).
  3. Copies EVERY block from the template drawing in, at its exact
     location (like PASTEORIG). Template model space -> model space,
     template layout -> same-named layout (or the first layout).

The 4 settings are the first 4 lines of BlockSwap.scr:
  BS-DELETE   = names to delete (wildcards OK)
  BS-COPY     = nil = copy every block in the template
  BS-TEMPLATE = template drawing (\\ttw\... path, backslashes doubled)
  BS-LOG      = log file (C:\CAD is created if missing)
Do NOT add blank lines or ; comments to the script.

Running it:
1. CLOSE the template drawing first, and don't include it in the batch.
2. Batch Save Utility: add the drawings, pick BlockSwap.scr, purge option OFF.
3. Test on 2-3 COPIES first and read C:\CAD\BlockSwap.log.

Log lines per drawing: how many were deleted, what was copied from where,
and any error (e.g. template not found / couldn't open).
