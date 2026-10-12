using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace PluginManager
{
    /// <summary>
    /// A collectible load context for one plugin. Assemblies are read into memory
    /// before loading, so the DLLs on disk are never locked and can be rebuilt
    /// while AutoCAD is running.
    /// </summary>
    internal sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly string _pluginDir;
        private readonly string _hostDir;

        public PluginLoadContext(string name, string pluginDir)
            : base(name, isCollectible: true)
        {
            _pluginDir = pluginDir;
            _hostDir = Path.GetDirectoryName(typeof(Autodesk.AutoCAD.ApplicationServices.Application).Assembly.Location)!;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name;
            if (string.IsNullOrEmpty(name))
                return null;

            // Anything the host already has (AutoCAD API, this manager, the framework)
            // must be shared, otherwise types like CommandMethodAttribute or Document
            // would exist twice and nothing would match.
            if (Default.Assemblies.Any(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase)))
                return null;

            // AutoCAD assemblies that aren't loaded yet (AdWindows, AcCui, ...) are also
            // left to the default context, even if a Copy Local duplicate sits next to the plugin.
            if (File.Exists(Path.Combine(_hostDir, name + ".dll")))
                return null;

            var candidate = Path.Combine(_pluginDir, name + ".dll");
            return File.Exists(candidate) ? LoadWithoutLocking(candidate) : null;
        }

        public Assembly LoadWithoutLocking(string path)
        {
            using var asm = new MemoryStream(File.ReadAllBytes(path));
            var pdbPath = Path.ChangeExtension(path, ".pdb");
            if (!File.Exists(pdbPath))
                return LoadFromStream(asm);

            using var pdb = new MemoryStream(File.ReadAllBytes(pdbPath));
            return LoadFromStream(asm, pdb);
        }
    }
}
