# Detail_Grid

An AutoLISP tool for AutoCAD: click a frame block (the block used to align
a viewport to your title block) and it automatically drops in a grid block,
matched to the frame's insertion point, rotation, and scale - so your detail
grid always lines up, no matter what sheet scale the frame is at.

## How it works

1. You draw your own "grid module" as a block (default expected name:
   `detail_line`) at true 1:1 size, with the insertion point set to
   whichever corner or center you want anchored to the frame. It can live
   in its own library drawing - it doesn't need to already be inserted in
   the drawing you're running the command in.
2. Run `DETAILGRID` and click the frame block.
3. If `detail_line` isn't already defined in the current drawing,
   DetailGrid imports the block definition automatically from the library
   file configured in `DetailGrid.cfg`.
4. DetailGrid reads the frame's scale - its annotation scale if it's an
   annotative block, otherwise its X/Y scale factor. If a scale in the
   drawing's scale list matches that factor, DetailGrid sets `CANNOSCALE`
   (the document's current annotation scale) to it - this is what makes
   an *annotative* grid block display at the right size, since annotative
   objects size themselves from `CANNOSCALE` rather than from a scale
   factor set on the object. Note this is a document-wide setting, so it
   also affects how every other annotative object in the drawing displays,
   not just the new grid.
5. The grid block is inserted at the frame's insertion point and rotation
   on its own non-plotting layer (`DETAIL-GRID`) - matched via `CANNOSCALE`
   if it's annotative, or via a direct X/Y scale factor if it isn't.
6. Run it again on the same frame any time you move/rescale it - the old
   grid for that frame is removed first, so you don't end up with stacked
   duplicates.

## Setup

This is set up as two folders - `DetailGrid.lsp` sits with the rest of the
office's scripts, while its config, docs, and library drawing live in a
dedicated support subfolder:

```
...\Scripts\_under_development\
    DetailGrid.lsp
    _support\Detail_Grid\
        DetailGrid.cfg
        README.md
        detail_grid.dwg      (contains the detail_line block)
```

1. Create your grid block (`detail_line`) in `detail_grid.dwg`, in the
   `_support\Detail_Grid` folder shown above.
2. `DetailGrid.lsp` already knows where that support folder is - it's set
   in `*dg:support-dir*` near the top of the file. Update that line if the
   folder ever moves.
3. Edit `DetailGrid.cfg` (in the support folder) to match your setup:
   ```
   GRID_BLOCK=detail_line
   GRID_SOURCE_DWG=A:\Civil\AutoCAD\Global\Australia\NSW\_default\Scripts\_under_development\_support\Detail_Grid\detail_grid.dwg
   GRID_LAYER=DETAIL-GRID
   ```
   Change the values after each `=` - no need to touch `DetailGrid.lsp`
   itself. If `DetailGrid.cfg` isn't found, DetailGrid falls back to the
   same defaults shown above.
4. Load `DetailGrid.lsp`:
   - `APPLOAD` it for the current session, or
   - Add it to your Startup Suite, or
   - Add `(load "DetailGrid.lsp")` to `acaddoc.lsp` so it's available in
     every drawing automatically. For autoload to find it by name alone
     (rather than a full path), put `...\Scripts\_under_development` on
     AutoCAD's Support File Search Path (`OPTIONS` > **Files** tab).
5. If you ever want to use a different grid block just for this session,
   run `DGRIDBLOCK` and click an instance of it - this overrides
   `DetailGrid.cfg` until you reload the drawing/lisp.

## Commands

| Command      | Alias | Description                                                        |
|--------------|-------|----------------------------------------------------------------------|
| `DETAILGRID` | `DG`  | Click a frame block; insert/refresh its aligned grid.               |
| `DGRIDBLOCK` |       | Click a block instance to use as the grid block going forward.      |

## Notes

- The grid layer (`DETAIL-GRID`) is created automatically, colored magenta
  (color 6), and set non-plotting.
- If `detail_line` is annotative, the drawing's Annotation Scale List needs
  an entry whose ratio matches the frame's scale factor, or DetailGrid
  can't switch `CANNOSCALE` to it (it'll warn you when this happens).
  Since `CANNOSCALE` is one document-wide setting, this approach shows one
  scale at a time - if you need several differently-scaled frames visible
  correctly at once on the same sheet, make `detail_line` non-annotative
  instead (Block Editor > Properties > Annotative: No) so it's matched
  per-instance via X/Y scale factor.
- Auto-import of the grid block reads the source .dwg through an
  ObjectDBX side-database, so it only pulls in that one block definition -
  it won't insert anything else from the library file.
- Requires full AutoCAD (AutoLISP customization isn't available in
  AutoCAD LT).
