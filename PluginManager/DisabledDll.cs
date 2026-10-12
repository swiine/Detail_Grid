using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PluginManager
{
    /// <summary>
    /// A DLL that AutoCAD loaded the normal way (NETLOAD, APPLOAD Startup Suite, autoload bundle).
    /// .NET can't unload those, so it is disabled instead: its commands are cancelled as they start,
    /// by vetoing the document lock AutoCAD takes before running a command.
    /// </summary>
    internal sealed class DisabledDll
    {
        private readonly List<string> _commands = new();

        public string Name { get; }
        public string Path { get; }
        public IReadOnlyList<string> Commands => _commands;

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

        // Global command name -> name of the disabled DLL that owns it.
        private static readonly Dictionary<string, string> Blocked = new(StringComparer.OrdinalIgnoreCase);
        private static bool _hooked;

        public static DisabledDll Disable(Assembly asm, Editor ed)
        {
            var dll = new DisabledDll(asm);
            var unblockable = new List<string>();

            foreach (var (_, attr) in CommandScanner.Find(asm))
            {
                var name = attr.GlobalName;
                if (dll._commands.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;

                dll._commands.Add(name);
                Blocked[name] = dll.Name;

                // Session commands don't lock the document, so the lock event never sees them.
                if ((attr.Flags & CommandFlags.Session) != 0)
                    unblockable.Add(name);
            }

            if (!_hooked)
            {
                AcApp.DocumentManager.DocumentLockModeChanged += OnDocumentLockModeChanged;
                _hooked = true;
            }

            if (unblockable.Count > 0)
                ed.WriteMessage($"\n  Can't block {string.Join(", ", unblockable)}: they run outside the drawing (Session commands).");

            return dll;
        }

        // Every modal command locks its document before it runs; vetoing the lock cancels the command.
        private static void OnDocumentLockModeChanged(object? sender, DocumentLockModeChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.GlobalCommandName) || !Blocked.TryGetValue(e.GlobalCommandName, out var owner))
                return;

            e.Veto();
            e.Document.Editor.WriteMessage($"\n{e.GlobalCommandName} is disabled ({owner}). Use PMENABLE to turn it back on.\n");
        }

        public void Enable(Editor ed)
        {
            foreach (var name in _commands)
                Blocked.Remove(name);
            _commands.Clear();
        }
    }
}
