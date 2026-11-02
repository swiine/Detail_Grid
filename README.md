# CivDOOM

A Doom-style first-person shooter that runs **inside Civil 3D**. Type `CIVDOOM` and the linework in
your drawing — lines, polylines, arcs, blocks, alignments, feature lines, parcels — becomes the walls
of a level, and the AutoCAD viewport itself turns into the game: the camera walks through your
drawing in perspective while you shoot imps in it.

`CIVDOOMGEN` draws a random level into the drawing as ordinary polylines and blocks, so you can edit
the level with normal drafting commands before playing it.

![In-game view](docs/screenshot-level.png)
![Enemies and pickups](docs/screenshot-enemies.png)

## How it works

* **Walls come from your drawing.** Every curve becomes a full-height wall, textured with its
  entity/layer colour. Blocks and Civil 3D objects (alignments, feature lines, parcels, survey
  figures) are exploded *in memory* — the drawing itself is never modified.
* **Enemies come from points.** Any `POINT` or COGO point becomes an enemy spawn (every fifth is a
  tougher Brute). If the drawing has none, enemies and pickups are auto-placed in the areas you can
  actually walk to.
* **The floor is a CAD grid.** One major grid line per wall-height, which makes it easy to judge scale.
* The engine is a software ray caster that works on arbitrary line segments (not a tile grid), with a
  spatial index so a 50,000-segment site plan still renders in ~3 ms per frame.

## Objectives: bosses, gates and the finish line

![The five bosses](docs/screenshot-bosses.png)

Levels can have a **finish line** (`DOOM-EXIT`). It sits behind a **locked gate** (any linework on the
`DOOM-GATE` layer, drawn with yellow/black hazard stripes) that opens when the bosses are beaten:
`DOOM-EXIT` needs **all** bosses dead, `DOOM-EXIT-FINAL` only the **final** boss (the one nearest the
finish). Walking onto the open finish line wins. Without a finish line, the old rule applies (kill everything).

Five bosses ship as monster files with `boss = yes` — The Blue Screen, Infinite Regen, The Corrupted DWG,
Unhandled Exception and Licence Expired. Bosses never appear as random monsters; `DOOM-BOSS` places a random
one, and each fight in a level gets a different boss. They can fire several shots per attack (`shots`,
`shot spread`, `shot color`). The HUD shows the current objective and a boss health bar.

### Generated levels

![A generated large level](docs/generated-level.png)

`CIVDOOMGEN` builds a **linear** level — start, rooms, boss arena, rooms, boss arena, …, gated finish
room — with straight corridors and no way around an arena. Small levels have 1–2 boss fights, Medium
2–3 and Large 4–5. Walls are closed polylines with one constant thickness (a double line; set the
thickness when generating, 0 for single lines) and one wall height for the whole level.

## Vertical areas: ledges, balconies and towers

![Platforms: a stepped tower, the view from the top, and a balcony](docs/platforms.png)

Levels can have raised floors you **jump** up onto (`Space`). A jump lifts you a bit over half a wall
height; low steps (under 0.16 of a wall) you just walk up, like stairs. You can walk off any edge.

* **Balconies** run along a room's wall, 0.4 of a wall high: jump up to grab the prize on them.
* **Stepped towers** are three jumps (0.2, 0.4, 0.6) with a weapon or item on top.
* **Boss arenas** get raised corners: high ground to fight from.
* Items up on a ledge can only be picked up from the top.
* Walking monsters can't climb ledges (fliers float over them), so high ground is safe from melee —
  but not from fireballs, which aim up and down at you. Your rockets and flames aim up and down too.

In a drawing, a platform is a **closed polyline (or circle) on the `DOOM-PLATFORM` layer**. Its
**Elevation** is the height of the top in drawing units, measured from the `DOOM-START` block's Z
(with 10-unit walls, elevation 4 is a jump-up ledge; 1.5 is a step). Overlapping platforms stack: the
highest one wins. `CIVDOOMGEN` draws its platforms this way, so you can `STRETCH`, `MOVE` or change the
elevation of them in Properties like anything else.

## CoWork (chat and multiplayer drafting)

