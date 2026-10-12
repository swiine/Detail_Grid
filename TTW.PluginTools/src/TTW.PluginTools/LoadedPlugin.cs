using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcUtils = Autodesk.AutoCAD.Internal.Utils;
using Exception = System.Exception;

namespace TTW.PluginTools
{
    /// <summary>
    /// A plugin DLL loaded into its own collectible context. Its commands are registered here
    /// (AutoCAD only registers them itself for NETLOADed DLLs), so they can be removed again.
    /// </summary>
    public sealed class LoadedPlugin
    {
        private readonly List<(string Group, string Name)> _commands = new List<(string, string)>();
        private readonly List<IExtensionApplication> _apps = new List<IExtensionApplication>();
        private readonly Dictionary<(Type, Document), object> _instances = new Dictionary<(Type, Document), object>();
        private PluginLoadContext _context;

        /// <summary>The DLL in the tools folder (or wherever it was picked from).</summary>
        public string SourceDll { get; }
        public string Name { get; }
        public Assembly Assembly { get; private set; }
        /// <summary>The source folder's stamp when this was loaded; it changes when anything in the folder does.</summary>
        public string Stamp { get; private set; }
        public DateTime LoadedAt { get; } = DateTime.Now;
        public IReadOnlyList<string> Commands => _commands.Select(c => c.Name).ToList();

        private LoadedPlugin(string sourceDll)
        {
            SourceDll = sourceDll;
            Name = Path.GetFileNameWithoutExtension(sourceDll);
        }

        internal static LoadedPlugin Load(string sourceDll, Editor ed)
        {
            var plugin = new LoadedPlugin(sourceDll);
            string srcDir = Path.GetDirectoryName(sourceDll);
            plugin.Stamp = PluginHost.FolderStamp(srcDir);

            // Load a local copy so the DLL in the tools folder stays unlocked; failing that, load
            // it into memory (still unlocked, but native dependencies load from the tools folder).
            string local = PluginHost.ShadowCopy(sourceDll, plugin.Stamp);
            plugin._context = local != null
                ? new PluginLoadContext(plugin.Name, Path.GetDirectoryName(local), inMemory: false)
                : new PluginLoadContext(plugin.Name, srcDir, inMemory: true);

            try
            {
                plugin.Assembly = plugin._context.LoadFile(local ?? sourceDll);
                plugin.RegisterCommands(ed);
                plugin.InitializeExtensionApps();
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
            string defaultGroup = "TTWPT_" + Name.ToUpperInvariant();

            foreach (var (method, attr) in CommandScanner.Find(Assembly))
            {
                if (method.GetParameters().Length != 0) continue;
                string group = string.IsNullOrEmpty(attr.GroupName) ? defaultGroup : attr.GroupName;
                string name = attr.GlobalName;
                try
                {
                    AcUtils.AddCommand(group, name, name, attr.Flags, () => Invoke(method));
                    _commands.Add((group, name));
                }
                catch (Exception ex)
                {
                    ed?.WriteMessage($"\nTTW Plugin Tools: couldn't register {name} ({ex.Message}). Is {Name} also loaded with NETLOAD?");
                }
            }
        }

        /// <summary>Runs a command typed at the command line.</summary>
        private void Invoke(MethodInfo method)
        {
            object instance = null;
            if (!method.IsStatic)
            {
                // AutoCAD makes one instance of a command class per drawing; do the same
                var key = (method.DeclaringType, AcApp.DocumentManager.MdiActiveDocument);
                if (!_instances.TryGetValue(key, out instance))
                    _instances[key] = instance = Activator.CreateInstance(method.DeclaringType, nonPublic: true);
            }

            try
            {
                method.Invoke(instance, null);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                var inner = ex.InnerException;
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[{Name}] {inner.GetType().Name}: {inner.Message}\n{inner.StackTrace}\n");
            }
        }

        private void InitializeExtensionApps()
        {
            var marker = Assembly.GetCustomAttribute<ExtensionApplicationAttribute>();
            IEnumerable<Type> types = marker != null
                ? (marker.Type != null ? new[] { marker.Type } : Array.Empty<Type>())
                : CommandScanner.GetLoadableTypes(Assembly).Where(t => typeof(IExtensionApplication).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

            foreach (var type in types)
            {
                var app = (IExtensionApplication)Activator.CreateInstance(type, nonPublic: true);
                app.Initialize();
                _apps.Add(app);
            }
        }

        /// <summary>
        /// Removes the commands, calls Terminate() and unloads. Returns true if .NET actually let
        /// go of the DLL; false means something the plugin set up (an event handler, palette,
        /// static field...) still points at it. The commands are gone either way.
        /// </summary>
        internal bool Unload(Editor ed)
        {
            foreach (var (group, name) in _commands)
            {
                try { AcUtils.RemoveCommand(group, name); }
                catch (Exception ex) { ed?.WriteMessage($"\nTTW Plugin Tools: couldn't remove {name} ({ex.Message})."); }
            }
            _commands.Clear();

            foreach (var app in _apps)
            {
                try { app.Terminate(); }
                catch (Exception ex) { ed?.WriteMessage($"\n[{Name}] Terminate() failed: {ex.Message}"); }
            }
            _apps.Clear();

            foreach (var disposable in _instances.Values.OfType<IDisposable>())
            {
                try { disposable.Dispose(); } catch { /* best effort */ }
            }
            _instances.Clear();
            Assembly = null;

            var weak = StartUnload();
            for (int i = 0; i < 10 && weak.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            return !weak.IsAlive;
        }

        // Kept out of line so nothing in Unload() holds on to the context
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
