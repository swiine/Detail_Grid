# Detail_Grid

## Block Layer Changer (Civil 3D 2026 plugin)

Finds objects inside **block definitions** by **colour** (or by layer), moves them to a
layer you choose, and resets their colour, linetype and lineweight to **ByLayer**. It works
on many blocks at once from a pop-up dialog.

### Using it

1. In Civil 3D 2026, run `NETLOAD` and pick `dist/BlockLayerChanger.dll` (prebuilt).
2. Type **`BLC`** (or `BLOCKLAYERCHANGE`) to open the dialog.
3. **Blocks to search**: tick the blocks, use *All*, type in *Filter*, or click
   *Pick in drawing* to select inserts on screen.
4. **Find by Colour / Layer**: the list shows every colour (with a swatch) or layer used
   inside the ticked blocks, and how many blocks use it. Tick the ones to change.
5. **Put them on layer**: pick an existing layer or type a new name to create it.
   Leave it blank to keep each object's layer and only reset it to ByLayer.
6. Click **Apply**. A summary appears in the dialog and on the command line. The dialog
   stays open, so you can do the next colour straight away.

Example: tick *All* blocks, tick colour **10**, type layer **example 1**, Apply. Every
object that is colour 10 in every block goes to `example 1` with colour, linetype and
lineweight set to ByLayer. Then tick colour **1**, choose another layer, Apply again.

Options:
- **Set colour, linetype & lineweight to ByLayer**: on by default, applied to every
  object that matches.
- **Include nested blocks**: also searches blocks inside the ticked blocks.
- **Include attributes on inserted blocks**: attribute values are stored on each insert,
  not in the block definition, so this updates those too.

Notes:
- Colour matching uses the object's own colour setting. An object that is *ByLayer* on
  a layer coloured 10 shows up as **ByLayer**, not **10**.
- Dynamic blocks are handled, including the hidden `*U` copies AutoCAD creates.
- Objects on locked layers are changed too.
- Xrefs and layouts are skipped.
- Type `U` after closing the dialog to undo the run.

### Building

A prebuilt DLL is in `dist/`. To rebuild after changing the code, double-click
**`build.bat`**. It needs the free [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
and copies the new DLL into `dist/`.

The project targets **Civil 3D 2026** (AutoCAD 2026, .NET 8, `AutoCAD.NET` 25.1.0).
It only uses the core AutoCAD API, so it also loads in plain AutoCAD 2026.

If Civil 3D blocks the DLL at load time, add its folder to `TRUSTEDPATHS`
(Options > Files > Trusted Locations).

To load it automatically at startup, add `(command "NETLOAD" "C:/path/to/BlockLayerChanger.dll")`
to your `acaddoc.lsp`, or add the DLL to the APPLOAD *Startup Suite*.
