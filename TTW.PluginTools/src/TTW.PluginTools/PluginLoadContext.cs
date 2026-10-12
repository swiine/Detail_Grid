using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace TTW.PluginTools
{
    /// <summary>
    /// One plugin's own load context. It's collectible, so the plugin can be unloaded and a
    /// new build loaded in its place without restarting Civil 3D (NETLOAD can't do that).
    /// </summary>
    internal sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly string _dir;
        private readonly bool _inMemory;
        private static string[] _hostDirs;

        /// <param name="dir">Folder holding the plugin and its dependencies.</param>
        /// <param name="inMemory">Read DLLs into memory instead of loading the files, so they aren't
        /// locked (used when the local copy couldn't be made and we're loading straight from A:).</param>
        public PluginLoadContext(string name, string dir, bool inMemory)
            : base(name, isCollectible: true)
        {
            _dir = dir;
            _inMemory = inMemory;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            string name = assemblyName.Name;
            if (string.IsNullOrEmpty(name)) return null;

            // Anything Civil 3D already has (AutoCAD and Civil 3D APIs, .NET, WPF, this launcher) is
            // shared, otherwise there'd be two Document types and CommandMethod wouldn't match.
            if (Default.Assemblies.Any(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase)))
                return null;
            // Same for API assemblies that aren't loaded yet, even if a Copy Local duplicate sits next to the plugin
            _hostDirs ??= ToolRunner.ReferenceDirs().Where(Directory.Exists).ToArray();
            if (_hostDirs.Any(d => File.Exists(Path.Combine(d, name + ".dll"))))
                return null;

            string path = Path.Combine(_dir, name + ".dll");
            return File.Exists(path) ? LoadFile(path) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string file = unmanagedDllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? unmanagedDllName : unmanagedDllName + ".dll";
            string path = Path.Combine(_dir, file);
            return File.Exists(path) ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
        }

        public Assembly LoadFile(string path)
        {
            if (!_inMemory) return LoadFromAssemblyPath(path);

            using var asm = new MemoryStream(File.ReadAllBytes(path));
            string pdbPath = Path.ChangeExtension(path, ".pdb");
            if (!File.Exists(pdbPath)) return LoadFromStream(asm);
            using var pdb = new MemoryStream(File.ReadAllBytes(pdbPath));
            return LoadFromStream(asm, pdb);
        }
    }
}
