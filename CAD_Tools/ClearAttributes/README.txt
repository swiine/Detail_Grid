CLEARATTS - empty all attribute values in the blocks you select

1. In AutoCAD type APPLOAD, pick ClearAttributes.lsp, click Load
   (or drag the file into the drawing).
2. Type CLEARATTS.
3. Select the blocks (window, crossing or click), press Enter.

- Every attribute value in those blocks becomes blank.
- The blocks, tags and attribute definitions stay - only the text goes.
- Blocks without attributes are ignored.
- Blocks on locked layers can't be selected - unlock the layer first.
- Constant attributes can't be changed and are left as they are.
- Fields in attributes are removed too (the value is cleared).
- One undo (U) puts everything back.
