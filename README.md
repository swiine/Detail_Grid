# Detail_Grid

## Block Layer Changer (AutoCAD plugin)

Finds objects on specific layers **inside block definitions** and moves them to a layer
you choose. It works on many blocks at once from a pop-up dialog.

### Using it

1. In AutoCAD, run `NETLOAD` and pick `BlockLayerChanger.dll`.
2. Type **`BLC`** (or `BLOCKLAYERCHANGE`) to open the dialog.
3. **Blocks to search**: tick the blocks, use *All*, type in *Filter*, or click
   *Pick in drawing* to select inserts on screen.
4. **Layers to find**: lists every layer used inside the ticked blocks, with how many
   blocks use it. Tick the ones to change.
5. **Change them to layer**: pick an existing layer or type a new name. A new name
   creates the layer.
6. Click **Change Layers**. A summary appears in the dialog and on the command line.
   You can keep going with other blocks/layers, then close the dialog.

Options:
- **Include nested blocks**: also searches blocks inside the ticked blocks.
- **Also change attributes on inserted blocks**: attribute values are stored on each
  insert, not in the block definition, so this updates those too.

Notes:
- Dynamic blocks are handled, including the hidden `*U` copies AutoCAD creates.
- Objects on locked layers are changed too.
- Xrefs and layouts are skipped.
- Type `U` once after closing the dialog to undo everything from that run.
- Objects on layer `0` inside a block take the layer of the insert. If you move them
  off `0`, they keep the new layer instead.

### Building

You need Visual Studio 2022 (or the .NET 8 SDK) on Windows.

```
dotnet build BlockLayerChanger/BlockLayerChanger.csproj -c Release
```

The DLL goes to `BlockLayerChanger/bin/Release/net8.0-windows/`.

The project targets **AutoCAD 2025/2026**. For **AutoCAD 2021–2024**, edit
`BlockLayerChanger.csproj`: set `TargetFramework` to `net48` and the `AutoCAD.NET`
package version to match your release (e.g. `24.3.0` for 2024).

To load it automatically at startup, add `(command "NETLOAD" "C:/path/to/BlockLayerChanger.dll")`
to your `acaddoc.lsp`, or add the DLL to the APPLOAD *Startup Suite*.
