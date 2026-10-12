using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcUtils = Autodesk.AutoCAD.Internal.Utils;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PluginManager
{
    /// <summary>
    /// A DLL that AutoCAD loaded the normal way (NETLOAD, APPLOAD Startup Suite, autoload bundle).
    /// .NET can't unload those, so it is disabled instead: each of its commands is shadowed by a
    /// stub in a newer command group, which AutoCAD finds first. Enabling removes the stubs.
    /// </summary>
    internal sealed class DisabledDll
    {
        private readonly List<string> _commands = new();

        public string Name { get; }
        public string Path { get; }
        public IReadOnlyList<string> Commands => _commands;
        private string Group => "PMOFF_" + Name.ToUpperInvariant();

        private DisabledDll(Assembly asm)
        {
            Name = asm.GetName().Name ?? System.IO.Path.GetFileNameWithoutExtension(asm.Location);
            Path = asm.Location;
        }

        /// <summary>
        /// Plugin DLLs in the default load context that define commands, leaving out AutoCAD's own,
        /// the .NET runtime, this manager, and anything loaded through PMLOAD.
        /// </summary>
        public static List<Assembly> FindNormallyLoaded()
        {
            var excludedDirs = new[]
            {
                System.IO.Path.GetDirectoryName(typeof(AcApp).Assembly.Location)!,
                System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk"),
            };
            var self = typeof(DisabledDll).Assembly;

            return AssemblyLoadContext.Default.Assemblies
                .Where(a => a != self && !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Where(a => !excludedDirs.Any(d => a.Location.StartsWith(d, StringComparison.OrdinalIgnoreCase)))
                .Where(a => SafeHasCommands(a))
                .OrderBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool SafeHasCommands(Assembly asm)
        {
            try { return CommandScanner.Find(asm).Any(); }
            catch { return false; }
        }

        public static DisabledDll Disable(Assembly asm, Editor ed)
        {
            var dll = new DisabledDll(asm);
            var message = $"\n{dll.Name} is disabled. Use PMENABLE to turn it back on.";

            foreach (var name in CommandScanner.Find(asm).Select(c => c.Attribute.GlobalName).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    AcUtils.AddCommand(dll.Group, name, name, CommandFlags.Modal,
                        () => AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(message));
                    dll._commands.Add(name);
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n  Could not disable {name}: {ex.Message}");
                }
            }

            return dll;
        }

        public void Enable(Editor ed)
        {
            foreach (var name in _commands)
            {
                try { AcUtils.RemoveCommand(Group, name); }
                catch (System.Exception ex) { ed.WriteMessage($"\n  Could not re-enable {name}: {ex.Message}"); }
            }
            _commands.Clear();
        }
    }
}
