# Detail_Grid
## MeshMustBeDestroyed.lsp

AutoLISP command that converts polygon meshes, polyface meshes, 3D faces and
MESH objects into 3D polylines (one polyline per face).

1. Load it with `APPLOAD` (or `(load "MeshMustBeDestroyed.lsp")`).
2. Run `MeshMustBeDestroyed` and select the objects.
3. Choose which edges to trace:
   - **All** (default): each face becomes a closed 3D polyline.
   - **Visible**: hidden edges are left out, so a face can become one or more open polylines.
4. Choose whether to delete the source objects.

New polylines keep the source object's layer, linetype, color, lineweight and linetype scale.

## ZEROELEV.lsp

AutoLISP command that moves objects to elevation 0 by setting the Z of every point to 0.

1. Load it with `APPLOAD` (or `(load "ZEROELEV.lsp")`).
2. Run `ZEROELEV`, then select objects or press Enter to process the whole drawing.
3. Choose whether to also flatten block definitions. This changes every insert of those blocks; xrefs are never touched.

- Lines, 3D polylines, 3D faces, meshes and splines are projected flat onto Z=0.
- Polylines, blocks (and their attributes), text, hatches, dimensions and so on get their elevation set to 0.
- Planar objects drawn in a tilted plane are skipped, because setting Z to 0 can't put them at elevation 0.
- Flat objects without editable points (regions, tables, images, multileaders) are moved down to Z=0.
- The `ELEVATION` system variable is set to 0.
- A summary lists what changed, and what was skipped or failed (for example, objects on locked layers).
