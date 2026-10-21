# Detail_Grid
## DeleteUnusedScales.lsp

AutoLISP routine that removes annotation scales nothing in the drawing uses.

1. Load `DeleteUnusedScales.lsp` with `APPLOAD`.
2. Run `LISTUNUSEDSCALES` to see what would be removed (changes nothing).
3. Run `DELUNUSEDSCALES` to delete them; it lists the scales and asks for confirmation.

Kept: the 1:1 scale, the current annotation scale (`CANNOSCALE`), scales assigned to
annotative objects, attributes or styles, and scales matching a viewport's scale.
The deletion is one undo step.
