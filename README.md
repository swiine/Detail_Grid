# Detail_Grid
## MESH2PL.lsp

AutoLISP command that converts polygon meshes, polyface meshes, 3D faces and
MESH objects into 3D polylines (one polyline per face).

1. Load it with `APPLOAD` (or `(load "MESH2PL.lsp")`).
2. Run `MESH2PL` and select the objects.
3. Choose which edges to trace:
   - **All** (default): each face becomes a closed 3D polyline.
   - **Visible**: hidden edges are left out, so a face can become one or more open polylines.
4. Choose whether to delete the source objects.

New polylines keep the source object's layer, linetype, color, lineweight and linetype scale.
