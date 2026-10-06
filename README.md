# Der Eisendrache — Call of Duty Zombies in Civil 3D 2026

A top-down, round-based zombies survival game that runs **inside an AutoCAD Civil 3D 2026 drawing**,
loosely based on the Black Ops III map *Der Eisendrache*. The castle is drafted into model space
as real drawing objects, with a Civil 3D TIN surface for the mountain and COGO points on every
machine. Zombies, bullets and the HUD are drawn live with AutoCAD transient graphics.

![Map preview](docs/map-preview.svg)

> Fan project for fun. It is not affiliated with Activision or Treyarch and uses no game assets:
> the map is a simplified re-imagining, and the weapon stats are hand-tuned approximations.

## What's in it

| Der Eisendrache feature | In the drawing |
|---|---|
| 10 castle areas: Spawn, Lower/Upper Courtyard, Gatehouse, Armory, Bridge, Undercroft, Mission Control, Rocket Test Site, Clock Tower | Walls, solid wall fill and area labels on `DE-*` layers |
| Buyable doors (750–1500) | Crossed-out rectangles that get erased from the DWG when you buy them |
| Rounds with walkers, runners and sprinters; zombie health and counts scale each round | Zombies pathfind around walls and through any doors you've opened |
| Panzer Soldat (round 8 and then every 4–6 rounds) | Armoured boss with a flamethrower and a health bar; drops a Max Ammo |
| Power switch (Mission Control) | Perks need power, except Quick Revive |
| Perks: Juggernog, Quick Revive, Speed Cola, Double Tap, Stamin-Up, Mule Kick, Widow's Wine (4-perk limit) | Coloured machines with prices |
| 3 landing pads → Pack-a-Punch (Undercroft) | Pack-a-Punch costs 5000: 2.5× damage, bigger mag and reserve, extra pierce |
| Wall-buys: RK5, Sheiva, L-CAR 9, Kuda, KN-44, KRM-262, HVK-30, VMP, ICR-1 | Buy the gun again to refill its ammo for half price |
| Mystery Box (950) with the Ray Gun and BO3 box weapons; the teddy bear moves it | 5 possible box locations |
| Gondola between the Lower Courtyard and the Rocket Test Site | Needs power; 20 s cooldown |
| Feed the 3 dragons (kill zombies near them) → **Wrath of the Ancients** bow from the Clock Tower pedestal | Each dragon shows its range ring and a soul counter |
| Bow upgrades at 4 altars (10 bow kills each): **Storm** (chain lightning), **Fire** (burning ground), **Void** (vortex), **Wolf** (spirit wolf) | One upgrade per bow |
| Power-ups: Max Ammo, Insta-Kill, Double Points, Kaboom (nuke), Fire Sale | They blink before they expire |
| Grenades, knife, self-revive with Quick Revive (up to 3 times) | |

## Controls

| Key | Action |
|---|---|
| `W` `A` `S` `D` (or arrow up/down) | Move |
| Mouse (or arrow left/right) | Aim |
| Left click / `Space` / `F` | Fire (hold for automatic weapons; click again for semi-auto) |
| `R` | Reload |
| `Q` | Swap weapon |
| `E` | Buy / use (doors, perks, wall-buys, box, power, pads, Pack-a-Punch, gondola, bow) |
| `V` / right click | Knife |
| `G` | Grenade |
| `P` | Pause (the game also pauses when Civil 3D isn't the active window) |
| `Esc` | Quit |
| `Enter` | Play again after game over |

While the game is running it swallows those keys and the mouse buttons, so AutoCAD doesn't select
or run commands. The mouse wheel and middle-button pan still work. Press `Esc` to get control back.

## Build and run

You need the **.NET 8 SDK**. Civil 3D does **not** have to be installed to build: the AutoCAD and
Civil 3D APIs come from Autodesk's official NuGet reference packages (`AutoCAD.NET` 25.1.0 and
`Civil3D.NET` 13.8.280, the 2026 releases). They are compile-only, so Civil 3D supplies the real DLLs
at run time.

```powershell
git clone <this repo>
cd Detail_Grid
dotnet build src/DerZombies.Civil3D -c Release
```

The output is a single file, `src\DerZombies.Civil3D\bin\Release\net8.0-windows\DerZombies.Civil3D.dll`.
The game engine is compiled into it.

In Civil 3D:

1. Start a **new, empty drawing**. The castle is drawn at 0,0 on `DE-*` layers.
2. Run `NETLOAD` and pick `src\DerZombies.Civil3D\bin\Release\net8.0-windows\DerZombies.Civil3D.dll`.
3. Run **`DERZOMBIES`**, then click once in the drawing so it has focus.

### Commands

| Command | What it does |
|---|---|
| `DERZOMBIES` | Draws the castle, the Civil 3D surface and points, zooms to it and starts a game |
| `DERZQUIT` | Stops the game (same as `Esc`) |
| `DERZMAP` | Draws the castle, surface and points without playing, e.g. to plot the "site plan" |
| `DERZCLEAN` | Erases everything on the `DE-*` layers |

## How it works

```
src/DerZombies.Core      Pure C# game engine (no Autodesk references), unit tested
  MapData.cs             The castle as a 60x40 ASCII grid + doors, perks, wall-buys, quest features
  GameMap.cs             Walkability, doors, line of sight, BFS flow field for zombie pathing
  Game.cs                Rounds, spawning, AI, weapons, perks, box, Pack-a-Punch, dragons, bow, power-ups
  Weapons.cs / Entities.cs
src/DerZombies.Civil3D   The Civil 3D 2026 plugin
  Commands.cs            DERZOMBIES / DERZQUIT / DERZMAP / DERZCLEAN
  MapBuilder.cs          Writes walls, doors, labels, machines and a title block into model space
  CivilSite.cs           TIN surface "DE - Eisendrache Mountain" + COGO points on the machines
  Renderer.cs            Transient graphics for the player, zombies, effects and HUD (nothing saved to the DWG)
  GameSession.cs         Frame loop (WinForms timer), GetAsyncKeyState input, PointMonitor aiming
tests/DerZombies.Tests   xUnit tests for the engine (map integrity, doors, perks, PaP, box, bow quest, a bot run)
tools/render_map_svg.py  Regenerates docs/map-preview.svg from MapData.cs
```

To change the map, edit the ASCII layout in `MapData.cs`: `#` is a wall, capital letters are areas,
and digits or lowercase letters are doors. Then run the tests and `python tools/render_map_svg.py`.

Run the engine tests on any OS:

```bash
dotnet test tests/DerZombies.Tests
```

## Status

- The engine's 15 tests pass, including a bot that buys a wall gun and survives the early rounds.
- The plugin builds with no warnings against Autodesk's AutoCAD 2026 and Civil 3D 2026 reference assemblies.
- It has **not been run inside Civil 3D 2026 yet**. If anything misbehaves at run time (most likely
  the input hook, the transient refresh, or the surface and COGO calls), please report it. The
  Civil 3D surface and points are wrapped so that a failure there won't stop the game.
