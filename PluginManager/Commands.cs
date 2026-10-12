using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(PluginManager.Commands))]

namespace PluginManager
{
    public static class Commands
    {
        // Keyed by plugin name (DLL file name without extension).
        private static readonly Dictionary<string, LoadedPlugin> Plugins = new(StringComparer.OrdinalIgnoreCase);

        // Normally loaded DLLs (NETLOAD, Startup Suite, autoload) that PMDISABLE switched off.
        private static readonly Dictionary<string, DisabledDll> Disabled = new(StringComparer.OrdinalIgnoreCase);

        private static Editor Ed => AcApp.DocumentManager.MdiActiveDocument.Editor;

        [CommandMethod("PMLOAD")]
        public static void LoadPlugin()
        {
            var ed = Ed;
            var opts = new PromptOpenFileOptions("\nSelect plugin DLL to load")
            {
                Filter = "Plugin assemblies (*.dll)|*.dll",
            };
            var res = ed.GetFileNameForOpen(opts);
            if (res.Status != PromptStatus.OK)
                return;

            Load(res.StringResult, ed);
        }

        [CommandMethod("PMUNLOAD")]
        public static void UnloadPlugin()
        {
            var ed = Ed;
            foreach (var plugin in Pick(ed, "unload"))
                Unload(plugin, ed);
        }

        [CommandMethod("PMRELOAD")]
        public static void ReloadPlugin()
        {
            var ed = Ed;
            foreach (var plugin in Pick(ed, "reload"))
            {
                Unload(plugin, ed);
                Load(plugin.SourcePath, ed);
            }
        }

        [CommandMethod("PMDISABLE")]
        public static void DisableDll()
        {
            var ed = Ed;

            // Everything that can be switched off: DLLs loaded through PMLOAD (unloaded for real)
            // and DLLs AutoCAD loaded itself (commands blocked, since .NET can't unload those).
            var choices = Plugins.Values.Select(p => (Name: p.Name, Plugin: (LoadedPlugin?)p, Assembly: (Assembly?)null))
                .Concat(DisabledDll.FindNormallyLoaded()
                    .Where(a => !Disabled.ContainsKey(a.GetName().Name ?? ""))
                    .Select(a => (Name: a.GetName().Name ?? a.Location, Plugin: (LoadedPlugin?)null, Assembly: (Assembly?)a)))
                .ToList();

            if (choices.Count == 0)
            {
                ed.WriteMessage("\nNo plugin DLLs with commands are loaded.");
                return;
            }

            foreach (var choice in PickFrom(ed, choices, c => c.Name, "disable"))
            {
                if (choice.Plugin != null)
                {
                    Unload(choice.Plugin, ed);
                    continue;
                }

                var dll = DisabledDll.Disable(choice.Assembly!, ed);
                Disabled[dll.Name] = dll;
                ed.WriteMessage($"\nDisabled {dll.Name} ({dll.Commands.Count} command(s) blocked). " +
                                "It stays in memory until AutoCAD closes; events it hooked keep running.");
            }
        }

        [CommandMethod("PMENABLE")]
        public static void EnableDll()
        {
            var ed = Ed;
            if (Disabled.Count == 0)
            {
                ed.WriteMessage("\nNothing is disabled. (Plugins removed with PMUNLOAD come back with PMLOAD.)");
                return;
            }

            foreach (var dll in PickFrom(ed, Disabled.Values.ToList(), d => d.Name, "enable"))
            {
                dll.Enable(ed);
                Disabled.Remove(dll.Name);
                ed.WriteMessage($"\nEnabled {dll.Name}.");
            }
        }

        [CommandMethod("PMLIST")]
        public static void ListPlugins()
        {
            var ed = Ed;
            foreach (var d in Disabled.Values)
                ed.WriteMessage($"\nDISABLED: {d.Name}\n   {d.Path}\n   Blocked: {string.Join(", ", d.Commands)}");

            if (Plugins.Count == 0)
            {
                ed.WriteMessage("\nNo plugins loaded through PMLOAD.");
                return;
            }

            var i = 1;
            foreach (var p in Plugins.Values)
            {
                ed.WriteMessage($"\n{i++}. {p.Name}  (loaded {p.LoadedAt:HH:mm:ss})\n   {p.SourcePath}");
                ed.WriteMessage($"\n   Commands: {(p.Commands.Count == 0 ? "(none)" : string.Join(", ", p.Commands.Select(c => c.Name)))}");
            }
        }

        private static void Load(string path, Editor ed)
        {
            if (!File.Exists(path))
            {
                ed.WriteMessage($"\nFile not found: {path}");
                return;
            }

            var name = Path.GetFileNameWithoutExtension(path);
            if (Plugins.TryGetValue(name, out var existing))
            {
                ed.WriteMessage($"\n{name} is already loaded, reloading it.");
                Unload(existing, ed);
            }

            try
            {
                var plugin = LoadedPlugin.Load(path, ed);
                Plugins[plugin.Name] = plugin;
                ed.WriteMessage($"\nLoaded {plugin.Name} with {plugin.Commands.Count} command(s): {string.Join(", ", plugin.Commands.Select(c => c.Name))}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nFailed to load {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void Unload(LoadedPlugin plugin, Editor ed)
        {
            Plugins.Remove(plugin.Name);
            var released = plugin.Unload(ed);
            ed.WriteMessage(released
                ? $"\nUnloaded {plugin.Name}."
                : $"\nUnloaded {plugin.Name}'s commands, but .NET could not release the assembly yet. " +
                  "Something still references it (an event handler, palette, ribbon tab, overrule, static field...). " +
                  "Clean those up in Terminate(). Reloading still works.");
        }

        private static List<LoadedPlugin> Pick(Editor ed, string verb)
        {
            if (Plugins.Count == 0)
            {
                ed.WriteMessage("\nNo plugins loaded through PMLOAD.");
                return new List<LoadedPlugin>();
            }
            return PickFrom(ed, Plugins.Values.ToList(), p => p.Name, verb);
        }

        /// <summary>Asks which item(s) to act on, by name or number. Enter selects all.</summary>
        private static List<T> PickFrom<T>(Editor ed, List<T> all, Func<T, string> nameOf, string verb)
        {
            if (all.Count <= 1)
                return all;

            for (var i = 0; i < all.Count; i++)
                ed.WriteMessage($"\n  {i + 1}. {nameOf(all[i])}");

            var opts = new PromptStringOptions($"\nWhich to {verb} (name or number) <all>: ") { AllowSpaces = false };
            var res = ed.GetString(opts);
            if (res.Status != PromptStatus.OK)
                return new List<T>();

            var answer = res.StringResult.Trim();
            if (answer.Length == 0 || answer == "*")
                return all;
            if (int.TryParse(answer, out var n) && n >= 1 && n <= all.Count)
                return new List<T> { all[n - 1] };

            var byName = all.Where(x => string.Equals(nameOf(x), answer, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byName.Count == 0)
                ed.WriteMessage($"\nNothing matches \"{answer}\".");
            return byName;
        }
    }
}
