CLONEBLOCK - make an exact copy of a block under the next number

1. In AutoCAD type APPLOAD, pick CloneBlock.lsp, click Load
   (or drag the file into the drawing).
2. Type CLONEBLOCK.
3. Click the block (or press Enter and type its name).
4. How many new blocks? Press Enter for 1.

Naming: counts up from 1 and uses the first number not already taken.
  ..._Text1 to ..._Text9 exist  ->  new block ..._Text10
  block called TITLE (no number) ->  new block TITLE1

- The copy is exact: same geometry, same attributes (tags, prompts,
  defaults), same base point and layers. Only the name changes.
- Existing blocks are never overwritten.
- New blocks are added to the drawing's block list; use INSERT to place them.
- Dynamic blocks: grips/parameters are not copied.
- One undo (U) removes everything it made.
