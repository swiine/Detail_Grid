# Detail_Grid

## TTW Plugin Tools

`TTW.PluginTools/` is a Civil 3D / AutoCAD 2026 window for running compiled `.dll` plugins,
with live loading: plugins can be built, loaded, unloaded and reloaded without closing CAD.

- `TTWPLUGINS` opens the window. Right-click a tool for **Build and reload**, **Reload plugin**
  and **Unload plugin**.
- `PMLOAD`, `PMRELOAD`, `PMUNLOAD`, `PMLIST`, `PMDISABLE` and `PMENABLE` do the same from the
  command line, for any plugin DLL.

See [TTW.PluginTools/README.md](TTW.PluginTools/README.md) for install and use.
`TTW.PluginTools/package.sh` builds everything into `TTW.PluginTools.zip`.
