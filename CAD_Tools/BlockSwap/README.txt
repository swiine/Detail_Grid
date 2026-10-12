BLOCKSWAP - delete all blocks (except the keep list), purge, copy blocks in from a template

Files:
  BlockSwap.scr  - for Autodesk Batch Save Utility (the 80 drawings)
  BlockSwap.lsp  - the same thing as a command, to TEST in one open drawing
                   (APPLOAD it, type BLOCKSWAP, watch the command line)

What it does in each drawing:
  1. Deletes EVERY block in model space and on every layout, EXCEPT:
       - the blocks named in BS-KEEP (the two scale bars)
       - xrefs
     Locked layers are handled.
  2. Purges (x3).
  3. Copies every block from the template in at its exact location
     (like PASTEORIG), except BS-KEEP blocks (so scale bars aren't doubled).
     Template model space -> model space; template layout -> same-named
     layout (or the drawing's first layout).

Settings = the first 3 lines of BlockSwap.scr:
  BS-KEEP     = block names to leave alone (wildcards OK)
  BS-TEMPLATE = template drawing (backslashes doubled); nil = don't copy
  BS-LOG      = log file (C:\CAD is created if missing)
Do NOT add blank lines or ; comments to the .scr.

Running it:
1. CLOSE the template drawing first.
2. Test: open a COPY of one drawing, APPLOAD BlockSwap.lsp, type BLOCKSWAP.
3. Batch: Batch Save Utility, pick BlockSwap.scr, its purge option OFF.
4. Check C:\CAD\BlockSwap.log.
