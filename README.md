# Detail_Grid
## ScaleDelete.lsp

AutoLISP routine that removes annotation scales nothing in the drawing uses.

1. Load `ScaleDelete.lsp` with `APPLOAD`.
2. Run `LISTUNUSEDSCALES` to see what would be removed (changes nothing).
3. Run `SCALEDELETE` to delete them; it lists the scales and asks for confirmation.

Kept: the 1:1 scale, the current annotation scale (`CANNOSCALE`), scales assigned to
annotative objects, attributes or styles, and scales matching a viewport's scale.
The deletion is one undo step.
