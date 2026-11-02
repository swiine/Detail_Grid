using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(CoWork.Loader.Loader))]

namespace CoWork.Loader
{
    /// <summary>
    /// What Civil 3D actually loads from the bundle. It picks the newest signed CoWork (see <see cref="Updater"/>),
    /// then loads it like NETLOAD would. So publishing a new version reaches everyone the next time they start
    /// Civil 3D, without reinstalling. This file rarely changes.
    /// </summary>
    public sealed class Loader : IExtensionApplication
    {
        private static string? _folder;
        private static HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
        private static string _message = "";

        public void Initialize()
        {
            string here = Path.GetDirectoryName(typeof(Loader).Assembly.Location) ?? "";
            string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoWork", "app");
            Updater.Choice? choice = null;
            try { choice = Updater.Choose(ReleaseFolder(Path.Combine(here, "cowork.json")), cache, Path.Combine(here, "app")); }
            catch (System.Exception ex) { _message = "CoWork couldn't check for updates: " + ex.Message; }

            if (choice == null)
            {
                _message = "CoWork: no signed copy of CoWork was found - reinstall it, or ask whoever looks after CoWork.";
                AcApp.Idle += SayOnce;
                return;
            }
            _folder = choice.Folder;
            _files = new HashSet<string>(choice.Manifest.Files.Select(f => Path.GetFileNameWithoutExtension(f.Name)), StringComparer.OrdinalIgnoreCase);
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            try
            {
                ExtensionLoader.Load(Path.Combine(choice.Folder, "CoWork.dll"));
                _message = $"CoWork {choice.Version}" + (choice.Note.Length > 0 ? $" ({choice.Note})" : "");
            }
            catch (System.Exception ex)
            {
                _message = $"CoWork {choice.Version} couldn't load: {ex.Message}";
            }
            AcApp.Idle += SayOnce;
        }

        public void Terminate() { }

        /// <summary>The release folder: "updates" in cowork.json next to the loader (null = no automatic updates).</summary>
        private static string? ReleaseFolder(string configFile)
        {
            try
            {
                if (!File.Exists(configFile)) return null;
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(configFile));
                return doc.RootElement.TryGetProperty("updates", out JsonElement u) && u.ValueKind == JsonValueKind.String && u.GetString() is { Length: > 0 } s ? s : null;
            }
            catch (System.Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
        }

        /// <summary>CoWork's own DLLs come from the checked folder - and only ones listed in its signed manifest.</summary>
        private static Assembly? Resolve(object? sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name ?? "";
            if (_folder == null || !_files.Contains(name)) return null;
            string path = Path.Combine(_folder, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }

        private static void SayOnce(object? sender, EventArgs e)
        {
            AcApp.Idle -= SayOnce;
            try { AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n" + _message); } catch { }
        }
    }
}
