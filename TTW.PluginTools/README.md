# TTW Plugin Tools

The TTW Advanced Tools window, but for **compiled `.dll` plugins** instead of `.csx` scripts.
Same look (Water Blue header, logo, list on the left, description and Run on the right,
status line), same behaviour:

| Advanced Tools (.csx) | Plugin Tools (.dll) |
|---|---|
| `TTWADVTOOLS` opens the window | `TTWPLUGINS` |
| `TTWADVRUN` for ribbon buttons | `TTWPLUGINRUN` - macro `^C^C_TTWPLUGINRUN;COMMANDNAME;` |
| One tool per `.csx` file | One tool per `[CommandMethod]` in a DLL |
| `// @name:` `// @description:` `// @mode: read` `// @tags:` | `[DisplayName]` `[Description]` `[ReadOnly(true)]` `[Category]` |
| Sub-folders = groups | Sub-folders = groups (or `[Category]`) |
| `_archive` folders hidden | same (`ArchivePrefix` in the config) |
| `Name.mp4` = tutorial video | `COMMANDNAME.mp4` or `Display Name.mp4` next to the DLL |
| Failed script -> drawing rolled back | Failed tool -> drawing rolled back |
| Read-only -> nothing kept | Read-only -> nothing kept |
| "Check it compiles" | "Check it loads" |
| Copy on A: stays unlocked | Each plugin is copied locally before loading, so A: stays unlocked |
| Edit a script, run it again | Update or rebuild a plugin, run it again: the new version loads, no restart |

## What's here

| Folder | |
|---|---|
| `ready-to-copy/Plugins/` | Built files, laid out exactly as they go on A: (see Install). |
| `src/TTW.PluginTools/` | The launcher's source. |
| `example/TTW.PluginTools.Example/` | An example plugin with 4 tools showing every option. Copy it to start a new plugin. |
| `deploy/` | The loader `.lsp` and the site config. |

## Install

Everything lives in one folder on A:, each plugin in its own sub-folder:

```
A:\Civil\AutoCAD\Global\Australia\NSW\_default\Scripts\_under_development\Plugins\
  TTW.PluginTools\            <- the launcher itself (never shows in the list)
    TTW.PluginTools.dll
    System.Reflection.MetadataLoadContext.dll
    TTW.PluginTools.config.json
    TTW_Plugin_Tools_Loader.lsp
  TTW.PluginTools.Example\    <- example plugin (4 tools)
    TTW.PluginTools.Example.dll
    TTWEXAMPLEREPORT.txt
  Your Plugin\                <- one folder per plugin: its DLL and anything it needs
    YourPlugin.dll
    YOURCOMMAND.mp4            <- optional tutorial video
  _archive\                   <- hidden unless "Show archived tools" is ticked
```

1. Copy `ready-to-copy\Plugins\*` into that `Plugins` folder.
2. Get the loader to run at startup - either:
   - copy `TTW_Plugin_Tools_Loader.lsp` into `Lisp_Routines` so StandardisedFileOpening picks it up
     (it loads the launcher from the Plugins folder, wherever the .lsp itself is), or
   - `APPLOAD` > Startup Suite > add `Plugins\TTW.PluginTools\TTW_Plugin_Tools_Loader.lsp`.
   To try it once without either: `APPLOAD` the .lsp, or `NETLOAD` `TTW.PluginTools.dll`.
3. Restart Civil 3D and type `TTWPLUGINS`.

The paths are already set: `DefaultToolsFolder` in `TTW.PluginTools.config.json`, and
`*path_TTWPluginToolsSource*` at the top of the loader. Change both if the folder moves.

## Adding a plugin

