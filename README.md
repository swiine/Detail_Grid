# BlockCraft for Civil 3D

A Minecraft-style block world that lives **in the drawing** and that you play
**in the AutoCAD viewport itself**. There is no separate game window. You can
generate the terrain from random noise or from one of your surfaces
(alignments become asphalt roads). Every visible block is an ordinary block
reference snapped to a grid, so you can switch freely between two modes:

* **Play:** `BCPLAY` puts your character into the world, in a third-person
  view from behind the shoulder (V switches to first person). You get WASD,
  mouse-look, gravity and collisions. Clicking breaks and places the real
  blocks in the drawing.
* **Draft:** press Esc and you're back to normal AutoCAD. Edit the same
  blocks with `BCBREAK`/`BCPLACE`, or use `ERASE`/`COPY`/`MOVE`/`ARRAY`
  followed by `BCSYNC`.

When you're done, `BCTOSURFACE` turns the terrain back into a TIN surface.

![A Civil 3D surface turned into a BlockCraft world](docs/surface-world.png)

*A preview render from the engine. The terraces are the surface's contours,
the road follows an alignment, and the house was built from placed blocks.*

## Commands

| Command | What it does |
| --- | --- |
| `BLOCKCRAFT` | Lists the commands. |
| `BCNEW` | Generates a random world in model space. Asks for a seed (press Enter for a random one), the world size (16-256 blocks), the block size, and the south-west corner point. |
| `BCSURFACE` | Pick a TIN or grid surface to build a world from it. Asks for the block size, the vertical exaggeration, and whether to pave alignments as roads. |
| `BCPLAY` | Switches into play mode in the viewport. If the drawing has no world yet, it offers to generate a random one first. |
| `BCBREAK` | Select blocks (window and crossing selections work) to dig them out. The blocks underneath are drawn as they are exposed. |
| `BCPLACE` | Pick blocks to build onto. `Type` sets the block material. `Direction` is either `Auto` (the face pointing at you) or `Top`/`Bottom`/`North`/`South`/`East`/`West`. |
| `BCSYNC` | Run this after `ERASE`, `COPY`, `MOVE` or `ARRAY` on `BC_*` blocks. It reads those edits back into the world and draws any blocks that are newly exposed. |
| `BCTOSURFACE` | Creates a Civil 3D TIN surface from the top of each block column. Trees and water are ignored. |
| `BCCLEAR` | Removes the world and all of its blocks from the drawing. |

## Play mode controls (BCPLAY)

| Input | Action |
| --- | --- |
| W A S D / arrows | Move |
| Mouse | Look (AutoCAD's crosshair stays centred and acts as the aim point) |
| V / F5 | Switch between third person (you can see your character) and first person |
| Space | Jump (or go up while flying) |
| Shift | Go down while flying |
| Ctrl | Sprint |
| F | Toggle flying |
| Left click (hold) | Break a block |
| Right click (hold) | Place a block |
| Middle click | Pick the block you're looking at |
| 1-9 / mouse wheel | Choose a block (shown on the command line) |
| Tab | Pause and free the mouse. Click the drawing to resume. |
| Esc | Return to drafting. The world is saved and your previous view is restored. |

Starting any other command while play mode is paused also switches you back
to drafting. The targeted block is outlined. Play mode switches the viewport
to a perspective view with the Realistic visual style.

The character (blocky head, body, swinging arms and legs) is drawn as
temporary on-screen graphics. It is never added to the drawing. In third
person the camera pulls in closer when a wall is in the way.

## How it's stored

* A drawing holds one world. The full voxel grid is saved, compressed, in the
  drawing's named object dictionary. It survives saving and reopening the DWG,
  and it follows `UNDO`.
* Only the visible shell is drawn: about 15,000 block references for the
  default 96 × 96 world, and about 58,000 at 192 × 192. Buried blocks stay as
  data until you dig down to them.
* Each block type is a 1 × 1 × 1 block definition (`BC_GRASS`, `BC_STONE`,
  ...). Its references are scaled to the cell size and placed on a
  `BLOCKCRAFT-<TYPE>` layer.
* A reference belongs to whichever voxel its insertion point is nearest.
  When you copy or move blocks, snap to their corners (Endpoint osnap) so
  they land on the grid.

## Build and install

Requirements: Civil 3D 2025 or 2026 (both run on .NET 8) and the .NET 8 SDK on
Windows. The Autodesk API assemblies come from Autodesk's official
`AutoCAD.NET` and `Civil3D.NET` NuGet packages. They're only needed to
compile and are never copied to the output.

```powershell
dotnet build BlockCraft.sln -c Release
```

The build creates an autoloader bundle at
`src\BlockCraft.Civil3D\bin\Release\net8.0-windows\BlockCraft.bundle`.
To install it, copy that folder to
`%APPDATA%\Autodesk\ApplicationPlugins\` and restart Civil 3D. You can also
`NETLOAD` `BlockCraft.Civil3D.dll`, keeping `BlockCraft.Core.dll` next to it.

In plain AutoCAD 2025/2026, everything except `BCSURFACE` and `BCTOSURFACE`
works. Those two need Civil 3D.

## Project layout

| Project | Contents |
| --- | --- |
| `src/BlockCraft.Core` | Engine with no UI or Autodesk dependencies: voxel world, terrain generation, player physics, block ray casting, world storage, and the visible-shell calculation. |
| `src/BlockCraft.Civil3D` | Civil 3D commands, the drawing-backed world (storage, block references, sync), in-viewport play mode, surface and alignment sampling, and TIN surface output. |
| `src/BlockCraft.UI`, `src/BlockCraft.Standalone` | A software-rendered game window and launcher, used for engine development and preview renders. They are not part of the plug-in. |
| `tests/BlockCraft.Core.Tests` | xUnit tests for the engine. |

## Notes and limits

* Play mode moves the real AutoCAD camera. Its frame rate depends on your
  graphics card and how many blocks are drawn, so smaller worlds feel
  smoother.
* Surface worlds are limited to 256 × 256 columns and 256 layers. If your
  inputs would exceed that, the block size or vertical scale is adjusted
  automatically and the command tells you.
* Any command that would draw more than 120,000 blocks asks you to confirm
  first.
