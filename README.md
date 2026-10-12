# Detail_Grid

## PluginManager — load, unload and reload AutoCAD plugins without restarting

AutoCAD can't unload a DLL you `NETLOAD`. Once it's in, it stays in until you close
AutoCAD, and the file is locked so you can't rebuild it either.

PluginManager gets around this. It loads your plugin DLLs into a collectible
.NET 8 `AssemblyLoadContext` (AutoCAD 2025+ runs on .NET 8), registers their
`[CommandMethod]` commands itself, and can remove them and drop the assembly again.
DLLs are read into memory, so the files on disk are never locked: rebuild in
Visual Studio, type `PMRELOAD`, and the new code is live.

### Build

Needs the .NET 8 SDK and AutoCAD 2026 installed.

```
cd PluginManager
dotnet build -c Release
```

If AutoCAD isn't in `C:\Program Files\Autodesk\AutoCAD 2026`, pass
`-p:AcadDir="<your AutoCAD folder>"`.

### Use

1. In AutoCAD, `NETLOAD` `PluginManager\bin\Release\net8.0-windows\PluginManager.dll`
   (this one stays loaded; add it to the Startup Suite in `APPLOAD` if you like).
2. Load your plugins with the commands below. **Don't** also `NETLOAD` them,
   or their commands will clash.

| Command    | What it does |
|------------|--------------|
| `PMLOAD`   | Pick a plugin DLL and load it. |
| `PMUNLOAD` | Remove a plugin's commands and unload it. |
| `PMRELOAD` | Unload and load again from disk (Enter = all plugins). |
| `PMDISABLE`| Switch off **any** plugin DLL, including ones loaded with `NETLOAD`, the `APPLOAD` Startup Suite or an autoload bundle (see below). |
| `PMENABLE` | Turn a disabled DLL back on. |
| `PMLIST`   | Show loaded and disabled plugins and their commands. |

### Disabling DLLs you didn't load through PluginManager

`PMDISABLE` lists every plugin DLL that has commands, both the `PMLOAD` ones and
the ones AutoCAD loaded itself (AutoCAD's own DLLs are left out).

- A `PMLOAD`ed plugin is fully unloaded, same as `PMUNLOAD`.
- A normally loaded DLL can't be unloaded: .NET doesn't allow it. Instead each of
  its commands is covered by a blocker command, so typing it just prints
  "disabled". `PMENABLE` removes the blockers. The DLL stays in memory and its file
  stays locked until AutoCAD closes, and anything it hooked into on startup (events,
  palettes, ribbon tabs) keeps running.
- Disabling lasts until AutoCAD closes.

### What your plugin needs for a clean unload

- Reference the AutoCAD DLLs with **Copy Local = No** (it still works if you don't,
  PluginManager ignores those copies, but it's tidier).
- Undo in `IExtensionApplication.Terminate()` everything `Initialize()` set up:
  unsubscribe events (`Application.Idle`, `DocumentManager.*`, `Database.*`),
  close palettes / remove ribbon tabs, remove overrules, clear static fields.
  PluginManager calls `Terminate()` on unload. Any leftover reference keeps the
  assembly in memory, and `PMUNLOAD` will tell you so. The commands are removed
  regardless, and `PMRELOAD` still loads the new build.
- Command methods must take no parameters (same as AutoCAD's rule).

### Limitations

- Only `[CommandMethod]` commands and `IExtensionApplication` are handled.
  `[LispFunction]` functions are not registered.
- Command registration uses `Autodesk.AutoCAD.Internal.Utils.AddCommand`, an
  unsupported (but long-standing) AutoCAD API.
- Localised command names aren't read; the global name is used for both.
