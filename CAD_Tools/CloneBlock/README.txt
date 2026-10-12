CLONEBLOCK - make an exact copy of a block under the next number

1. In AutoCAD type APPLOAD, pick CloneBlock.lsp, click Load
   (or drag the file into the drawing).
2. Type CLONEBLOCK.
3. Click the block (or press Enter and type its name).
4. How many new blocks? Press Enter for 1.
5. Place them now? Press Enter (Yes), then click where each one goes.
   Scale 1, rotation 0. If you clicked a block in step 3, its attribute
   values are copied onto the new one; if you typed the name, the
   attribute defaults are used.

Naming: counts up from 1 and uses the first number not already taken.
  ..._Text1 to ..._Text9 exist  ->  new block ..._Text10
  block called TITLE (no number) ->  new block TITLE1

- The copy is exact: same geometry, same attributes (tags, prompts,
  defaults), same base point and layers. Only the name changes.
- Existing blocks are never overwritten.
- Answer No at step 5 to just add them to the block list (INSERT later).
- Dynamic blocks: grips/parameters are not copied.
- One undo (U) removes everything it made.
