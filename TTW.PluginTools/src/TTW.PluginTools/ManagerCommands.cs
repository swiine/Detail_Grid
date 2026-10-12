using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

[assembly: CommandClass(typeof(TTW.PluginTools.ManagerCommands))]

namespace TTW.PluginTools
{
    /// <summary>
    /// Typed commands for loading any plugin DLL (not only ones in the tools folder) and
    /// unloading, reloading or disabling plugins. They share PluginHost with the window, so a
    /// plugin loaded either way shows up in both.
    /// </summary>
    public static class ManagerCommands
    {
        // Plugins loaded some other way (NETLOAD, startup suite) that PMDISABLE switched off
        private static readonly Dictionary<string, DisabledDll> Disabled = new Dictionary<string, DisabledDll>(StringComparer.OrdinalIgnoreCase);

        private static Editor Ed => AcApp.DocumentManager.MdiActiveDocument.Editor;

        [CommandMethod("PMLOAD")]
        public static void LoadPlugin()
        {
            var ed = Ed;
            var res = ed.GetFileNameForOpen(new PromptOpenFileOptions("\nPlugin DLL to load") { Filter = "Plugin (*.dll)|*.dll" });
            if (res.Status == PromptStatus.OK)
                Load(res.StringResult, ed);
        }

        [CommandMethod("PMUNLOAD")]
        public static void UnloadPlugin()
        {
            var ed = Ed;
            foreach (var plugin in PickLoaded(ed, "unload"))
                ed.WriteMessage("\n" + PluginHost.UnloadMessage(plugin, PluginHost.Unload(plugin, ed)));
        }

        [CommandMethod("PMRELOAD")]
        public static void ReloadPlugin()
        {
            var ed = Ed;
            foreach (var plugin in PickLoaded(ed, "reload"))
                Load(plugin.SourceDll, ed);
        }

        [CommandMethod("PMDISABLE")]
        public static void DisablePlugin()
        {
            var ed = Ed;
            // Plugins loaded here are unloaded for real; ones loaded some other way can't be,
            // so their commands are blocked instead
            var choices = PluginHost.All.Select(p => (Name: p.Name, Plugin: p, Assembly: (Assembly)null))
                .Concat(DisabledDll.FindNormallyLoaded()
                    .Where(a => !Disabled.ContainsKey(a.GetName().Name ?? ""))
                    .Select(a => (Name: a.GetName().Name ?? a.Location, Plugin: (LoadedPlugin)null, Assembly: a)))
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
                    ed.WriteMessage("\n" + PluginHost.UnloadMessage(choice.Plugin, PluginHost.Unload(choice.Plugin, ed)));
                    continue;
                }
                var dll = DisabledDll.Disable(choice.Assembly, ed);
                Disabled[dll.Name] = dll;
                ed.WriteMessage($"\nDisabled {dll.Name} ({dll.Commands.Count} command(s) blocked). " +
                                "It stays in memory until Civil 3D closes; anything it hooked into keeps running.");
            }
        }

        [CommandMethod("PMENABLE")]
        public static void EnablePlugin()
        {
            var ed = Ed;
            if (Disabled.Count == 0)
            {
                ed.WriteMessage("\nNothing is disabled. (Unloaded plugins come back with PMLOAD, or by running one of their tools.)");
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

            var all = PluginHost.All;
            if (all.Count == 0)
            {
                ed.WriteMessage("\nNo plugins loaded through Plugin Tools.");
                return;
            }
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                string updated = PluginHost.IsOutOfDate(p) ? "  ** updated on disk - PMRELOAD to load it **" : "";
                ed.WriteMessage($"\n{i + 1}. {p.Name}  (loaded {p.LoadedAt:HH:mm:ss}){updated}\n   {p.SourceDll}");
                ed.WriteMessage($"\n   Commands: {(p.Commands.Count == 0 ? "(none)" : string.Join(", ", p.Commands))}");
            }
        }

        private static void Load(string path, Editor ed)
        {
            if (!File.Exists(path))
            {
                ed.WriteMessage($"\nFile not found: {path}");
                return;
            }
            try
            {
                var plugin = PluginHost.Load(path, ed);
                ed.WriteMessage($"\nLoaded {plugin.Name} with {plugin.Commands.Count} command(s): {string.Join(", ", plugin.Commands)}");
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\nFailed to load {Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static List<LoadedPlugin> PickLoaded(Editor ed, string verb)
        {
            var all = PluginHost.All.ToList();
            if (all.Count == 0)
            {
                ed.WriteMessage("\nNo plugins loaded through Plugin Tools.");
                return all;
            }
            return PickFrom(ed, all, p => p.Name, verb);
        }

        /// <summary>Asks which item(s) to act on, by name or number. Enter selects all.</summary>
        private static List<T> PickFrom<T>(Editor ed, List<T> all, Func<T, string> nameOf, string verb)
        {
            if (all.Count <= 1) return all;

            for (int i = 0; i < all.Count; i++)
                ed.WriteMessage($"\n  {i + 1}. {nameOf(all[i])}");

            var res = ed.GetString(new PromptStringOptions($"\nWhich to {verb} (name or number) <all>: ") { AllowSpaces = false });
            if (res.Status != PromptStatus.OK) return new List<T>();

            string answer = res.StringResult.Trim();
            if (answer.Length == 0 || answer == "*") return all;
            if (int.TryParse(answer, out int n) && n >= 1 && n <= all.Count) return new List<T> { all[n - 1] };

            var byName = all.Where(x => string.Equals(nameOf(x), answer, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byName.Count == 0) ed.WriteMessage($"\nNothing matches \"{answer}\".");
            return byName;
        }
    }
}