A separate Civil 3D tool in this repo (`src/CoWork`, `src/CoWork.Core`): chat rooms and live
multiplayer drafting. It has its own folder, settings, commands and status-bar icon, and refuses any folder whose
path contains "TTWChat", so it never touches another chat tool. It has nothing to do with the game.

**Licences and updates:** only people you approve can use CoWork, and new versions reach everyone the next time
they restart Civil 3D (Civil 3D loads a small signed-update loader, `src/CoWork.Loader`). Approving people, revoking
licences and publishing updates are done with the CoWork Admin tool (`tools/CoWork.Admin`), which needs the private
signing key - never committed here. See [docs/LICENSING-AND-UPDATES.md](docs/LICENSING-AND-UPDATES.md).

**Install:** close Civil 3D, run `Install-CoWork.cmd` (copies `CoWork.bundle` to
`%APPDATA%\Autodesk\ApplicationPlugins\`), start Civil 3D. `Uninstall-CoWork.cmd` removes it.
The shared folder (a `CoWork-data` folder on a network drive) is set in `CoWork.bundle\Contents\cowork.json`;
everyone needs write access, and CoWork creates the folder the first time if it's missing.

**The CoWork strip:** a slim strip of icons docked to the edge of the screen (an AutoCAD palette, so it docks
left or right, floats and auto-hides like Properties). Click an icon and its page slides out; it
tucks away again when the mouse leaves it, unless you pin it open:
Chat (with an unread count), Friends (with how many are online), CoWork (start, leave or join a live session),
Teams, then **your own command buttons** (Save, Plot, Purge all and Reload xrefs to start with; **+** adds any
command, right-click one to remove it), and Settings at the bottom (name, pop-ups, colour scheme, crosshair colour). `CW`
or `CWCHAT` brings the strip back if it's closed; the status-bar icon opens the chat.

**Chat:** everyone is in **General**; opening a drawing under `...\jobs\<yy>\<job>\...` adds that job's room. You
see who's here, messages show the drawing they were sent from (click to open it), and new messages pop up from the
status bar while you're not looking at the chat. `CWNAME` sets your name, `CWFOLDER` the shared folder, `CWPOPUPS`
turns pop-ups on/off.

**CoWork (multiplayer drafting):** open a saved drawing on a network drive and click the **CoWork** icon on the strip, then **CoWork in ...** (or type `COWORK`). A
**Join CoWork** button appears in the job's room (or General). Others click it: the drawing opens read-only on
their PC and from then on objects added, changed or erased in model space sync both ways a moment after each
command finishes, and everyone's crosshair shows with their name. Only the host saves; their file includes everyone's
work. `CWLEAVE` leaves. When the host's CoWork goes away - they end the session, close the drawing or Civil 3D, get
locked out, or crash / drop off the network (no sign of them for 20 s) - every guest's copy is closed and they get a
"You have been disconnected" message, so nobody keeps drafting into a drawing that's no longer shared.

What syncs: objects in model space; **layers** (new layers, colour, linetype, lineweight, on/off, freeze, lock,
plot, description - your current layer is never frozen on you); and in Civil 3D, **COGO points** (matched by point
number: add, move, re-describe, rename, erase) and **point groups** (name, description and include/exclude rules).
Not yet (stage two): alignments, surfaces, corridors and other Civil 3D objects - you get a one-off note if you
change one during a session.

**Session controls:** guests can **Join** or just **Watch** (view only: they see changes and crosshairs but can't
change anything). On the CoWork page the host can make any guest view-only or let them edit again, **Remove** them
(their copy closes with a message), or **Make host** - CoWork saves, the host's copy closes, and the guest reopens the
drawing for editing and carries on hosting with everyone still in. While hosting, CoWork autosaves every 5 minutes
(Settings: off / 2 / 5 / 10 / 15).

**Share a view:** the pin button next to Send (or `CWSHARE`) posts "Look here" with this drawing, zoom and selected
objects; **Go to view** opens or switches to the drawing, zooms there and selects them. **Who has this open:** when
someone else has a drawing you have open, CoWork says so, and the CoWork page offers **Message** and **Ask to CoWork**.

**Problems:** CoWork logs what goes wrong (on the PC and in `CoWork-data\logs\<login>.txt`); Settings has **Send problem
report**, which also messages the `support` login from `cowork.json`. The admin tool's **R** reads reports and logs.
Each release's `whatsnew.txt` is shown once, after the update.

**Lock while editing:** whatever you've selected (or a command is changing) is yours until you let go. Everyone
else sees it in your crosshair colour, labelled "Sam is editing this", and can't select it. If two people grab the
same thing at the same moment, whoever grabbed it first keeps it. Pick your crosshair colour in Settings.

**Closing chat tabs:** the **✕** on a tab (or a middle-click) closes it - the chat and its history are kept.
**+N** at the end lists closed chats to reopen. Private and Teams chats reopen by themselves when the other
person writes, any chat reopens if you're @mentioned in it, and a job's chat reopens when you open a drawing from
that job. **General** (everyone) can't be closed.

**Crosshair colour:** 8 swatches in Settings, or **Custom colour** opens a colour wheel (plus brightness and a
`#RRGGBB` box) for any colour.

**Colours and logo:** Settings has six colour schemes (Slate & Mint is the default) plus Civil 3D's own greys and
blue (`docs/brand/palettes.png`). The logo is **Interlock** - two brackets slotting together into one square - and
takes its two colours from the scheme (`docs/brand/interlock.png`); the status-bar icon is a hand-placed 16 x 16
pixel version of it. To use your own icon instead, put `cowork-icon.png` (16 x 16, transparent background) or
`cowork-icon.ico` in `CoWork.bundle\Contents\`. Ready-made Interlock icons for every scheme are in
`docs/brand/icons/`; `node docs/brand/render.js docs/brand` redraws the sheets and icons.

**@mentions:** `@Sam`, `@SamSmith` or `@login` in a message pops up for that person even if they've turned pop-ups
off. Typing `@` suggests names.

**Friends:** the people icon on the strip (or `CWFRIENDS`) lists your friends - online or last
seen, the job and drawing they're in, and buttons to message them privately, join their CoWork session or open a
Teams chat. Add people from the online list, by clicking a name in the chat, or by login/email (`CWFRIEND`). A pop-up
says when a friend comes online. Friends are saved in your own `%APPDATA%\CoWork\settings.json`.

**Teams link (off until IT approves it):** Teams status and two-way 1:1 Teams chats in the palette, through
Microsoft Graph with the user's own sign-in. See [docs/TEAMS-IT-SETUP.md](docs/TEAMS-IT-SETUP.md) for what IT needs to
set up; once the tenant and client ids are in `cowork.json`, `CWTEAMS` signs in.

How it's stored (each person writes only their own files):

```
CoWork-data\rooms\<room>\<person>\<yyyy-MM>.jsonl   messages
CoWork-data\online\<person>.json                      who's here
CoWork-data\live\<session>\session.json               CoWork session: drawing, host, ended?
CoWork-data\live\<session>\<person>\here.json         name + crosshair
CoWork-data\live\<session>\<person>\000001.dwg/.json  that person's changes, numbered
```

## Multiplayer (co-op)

![The host's view of three other players, and a player's view of the host](docs/multiplayer.png)

Play a level together: the monsters, keycards, doors, pickups and boss gates are shared, and each of you moves
and shoots on your own computer. Games go through CivDOOM's own small server; nothing else is used or touched.

**Set up once:** run `CivDoom.Server.exe` on a PC everyone can reach (it needs only the .NET 8 runtime; leave the
window open). Everyone then runs `CIVDOOMNET` > `Server` and types that PC's name. To set it for everyone at once,
put the name in `civdoom.server.json` next to the plugin: `{ "server": "CADPC01" }`. No server? `CIVDOOMHOST`
offers to run one inside the host's Civil 3D for that game (others then enter the host's PC name).

1. **Host:** `CIVDOOMHOST` (same level choices as `CIVDOOM`). It tells you the game id.
2. **Join:** `CIVDOOMJOIN`, then press Enter to pick from the games running (or type the id). The level comes
   from the host, so you don't need the drawing.
3. Play. Other players appear as corrupted marines in their own colour with their name and health overhead, on
   the radar, and in the co-op panel (health and kills). Monsters go for whoever is nearest. If you die, **Enter**
   respawns you; the game carries on. When the host quits, the game ends for everyone.

| | |
| --- | --- |
| `CIVDOOMHOST` | Host a co-op game |
| `CIVDOOMJOIN` | Join a game (Enter to choose from the list) |
| `CIVDOOMGAMES` | List the games on the server |
| `CIVDOOMNET` | Set the server PC and your player name |
| `CIVDOOMALERTS` | Pop-ups when someone starts a game, on/off |

**Status bar icon:** CivDOOM puts a pixel skull in Civil 3D's status bar (bottom right). Click it for a menu: play
this drawing, play in a window, draw a new level, change the area, host a co-op game, or join one of the games
running (listed right there). When someone starts a co-op game, a bubble pops up with a **Join game** link.

How it works: the host's computer runs the monsters, doors and pickups and sends them out 15 times a second; each
player sends where they are, and tells the host what their shots hit, what they picked up and which doors they
opened. The server (plain TCP on port 5390, one JSON message per line) just keeps the list of games, hands joining
players the level, and passes messages between players in the same game; it stores nothing on disk.
`CivDoom.Sandbox.exe host [server]` / `join <server> [game]` does the same outside Civil 3D.

## Keycards and locked doors

![A locked red door, the red keycard, the door opened, and a blue door](docs/keycard-doors.png)

Doors in a keycard's colour (**red**, **blue**, **yellow**) stay shut until you walk up to them holding
that keycard; then every door of that colour opens. Without it you're told which card you need. Your
cards show in the status bar (right of ARMS), keycards and doors show in their colour on the radar,
and the objective and compass point you to the next card you need.

* `CIVDOOMGEN` locks the way into a few rooms: 1 door on Small levels, 2 on Medium, 3 on Large
  (red, then blue, then yellow). Each key lies before its door — often on top of a balcony or tower — and
  there's no way round a door without its key.
* Monsters can't open doors, so they stay behind them until you do.
* In a drawing: draw the door as lines on the **`DOOM-DOOR-RED`**, **`DOOM-DOOR-BLUE`** or
  **`DOOM-DOOR-YELLOW`** layer, and insert a **`DOOM-KEY-RED`** (etc.) block where the keycard lies.
* ASCII maps: `(` `[` `{` are the red, blue and yellow keys; `)` `]` `}` their doors.

## Areas (themes)

![Classic, Downtown / Industrial Estate, Desert Highway / Night City, Hell](docs/themes.png)

Each level has an area: **Classic**, **Downtown** (office towers and a skyline), **Industrial Estate**
(corrugated sheds, chimneys, cranes), **Desert Highway** (sandstone and mesas), **Night City** (glass towers,
lit windows) or **Hell**. The walls become building facades and a 360° skyline turns with you.
`CIVDOOMGEN` picks one at random; `CIVDOOMTHEME` changes it. The choice is a `DOOM-THEME-<NAME>` block in
the drawing (no block = Classic). Themes are editable folders (`themes\city\city.txt`), with optional
`skyline.png` and `wall.png` pictures.

## The finish-line cutscene

![Running off the map](docs/screenshot-cutscene.png)

Cross the open finish line and the camera pulls back while your **corrupted marine** sprints off the
map, then a LEVEL COMPLETE card shows your time, kills, bosses and damage taken. The character is
editable too (`player\player.txt`, or `player.png` with `player_run`/`player_run2` frames).

## The HUD

![The HUD in Night City](docs/screenshot-hud.png)

Pixel-art, Doom style: a status bar with big ammo and health numbers, ARMS slots, kills/bosses/time,
ammo stock by type and a live **corrupted-marine face** (looks toward whoever hit you, grins at kills and
pickups, bloodier and more corrupted as health drops). Up top: a compass with an objective marker and
distance, a rotating radar, and a boss bar with trailing damage. In combat: a dynamic crosshair, hit and
kill markers, directional damage arcs and a low-health vignette. Plus a message feed, weapon popup,
a "killed by" death screen and a graded level-complete card. `H` hides the HUD, `Tab` the radar.

![Level complete](docs/screenshot-complete.png)

## Commands

| Command | What it does |
| --- | --- |
| `CIVDOOM` | Play the drawing. Choose **Viewport** (inside the AutoCAD drawing window) or **Window** (a separate game window). |
| `CIVDOOMWINDOW` | The original pop-out version: same level options and weapons, always in its own game window (smoothest, with minimap). |
| `CIVDOOMGEN` | Draw a random level (Small/Medium/Large) as editable objects: closed polylines on `DOOM-WALLS`, plus marker blocks. |
| `CIVDOOMTHEME` | Choose (or randomise) the area the drawing's level looks like. |
| `CIVDOOMBLOCKS` | Add all the `DOOM-*` marker blocks to the drawing so you can `INSERT` them yourself. |

### Level markers

Ordinary blocks whose names say what they are — move, copy, rotate or erase them like anything else:

| Block | Meaning |
| --- | --- |
| `DOOM-START` | Where you spawn. Its rotation is the way you face; its scale is used as the default wall height. |
| `DOOM-MONSTER-IMP`, `DOOM-MONSTER-BRUTE`, … | A specific monster (one block per monster file). `DOOM-MONSTER` = random. |
| `DOOM-WEAPON-SHOTGUN`, … | A specific weapon (one per weapon file). `DOOM-WEAPON` = any. |
| `DOOM-HEALTH`, `DOOM-AMMO` | Medkit, ammo box. |
| `DOOM-KEY-RED`, `DOOM-KEY-BLUE`, `DOOM-KEY-YELLOW` | Keycards for the matching doors. |

Layers: linework on `DOOM-GATE` is a locked gate; on `DOOM-DOOR-RED` / `-BLUE` / `-YELLOW` a keycard door; closed polylines on `DOOM-PLATFORM` are raised floors
(elevation = height); everything else is a wall.

If a drawing has no monster/item/weapon markers, those are placed automatically. You can redefine the
blocks' appearance freely; only the names matter.

### Playing in the viewport

Walls are shown as temporary 3D faces standing on your linework (the drawing is never modified), the
viewport switches to a perspective *Realistic* view that follows you, and monsters, items and your gun
are drawn as pixel billboards. Health and ammo appear as bars at the bottom of the view and in the
status bar; messages go to the command line. Keyboard and mouse are captured while you play, and `Esc`
restores your original view. This mode is experimental: expect a lower frame rate than the window mode
on big drawings.

## Using it

1. Build (see below), then in Civil 3D run `NETLOAD` and pick
   `src/CivDoom.Civil3D/bin/Release/net8.0-windows/CivDoom.Civil3D.dll`
   (or install the autoloader bundle, below).
2. Run **`CIVDOOM`** and answer the prompts:

   | Prompt | Meaning |
   | --- | --- |
   | `Level source [Drawing/Selection/Demo]` | **Drawing**: everything in model space on visible layers. **Selection**: only what you pick. **Demo**: a built-in level. |
   | `Wall height in drawing units` | Sets the scale. Default is guessed from `INSUNITS` (10 for feet, 3 for metres, …). Bigger = you feel smaller. |
   | `Pick player start` / `Pick a point to face` | Where you spawn and which way you look. Enter to use the middle of the drawing, facing east. |

### Controls

| | |
| --- | --- |
| `W` `A` `S` `D` / arrows | Move / strafe / turn |
| Mouse | Turn (click the window first to capture the mouse) |
| Left click / `Ctrl` | Fire |
| `Space` | Jump |
| `1`–`9` / mouse wheel | Switch weapon (press a number again to cycle weapons sharing it) |
| `Shift` | Run |
| `Tab` / `M` | Toggle minimap |
| `Enter` | Restart after winning or dying |
| `F5` | Reload the monster and weapon files |
| `Esc` | Release the mouse, press again to return to Civil 3D |

Clear every hostile to win. Medkits heal 25, ammo boxes give 20 rounds.

**Tips:** floor plans and building footprints make the best levels. On a large site plan use
*Selection* to pick one area, and put down a few `POINT`s where you want enemies.

## Weapons

You start with the pistol; the rest are lying around the level (in drawings, one of each is placed
automatically).

| Key | Weapon | |
| --- | --- | --- |
| 1 | Chainsaw / Katana | Melee, never run out. The katana sweeps everything in front of you. |
| 2 | Pistol | |
| 3 | Shotgun | 7 pellets per shot |
| 4 | Chaingun | Very fast |
| 5 | Rocket Launcher | Splash damage — including to you |
| 6 | Flamethrower | Short-range stream of fire |

Weapons are editable text files too (in a `weapons` folder next to the plugin), in the same format
as monsters: stats at the top, then `[colors]` and the `[hand]`, `[fire]`, `[pickup]` and
`[projectile]` pictures. Copy a file to add a new weapon.

## Designing monsters

![The glitches](docs/screenshot-glitch-monsters.png)

The monsters are **glitches from the drawing itself**: Proxy Object, Unresolved Xref, Fatal Error (floats),
Stray Vertex (a tiny, fast, floating grip), Not Responding (a spinning busy wheel) and Rogue Hatch — plus the
five bosses above. They visibly glitch on screen (rows tear and colours flicker; the `glitch` setting, 0–1).

Every monster is a plain text file in its own folder (`monsters\imp\imp.txt`) next to the plugin DLL (created with the
imp and brute the first time the game runs). Open one in Notepad, change it, save, and press **F5**
in the game to see the result immediately.

```
name   = Imp
health = 50        # stats: health, speed, size, damage, fireball speed, attack delay, spawn weight
[colors]
R = A8322A         # one letter = one colour; "." is see-through
[idle]             # also [walk], [attack], [dead]
...RRRRRRRRRR...
```

* **New monster:** copy `imp.txt` to a new name (e.g. `cacodemon.txt`). It joins the random spawns
  according to its `spawn weight`.
* **Remove a monster:** delete its file. **Start over:** delete the `monsters` folder.
* Mistakes never crash the game: a message in the game window names the file and line to fix, and
  a broken built-in file falls back to the original.

### Using PNG or JPEG pictures

Put `imp.png` (or `.jpg`/`.jpeg`) in the `imp` folder next to `imp.txt` and it replaces the character art. Optional extra
frames are `imp_walk`, `imp_attack` and `imp_dead`; missing ones are generated (mirrored, glowing,
squashed). Transparent PNGs work best; otherwise the background colour touching the edges is removed.
Pictures are trimmed and shrunk to `image size` pixels (default 64). Weapons work the same way
(`shotgun.png`, `shotgun_fire`, `shotgun_pickup`, `shotgun_projectile`).

The shipped designs live in `src/CivDoom.Engine/Monsters/` and are embedded in the engine DLL.

## Building

Requires Windows and the .NET 8 SDK. Targets **Civil 3D 2025 and later** (and any other AutoCAD
2025+ product, since AutoCAD moved to .NET 8 in 2025). No Autodesk install is needed to build: the
AutoCAD reference assemblies come from the `AutoCAD.NET` NuGet package.

```
dotnet build CivDoom.sln -c Release
dotnet test tests/CivDoom.Engine.Tests
```

To try the game without Civil 3D, run `src/CivDoom.Sandbox` (`dotnet run --project src/CivDoom.Sandbox`).

### Autoloader bundle

Building copies the plugin DLLs into `bundle/CivDoom.bundle/Contents/`. Copy the whole
`CivDoom.bundle` folder into `%APPDATA%\Autodesk\ApplicationPlugins\` and restart Civil 3D;
`CIVDOOM` will then be available without `NETLOAD`. CI also publishes the bundle as a build artifact.

## Project layout

| Project | What it is |
| --- | --- |
| `src/CivDoom.Engine` | The game: level building, ray casting, collision, AI, renderer. Pure .NET, no Autodesk or UI dependencies. |
| `src/CivDoom.Windows` | The WinForms game window (input, game loop, HUD). |
| `src/CivDoom.Civil3D` | The plugin: the `CIVDOOM` command and the drawing → level extractor. |
| `src/CivDoom.Sandbox` | Standalone launcher for the built-in level. |
| `tests/CivDoom.Engine.Tests` | xUnit tests for the engine. |

Item and weapon sprites are character art in `Sprites.cs`; monsters are the text files above. There are no binary assets.
