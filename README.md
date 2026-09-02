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
   file configured in `*dg:grid-source-dwg*`.
4. DetailGrid reads the frame's scale - its annotation scale if it's an
   annotative block, otherwise its X/Y scale factor - along with its
   rotation and insertion point, and inserts the grid block at that exact
   point/rotation/scale on its own non-plotting layer (`DETAIL-GRID`).
5. Run it again on the same frame any time you move/rescale it - the old
   grid for that frame is removed first, so you don't end up with stacked
   duplicates.

## Setup

1. Create your grid block (`detail_line`) in a drawing - a dedicated
   library file works well, e.g. `detail_grid.dwg`.
2. Open `DetailGrid.lsp` and check the two config lines near the top:
   - `*dg:grid-block*` - the block's name (default `"detail_line"`).
   - `*dg:grid-source-dwg*` - full path to the .dwg that holds it
     (default `"C:\_under development\detail grid\detail_grid.dwg"`).
     Update this if the library file lives somewhere else or moves.
3. Load `DetailGrid.lsp`:
   - `APPLOAD` it for the current session, or
   - Add it to your Startup Suite, or
   - Add `(load "DetailGrid.lsp")` to `acaddoc.lsp` so it's available in
     every drawing automatically.
4. If you ever want to use a different grid block, run `DGRIDBLOCK` once
   and click an instance of it - DetailGrid will remember it for the
   session (or edit `*dg:grid-block*` directly).

## Commands

| Command      | Alias | Description                                                        |
|--------------|-------|----------------------------------------------------------------------|
| `DETAILGRID` | `DG`  | Click a frame block; insert/refresh its aligned grid.               |
| `DGRIDBLOCK` |       | Click a block instance to use as the grid block going forward.      |

## Notes

- The grid layer (`DETAIL-GRID`) is created automatically, colored magenta
  (color 6), and set non-plotting.
- Auto-import of the grid block reads the source .dwg through an
  ObjectDBX side-database, so it only pulls in that one block definition -
  it won't insert anything else from the library file.
- Requires full AutoCAD (AutoLISP customization isn't available in
  AutoCAD LT).
