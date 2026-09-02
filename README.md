# Detail_Grid

An AutoLISP tool for AutoCAD: click a frame block (the block used to align
a viewport to your title block) and it automatically drops in a grid block,
matched to the frame's insertion point, rotation, and scale - so your detail
grid always lines up, no matter what sheet scale the frame is at.

## How it works

1. You draw your own "grid module" as a block (name it `GRID_LINE`, or use
   whatever name you like and point DetailGrid at it - see below). Draw it
   at true 1:1 size, with the insertion point set to whichever corner or
   center you want anchored to the frame.
2. Run `DETAILGRID` and click the frame block.
3. DetailGrid reads the frame's scale - its annotation scale if it's an
   annotative block, otherwise its X/Y scale factor - along with its
   rotation and insertion point, and inserts the grid block at that exact
   point/rotation/scale on its own non-plotting layer (`DETAIL-GRID`).
4. Run it again on the same frame any time you move/rescale it - the old
   grid for that frame is removed first, so you don't end up with stacked
   duplicates.

## Setup

1. In your drawing (or template), create the grid block you want to use
   for lining up details. Default expected name is `GRID_LINE`.
2. Load `DetailGrid.lsp`:
   - `APPLOAD` it for the current session, or
   - Add it to your Startup Suite, or
   - Add `(load "DetailGrid.lsp")` to `acaddoc.lsp` so it's available in
     every drawing automatically.
3. If your grid block isn't named `GRID_LINE`, run `DGRIDBLOCK` once and
   click an instance of it - DetailGrid will remember it for the session.
   (Or just edit `*dg:grid-block*` near the top of the .lsp file.)

## Commands

| Command      | Alias | Description                                                        |
|--------------|-------|----------------------------------------------------------------------|
| `DETAILGRID` | `DG`  | Click a frame block; insert/refresh its aligned grid.               |
| `DGRIDBLOCK` |       | Click a block instance to use as the grid block going forward.      |

## Notes

- The grid layer (`DETAIL-GRID`) is created automatically, colored magenta
  (color 6), and set non-plotting.
- Requires full AutoCAD (AutoLISP customization isn't available in
  AutoCAD LT).
