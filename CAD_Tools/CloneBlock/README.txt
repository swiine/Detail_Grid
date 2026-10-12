CLONEBLOCK - make numbered 1:1 copies of a block

1. In AutoCAD type APPLOAD, pick CloneBlock.lsp, click Load
   (or drag the file into the drawing).
2. Type CLONEBLOCK.
3. Click the block (or press Enter and type its name).
4. Enter the first new number (defaults to the next one, e.g. 10 after 9)
   and the last new number (e.g. 21).

Example: ..._Revision_Text9 -> ..._Revision_Text10 to ..._Revision_Text21

- The copies are exact: same geometry, attributes, base point, layers.
- Names that already exist are skipped, never overwritten.
- New blocks are added to the drawing's block list; use INSERT to place them.
- Leading zeros are kept (09 -> 10, 11; 009 -> 010).
- Dynamic blocks: grips/parameters are not copied.
- One undo (U) removes everything it made.
- Save the drawing afterwards. To get the blocks into other drawings,
  copy them in from this one (or use it as the BlockSwap template).