1. Build the plugin (any normal AutoCAD/Civil 3D 2026 .NET plugin - it doesn't reference the launcher).
2. Copy its output (the DLL and anything it needs, not `.pdb`) into a new folder in `Plugins`.
3. It shows up in the window straight away (the folder is watched), or right-click > Reload list.

The folder name is the group heading in the list, unless the command has a `[Category]`.

## Describing a tool

All standard .NET attributes from `System.ComponentModel`, so nothing extra to reference:

```csharp
[CommandMethod("TTWCOUNTBLOCKS")]                 // makes it a tool (and a normal typed command)
[DisplayName("Count blocks")]                     // name in the list (else the command name)
[Description("Lists every block and how many.")] // shown when picked (or COMMAND.txt next to the DLL)
[Category("Audit")]                               // group heading (else the first sub-folder)
[ReadOnly(true)]                                  // read-only: changes are thrown away
public void CountBlocks() { ... }

[CommandMethod("HELPER")]
[Browsable(false)]                                // keeps a command out of the list
public void Helper() { ... }
```

## How running works

- The plugin's folder is copied to `%LOCALAPPDATA%\TTW\PluginTools\cache` and loaded from
  there the first time one of its tools runs: its commands get registered (so they can be typed
  too) and `IExtensionApplication.Initialize` runs. The copy on A: is never locked.
- Each plugin is loaded into its own unloadable .NET context. When anything in its folder
  changes, the next run unloads the old version and loads the new one - no restart.
- The command method runs inside one outer transaction. If it throws (or the tool is
  read-only) that transaction is aborted, which rolls back everything the tool did.
  For this to work, plugins must use `TransactionManager.StartTransaction()` -
  changes made in `StartOpenCloseTransaction()` or by sending commands aren't covered.
- Tools can prompt for points, selections etc. as normal - they run in command context.
- Commands with `CommandFlags.Session` can't be wrapped, so they're sent to the command line instead.
- A plugin loaded some other way first (`NETLOAD`, startup suite) can't be unloaded - .NET
  doesn't allow it. Plugin Tools uses that copy and says so; restart Civil 3D and load it only
  through Plugin Tools to get live reloading.

## Working on a plugin without restarting Civil 3D

Right-click a tool in the window:

| | |
|---|---|
| **Build from source...** / **Build and reload** | Compiles the plugin's `.csproj` straight into its folder in the list, then swaps the running copy for the new build. The first time, it asks which `.csproj` builds that DLL and remembers it (per person). Compiler errors go to the command line. Needs the .NET 8 SDK (Visual Studio installs it). Building into the shared tools folder asks first, because everyone gets the new build. |
| **Reload plugin** | Loads the current version from disk again. |
| **Unload plugin** | Removes it and its commands. It loads again the next time one of its tools runs. |
| **Change source project...** | Points Build at a different `.csproj`. |

So the loop is: edit the code, **Build and reload**, run the tool. Or build in Visual Studio with
its output going to the plugin's folder, and just run the tool again.

For a clean unload, undo in `IExtensionApplication.Terminate()` everything `Initialize()` set up:
unsubscribe events, close palettes, remove ribbon tabs and overrules, clear static fields. Anything
left keeps the old copy in memory (Plugin Tools tells you); the commands are swapped either way.

## Typed commands

For plugins anywhere, not just in the tools folder. They share the same list of loaded plugins
as the window.

| Command | |
|---|---|
| `PMLOAD` | Pick any plugin `.dll` and load it (unloadable, file not locked). |
| `PMRELOAD` | Load the current version of a loaded plugin from disk again (Enter = all). |
| `PMUNLOAD` | Unload a plugin. |
| `PMLIST` | What's loaded, its commands, and whether it's been updated on disk. |
| `PMDISABLE` | Switch off any plugin, including ones loaded with `NETLOAD` or at startup. Those can't be unloaded, so their commands are blocked instead (they print "disabled"). Commands flagged `CommandFlags.Session` can't be blocked; it lists any it finds. |
| `PMENABLE` | Turn a disabled plugin back on. |

## Building

Visual Studio 2022 (".NET desktop development") or the .NET 8 SDK:
```
dotnet build src\TTW.PluginTools -c Release
dotnet build example\TTW.PluginTools.Example -c Release
```
The AutoCAD API comes from the `AutoCAD.NET` 25.1 NuGet package (for 2026); no Civil 3D install is
needed to build. Output: `bin\Release\net8.0-windows\` - copy everything except `.pdb`/`.deps.json`.
`./package.sh` does both builds, fills `ready-to-copy/` and zips it all up.
