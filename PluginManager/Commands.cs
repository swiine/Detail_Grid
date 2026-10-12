using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        [CommandMethod("PMLIST")]
        public static void ListPlugins()
        {
            var ed = Ed;
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

        /// <summary>Asks which loaded plugin(s) to act on. Enter selects all.</summary>
        private static List<LoadedPlugin> Pick(Editor ed, string verb)
        {
            var all = Plugins.Values.ToList();
            if (all.Count == 0)
            {
                ed.WriteMessage("\nNo plugins loaded through PMLOAD.");
                return new List<LoadedPlugin>();
            }
            if (all.Count == 1)
                return all;

            for (var i = 0; i < all.Count; i++)
                ed.WriteMessage($"\n  {i + 1}. {all[i].Name}");

            var opts = new PromptStringOptions($"\nPlugin to {verb} (name or number) <all>: ") { AllowSpaces = false };
            var res = ed.GetString(opts);
            if (res.Status != PromptStatus.OK)
                return new List<LoadedPlugin>();

            var answer = res.StringResult.Trim();
            if (answer.Length == 0 || answer == "*")
                return all;
            if (int.TryParse(answer, out var n) && n >= 1 && n <= all.Count)
                return new List<LoadedPlugin> { all[n - 1] };
            if (Plugins.TryGetValue(answer, out var byName))
                return new List<LoadedPlugin> { byName };

            ed.WriteMessage($"\nNo loaded plugin matches \"{answer}\".");
            return new List<LoadedPlugin>();
        }
    }
}
