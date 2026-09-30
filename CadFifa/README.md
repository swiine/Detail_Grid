# CAD FIFA

An 11-a-side football game that runs inside AutoCAD.

It uses only the core AutoCAD .NET API, so it works in plain **AutoCAD** and in any product built on it, such as Civil 3D, AutoCAD Architecture, AutoCAD Mechanical, AutoCAD Map 3D and AutoCAD MEP.

| AutoCAD version | Load this DLL |
| --- | --- |
| 2025, 2026 | `net8.0-windows/CadFifa.dll` |
| 2021, 2022, 2023, 2024 | `net48/CadFifa.dll` |

AutoCAD LT can't run it, because LT doesn't support .NET plugins.

## What it does

- `FIFA` draws a regulation 105 × 68 m pitch into model space, with mown grass stripes, penalty areas, arcs and goals.
- The players and ball move as transient graphics, so the game never writes to your drawing or undo history while you play.
- A small control window runs the match. It reads your keyboard and shows the score and clock.
- When you quit, the pitch is erased again.

## Build

Requirements: Windows and the .NET 8 SDK. The AutoCAD API comes from NuGet, so AutoCAD doesn't need to be installed to build.

```
cd CadFifa
dotnet build -c Release
```

This builds both DLLs:

- `CadFifa/bin/Release/net8.0-windows/CadFifa.dll`
- `CadFifa/bin/Release/net48/CadFifa.dll`

## Run

1. Open AutoCAD and any drawing (a blank one is best).
2. Type `NETLOAD` and pick the DLL for your version from the table above. If AutoCAD shows a security prompt, choose **Load**.
3. Type `FIFA`.
4. Pick a centre point for the pitch, or press Enter for 0,0.
5. Choose a difficulty: Easy, Normal or Hard.
6. Keep the **CAD FIFA** window focused while you play. Clicking back into the drawing pauses the game.

## Controls

| Key | Action |
| --- | --- |
| WASD / arrow keys | Move |
| Shift | Sprint |
| Space (hold, then release) | Shoot. Hold longer for more power; W/S or Up/Down aims high or low in the goal. Without the ball, it tackles. |
| E | Pass (to the best teammate in the direction you're facing) |
| Q | Switch to the player nearest the ball |
| P | Pause |
| R | Restart the match |
| Esc | Quit and remove the pitch |

You play as **red**, attacking to the right. A match lasts 4 real minutes, shown as 90'. Throw-ins, corners, goal kicks, keeper saves and kick-offs after goals are all handled.

## Code layout

| File | Purpose |
| --- | --- |
| `Match.cs` | The game engine: physics, rules and AI. It has no AutoCAD dependencies. |
| `Pitch.cs` | Draws the pitch as real lines, arcs and hatches on the `FIFA-PITCH` and `FIFA-GRASS` layers. |
| `Renderer.cs` | Draws players and the ball (as filled donuts) and the scoreboard using `TransientManager`. |
| `GameWindow.cs` | The modeless WinForms window. It runs the game loop and handles keyboard input. |
| `Commands.cs` | The `FIFA` command. |
