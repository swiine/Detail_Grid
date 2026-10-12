using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.AutoCAD.EditorInput;

namespace TTW.PluginTools
{
    /// <summary>
    /// Keeps track of the plugins Plugin Tools has loaded, and loads, unloads and reloads them.
    /// Shared by the window, TTWPLUGINRUN and the PM* commands.
    /// </summary>
    public static class PluginHost
    {
        private static readonly Dictionary<string, LoadedPlugin> Plugins = new Dictionary<string, LoadedPlugin>(StringComparer.OrdinalIgnoreCase);

        private static string CacheRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TTW", "PluginTools", "cache");

        public static IReadOnlyList<LoadedPlugin> All => Plugins.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>The loaded plugin for this DLL, or null.</summary>
        public static LoadedPlugin Find(string sourceDll) =>
            Plugins.TryGetValue(Path.GetFullPath(sourceDll), out var p) ? p : null;

        /// <summary>True when anything in the plugin's folder has changed since it was loaded.</summary>
        public static bool IsOutOfDate(LoadedPlugin plugin)
        {
            try { return plugin.Stamp != FolderStamp(Path.GetDirectoryName(plugin.SourceDll)); }
            catch { return false; }   // folder unreachable - keep what's loaded
        }

        /// <summary>
        /// Loads a plugin (unloading the copy already loaded, if any). Must be called in command
        /// context. Throws if it doesn't load.
        /// </summary>
        public static LoadedPlugin Load(string sourceDll, Editor ed)
        {
            sourceDll = Path.GetFullPath(sourceDll);
            var existing = Find(sourceDll);
            if (existing != null) Unload(existing, ed);

            var plugin = LoadedPlugin.Load(sourceDll, ed);
            Plugins[sourceDll] = plugin;
            return plugin;
        }

        /// <summary>Unloads a plugin. Returns false if .NET couldn't fully let go of it (see LoadedPlugin.Unload).</summary>
        public static bool Unload(LoadedPlugin plugin, Editor ed)
        {
            Plugins.Remove(plugin.SourceDll);
            return plugin.Unload(ed);
        }

        public static string UnloadMessage(LoadedPlugin plugin, bool released) => released
            ? $"Unloaded {plugin.Name}."
            : $"Unloaded {plugin.Name}'s commands, but something it set up (an event handler, palette, ribbon tab or static field) " +
              "still holds it in memory. Undo those in its Terminate(). Reloading still works.";

        /// <summary>
        /// Copies the plugin's folder (its DLL and dependencies) to the local cache, one folder per
        /// release so a new release never has to overwrite a DLL that's still loaded.
        /// Returns the local DLL, or null if copying wasn't possible.
        /// </summary>
        internal static string ShadowCopy(string dll, string stamp)
        {
            try
            {
                string srcDir = Path.GetDirectoryName(dll);
                string pluginRoot = Path.Combine(CacheRoot, Hash(srcDir.ToLowerInvariant()));
                string release = Path.Combine(pluginRoot, stamp);
                string localDll = Path.Combine(release, Path.GetFileName(dll));

                if (!File.Exists(localDll))
                {
                    string temp = release + ".partial";
                    if (Directory.Exists(temp)) Directory.Delete(temp, true);
                    Directory.CreateDirectory(temp);
                    foreach (var f in Directory.EnumerateFiles(srcDir))
                        File.Copy(f, Path.Combine(temp, Path.GetFileName(f)), true);
                    if (Directory.Exists(release)) Directory.Delete(release, true);
                    Directory.Move(temp, release);
                }
                Tidy(pluginRoot, keep: release);
                return localDll;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Deletes older local releases. Ones still in use (here or in another Civil 3D) are skipped.</summary>
        private static void Tidy(string pluginRoot, string keep)
        {
            foreach (var d in Directory.EnumerateDirectories(pluginRoot))
            {
                if (string.Equals(d.TrimEnd('\\'), keep.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                try { Directory.Delete(d, true); } catch { /* in use - next time */ }
            }
        }

        /// <summary>Changes whenever any file in the folder changes, is added or removed.</summary>
        internal static string FolderStamp(string dir)
        {
            var sb = new StringBuilder();
            foreach (var f in Directory.EnumerateFiles(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                sb.Append(Path.GetFileName(f)).Append(File.GetLastWriteTimeUtc(f).Ticks).Append(new FileInfo(f).Length).Append(';');
            return Hash(sb.ToString());
        }

        private static string Hash(string s) =>
            Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..12].ToLowerInvariant();
    }
}
