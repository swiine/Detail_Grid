# Civil 3D FIFA

An 11-a-side football game that runs inside **Civil 3D 2026** (or any AutoCAD 2026-based product).

- `FIFA` draws a regulation 105 × 68 m pitch into model space, with mown grass stripes, penalty areas, arcs and goals.
- The players and ball move as transient graphics, so the game never writes to your drawing or undo history while you play.
- A small control window runs the match. It reads your keyboard and shows the score and clock.
- When you quit, the pitch is erased again.

## Build

Requirements: Windows and the .NET 8 SDK. The AutoCAD 2026 API comes from NuGet.

```
cd CivilFifa
dotnet build -c Release
```

The output is `CivilFifa/bin/Release/net8.0-windows/CivilFifa.dll`.

## Run

1. Open Civil 3D 2026 and any drawing (a blank one is best).
2. Type `NETLOAD` and pick `CivilFifa.dll`. If Civil 3D shows a security prompt, choose **Load**.
3. Type `FIFA`.
4. Pick a centre point for the pitch, or press Enter for 0,0.
5. Choose a difficulty: Easy, Normal or Hard.
6. Keep the **Civil 3D FIFA** window focused while you play. Clicking back into the drawing pauses the game.

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
