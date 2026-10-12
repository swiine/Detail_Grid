BLOCKSWAP - delete blocks, purge, copy new blocks in from a template

For: Autodesk Batch Save Utility

1. Open BlockSwap.scr in Notepad and edit the 4 lines at the top:
     BS-DELETE   = blocks to delete from paper space + purge
     BS-COPY     = blocks to copy from the template (nil = all)
     BS-TEMPLATE = path to your template drawing
     BS-LOG      = path for the log file
   Use forward slashes / in paths. Do NOT add blank lines.
2. In Batch Save Utility add your drawings and pick BlockSwap.scr as the script.
   Leave its own purge option OFF - the script purges at the right point.
3. Test on 2-3 COPIES first, check the log, then run the rest.

New blocks land at their exact location from the template (same as PASTEORIG).
If a new block has the same name as an old one, put it in BS-DELETE too.
Use ListBlocks (other folder) to get the block names.
