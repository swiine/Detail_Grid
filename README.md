# Detail_Grid
## DelBlockHatch.lsp

AutoLISP routine for AutoCAD (also runs in BricsCAD) that deletes every hatch
inside every block definition in the current drawing.

1. Load it with `APPLOAD` (or drag the file into the drawing window).
2. Run `DELBLOCKHATCH`.

- Cleans all block definitions, so every insert updates at once, including
  nested and dynamic blocks.
- Leaves hatches in model space and paper space alone.
- Skips xrefs.
- Temporarily unlocks locked layers, then locks them again.
- Can be undone in a single `UNDO`.
