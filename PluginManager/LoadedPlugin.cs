using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcUtils = Autodesk.AutoCAD.Internal.Utils;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PluginManager
{
    /// <summary>
    /// One plugin DLL loaded into its own collectible context, with its commands
    /// registered by hand (AutoCAD only auto-registers commands for NETLOADed assemblies).
    /// </summary>
    internal sealed class LoadedPlugin
    {
        private readonly List<(string Group, string Name)> _commands = new();
        private readonly List<IExtensionApplication> _apps = new();
        private readonly Dictionary<(Type, Document?), object> _instances = new();

        private PluginLoadContext? _context;
        private Assembly? _assembly;

        public string Name { get; }
        public string SourcePath { get; }
        public DateTime LoadedAt { get; } = DateTime.Now;
        public IReadOnlyList<(string Group, string Name)> Commands => _commands;

        private LoadedPlugin(string sourcePath)
        {
            SourcePath = sourcePath;
            Name = Path.GetFileNameWithoutExtension(sourcePath);
        }

        public static LoadedPlugin Load(string path, Editor ed)
        {
            var plugin = new LoadedPlugin(Path.GetFullPath(path));
            var context = new PluginLoadContext(plugin.Name, Path.GetDirectoryName(plugin.SourcePath)!);
            plugin._context = context;

            try
            {
                plugin._assembly = context.LoadWithoutLocking(plugin.SourcePath);
                plugin.RegisterCommands(ed);
                plugin.InitializeExtensionApps(ed);
            }
            catch
            {
                plugin.Unload(ed);
                throw;
            }

            return plugin;
        }

        private void RegisterCommands(Editor ed)
        {
            var defaultGroup = "PM_" + Name.ToUpperInvariant();

            foreach (var (method, attr) in CommandScanner.Find(_assembly!))
            {
                if (method.GetParameters().Length != 0)
                {
                    ed.WriteMessage($"\n  Skipped {method.DeclaringType?.Name}.{method.Name}: command methods must take no parameters.");
                    continue;
                }

                var group = string.IsNullOrEmpty(attr.GroupName) ? defaultGroup : attr.GroupName;
                var name = attr.GlobalName;

                try
                {
                    AcUtils.AddCommand(group, name, name, attr.Flags, () => Invoke(method));
                    _commands.Add((group, name));
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n  Could not register {name}: {ex.Message} (is the plugin also NETLOADed?)");
                }
            }
        }

        private void Invoke(MethodInfo method)
        {
            object? instance = null;
            if (!method.IsStatic)
            {
                // AutoCAD creates one instance of a command class per document; mirror that.
                var key = (method.DeclaringType!, AcApp.DocumentManager.MdiActiveDocument);
                if (!_instances.TryGetValue(key, out instance))
                {
                    instance = Activator.CreateInstance(method.DeclaringType!, nonPublic: true)!;
                    _instances[key] = instance;
                }
            }

            try
            {
                method.Invoke(instance, null);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage($"\n[{Name}] {ex.InnerException.GetType().Name}: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}\n");
            }
        }

        private void InitializeExtensionApps(Editor ed)
        {
            var asm = _assembly!;
            var marker = asm.GetCustomAttribute<ExtensionApplicationAttribute>();

            IEnumerable<Type> appTypes = marker != null
                ? (marker.Type != null ? new[] { marker.Type } : Array.Empty<Type>())
                : CommandScanner.GetLoadableTypes(asm).Where(t => typeof(IExtensionApplication).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

            foreach (var type in appTypes)
            {
                var app = (IExtensionApplication)Activator.CreateInstance(type, nonPublic: true)!;
                app.Initialize();
                _apps.Add(app);
            }
        }

        /// <summary>
        /// Removes the plugin's commands, calls Terminate() and unloads its context.
        /// Returns true if the runtime actually released the assembly.
        /// </summary>
        public bool Unload(Editor ed)
        {
            foreach (var (group, name) in _commands)
            {
                try { AcUtils.RemoveCommand(group, name); }
                catch (System.Exception ex) { ed.WriteMessage($"\n  Could not remove {name}: {ex.Message}"); }
            }
            _commands.Clear();

            foreach (var app in _apps)
            {
                try { app.Terminate(); }
                catch (System.Exception ex) { ed.WriteMessage($"\n  [{Name}] Terminate() threw: {ex.Message}"); }
            }
            _apps.Clear();

            foreach (var disposable in _instances.Values.OfType<IDisposable>())
            {
                try { disposable.Dispose(); } catch { /* best effort */ }
            }
            _instances.Clear();
            _assembly = null;

            var weak = StartUnload();
            for (var i = 0; i < 10 && weak.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            return !weak.IsAlive;
        }

        // Kept out of line so no local in Unload() keeps the context alive.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private WeakReference StartUnload()
        {
            var weak = new WeakReference(_context);
            _context?.Unload();
            _context = null;
            return weak;
        }
    }
}
