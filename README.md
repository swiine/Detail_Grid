# BlockCraft for Civil 3D

A Minecraft-style voxel game that runs **inside Autodesk Civil 3D**. Play on a
random procedural world, or turn one of your Civil 3D surfaces into a world
(alignments become asphalt roads). You can then bring whatever you built back
into the drawing as 3D solids.

![A Civil 3D surface turned into a BlockCraft world](docs/surface-world.png)

*A sample site. The terraces are the surface's contours, the road follows an
alignment, and the house was built in-game.*

## Commands

| Command | What it does |
| --- | --- |
| `BLOCKCRAFT` | Starts a procedural world. Asks for a seed (press Enter for a random one). |
| `BLOCKCRAFTSURFACE` | Pick a TIN or grid surface to build a world from it. It asks for the block size (in drawing units), the vertical exaggeration, and whether to pave alignments as roads. |
| `BLOCKCRAFTRESUME` | Goes back into the last world you played this session. |
| `BLOCKCRAFTEXPORT` | Adds every block you placed to model space as a 3D solid, at its real drawing coordinates, on a `BLOCKCRAFT-<TYPE>` layer. |

In a surface world, each block column is sampled at its centre with
`FindElevationAtXY`. The block's top face sits on the surface elevation.
Columns outside the surface boundary are left empty. The HUD shows your
position in drawing X/Y/Z, so you can tell where you are on the site.

## Controls

| Input | Action |
| --- | --- |
| Click | Capture the mouse and start playing |
| W A S D / arrows | Move |
| Mouse | Look |
| Space | Jump (or go up while flying) |
| Shift | Go down while flying |
| Ctrl | Sprint |
| F | Toggle flying |
| Left click (hold) | Break a block |
| Right click (hold) | Place a block |
| Middle click | Pick the block you're looking at |
| 1-9 / mouse wheel | Choose a block |
| + / - | Raise or lower render quality |
| F1 | Show or hide help |
| Esc | Release the mouse, then press again to return to the drawing |

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
`NETLOAD` the `BlockCraft.Civil3D.dll` file inside `Contents\`.

In plain AutoCAD 2025/2026, `BLOCKCRAFT` still works. `BLOCKCRAFTSURFACE`
tells you that it needs Civil 3D.

You can also play without Civil 3D:

```powershell
dotnet run --project src\BlockCraft.Standalone -- 1234   # optional seed
```

## Project layout

| Project | Contents |
| --- | --- |
| `src/BlockCraft.Core` | Engine with no UI or Autodesk dependencies: voxel world, terrain generation, player physics, block ray casting, and a multi-threaded software ray-cast renderer. No GPU is needed, so there is no DirectX or OpenGL conflict inside Civil 3D. |
| `src/BlockCraft.UI` | WinForms game window: game loop, mouse-look, input handling, and the HUD. |
| `src/BlockCraft.Civil3D` | Civil 3D commands, surface and alignment sampling, and the 3D solid exporter. |
| `src/BlockCraft.Standalone` | Launches the game on its own, outside Civil 3D. |
| `tests/BlockCraft.Core.Tests` | xUnit tests for the engine. |

## Notes and limits

* The game window is modal, so Civil 3D waits while you play. Close the window
  (Esc twice) to get back to the drawing.
* Surface worlds are limited to 512 × 512 columns and 256 layers. If your
  inputs would exceed that, the block size or vertical scale is adjusted
  automatically and the command tells you.
* `BLOCKCRAFTEXPORT` exports every block that is currently placed each time you
  run it. Running it twice creates duplicates.
* Worlds are kept only for the current Civil 3D session. They are not saved to
  disk.
