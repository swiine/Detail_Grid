using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace TTW.PluginTools
{
    /// <summary>
    /// Finds the tools in a folder: every [CommandMethod] in every .dll under it.
    /// DLLs are read with MetadataLoadContext, so nothing is loaded into Civil 3D and
    /// no file is left locked - the CAD team can replace DLLs while people work.
    /// </summary>
    public static class ToolScanner
    {
        public static readonly string[] VideoExtensions = { ".mp4", ".m4v", ".wmv", ".mov", ".avi" };

        private const string CommandMethodAttr = "Autodesk.AutoCAD.Runtime.CommandMethodAttribute";
        private const string CommandFlagsType = "Autodesk.AutoCAD.Runtime.CommandFlags";
        private const int SessionFlag = (int)Autodesk.AutoCAD.Runtime.CommandFlags.Session;

        // Libraries that are never tools - skipping them keeps the scan quick
        private static readonly string[] SkipPrefixes = { "System.", "Microsoft.", "Newtonsoft.", "Autodesk." };
        private const string LauncherDll = "TTW.PluginTools.dll";

        public sealed class Result
        {
            public List<ToolInfo> Tools { get; } = new List<ToolInfo>();
            /// <summary>DLLs that couldn't be read, with the reason.</summary>
            public List<string> Problems { get; } = new List<string>();
        }

        /// <param name="root">The tools folder.</param>
        /// <param name="archivePrefix">Sub-folders starting with this are marked archived.</param>
        /// <param name="referenceDirs">Folders holding the assemblies plugins are built against
        /// (.NET runtime, WPF, AutoCAD, Civil 3D) so their attributes can be read.</param>
        public static Result Scan(string root, string archivePrefix, IEnumerable<string> referenceDirs)
        {
            var result = new Result();
            var dlls = Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories)
                                .Where(p => !SkipPrefixes.Any(s => Path.GetFileName(p).StartsWith(s, StringComparison.OrdinalIgnoreCase))
                                            && !Path.GetFileName(p).Equals(LauncherDll, StringComparison.OrdinalIgnoreCase))
                                .ToList();
            if (dlls.Count == 0) return result;

            // Plugin DLLs and their neighbours come first so a plugin's own copy of a
            // dependency wins over anything with the same name elsewhere.
            var resolverPaths = Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories)
                .Concat(referenceDirs.Where(Directory.Exists).SelectMany(d => Directory.EnumerateFiles(d, "*.dll")))
                .GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First());

            using var mlc = new MetadataLoadContext(new PathAssemblyResolver(resolverPaths));
            foreach (var dll in dlls)
            {
                try
                {
                    ScanDll(mlc, root, dll, archivePrefix, result.Tools);
                }
                catch (BadImageFormatException) { /* a native DLL - not a plugin */ }
                catch (FileLoadException ex) when (ex.Message.Contains("already been loaded"))
                {
                    result.Problems.Add($"{Rel(root, dll)}: another DLL in the tools folder has the same assembly name.");
                }
                catch (Exception ex)
                {
                    result.Problems.Add($"{Rel(root, dll)}: {ex.Message}");
                }
            }

            // Same command in two DLLs (e.g. a copy left behind): AutoCAD can only have one
            foreach (var dup in result.Tools.Where(t => !t.IsArchived).GroupBy(t => t.CommandName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                foreach (var t in dup)
                    t.Warning = $"The command {t.CommandName} is in more than one DLL ({string.Join(", ", dup.Select(d => d.RelativeDllPath))}). Only one can be loaded - remove the extra copy.";

            return result;
        }

        private static void ScanDll(MetadataLoadContext mlc, string root, string dll, string archivePrefix, List<ToolInfo> tools)
        {
            Assembly asm = mlc.LoadFromAssemblyPath(dll);
            string dir = Path.GetDirectoryName(dll);
            string rel = Rel(root, dll);
            var folders = Path.GetDirectoryName(rel)?.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            bool archived = !string.IsNullOrEmpty(archivePrefix) && folders.Any(f => f.StartsWith(archivePrefix, StringComparison.Ordinal));
            // Default group: the first sub-folder under the tools folder ("" = straight in it)
            string folderGroup = folders.Length > 0 ? folders[0] : "";
            DateTime modified = File.GetLastWriteTime(dll);

            foreach (var type in SafeGetTypes(asm))
            {
                MethodInfo[] methods;
                try { methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly); }
                catch { continue; }

                foreach (var method in methods)
                {
                    IList<CustomAttributeData> attrs;
                    try { attrs = method.GetCustomAttributesData(); }
                    catch { continue; }

                    var cmd = attrs.FirstOrDefault(a => a.AttributeType.FullName == CommandMethodAttr);
                    if (cmd == null || method.GetParameters().Length != 0) continue;
                    if (Arg(attrs, "System.ComponentModel.BrowsableAttribute") is false) continue;

                    var (commandName, flags) = ReadCommandMethod(cmd);
                    if (string.IsNullOrWhiteSpace(commandName)) continue;

                    IList<CustomAttributeData> typeAttrs;
                    try { typeAttrs = type.GetCustomAttributesData(); }
                    catch { typeAttrs = Array.Empty<CustomAttributeData>(); }

                    string name = Arg(attrs, "System.ComponentModel.DisplayNameAttribute") as string;
                    if (string.IsNullOrWhiteSpace(name)) name = commandName;

                    string description = Arg(attrs, "System.ComponentModel.DescriptionAttribute") as string;
                    if (string.IsNullOrWhiteSpace(description)) description = ReadSidecarText(dir, commandName);

                    string group = (Arg(attrs, "System.ComponentModel.CategoryAttribute")
                                    ?? Arg(typeAttrs, "System.ComponentModel.CategoryAttribute")) as string;
                    bool isRead = (Arg(attrs, "System.ComponentModel.ReadOnlyAttribute")
                                   ?? Arg(typeAttrs, "System.ComponentModel.ReadOnlyAttribute")) is true;

                    tools.Add(new ToolInfo
                    {
                        Name = name,
                        Description = description,
                        // Archived copies stay together under their archive folder, away from the live ones
                        Group = archived || string.IsNullOrWhiteSpace(group) ? folderGroup : group,
                        IsRead = isRead,
                        RunAsCommand = (flags & SessionFlag) != 0,
                        IsArchived = archived,
                        CommandName = commandName,
                        DllPath = dll,
                        RelativeDllPath = rel,
                        TypeName = type.FullName,
                        MethodName = method.Name,
                        VideoPath = FindVideo(dir, commandName, name),
                        Modified = modified,
                    });
                }
            }
        }

        /// <summary>
        /// CommandMethod has several constructors: (name), (name, flags), (group, name, flags),
        /// (group, name, localId, flags, ...). When the second argument is a string, it's the name.
        /// </summary>
        private static (string Name, int Flags) ReadCommandMethod(CustomAttributeData cmd)
        {
            var args = cmd.ConstructorArguments;
            if (args.Count == 0) return (null, 0);
            string name = args.Count >= 2 && args[1].ArgumentType.FullName == "System.String"
                ? args[1].Value as string
                : args[0].Value as string;
            int flags = 0;
            foreach (var a in args)
                if (a.ArgumentType.FullName == CommandFlagsType && a.Value != null)
                    flags = Convert.ToInt32(a.Value);
            return (name, flags);
        }

        /// <summary>First constructor argument of the named attribute, or null if it isn't there.</summary>
        private static object Arg(IList<CustomAttributeData> attrs, string fullName)
        {
            var a = attrs.FirstOrDefault(x => x.AttributeType.FullName == fullName);
            return a != null && a.ConstructorArguments.Count > 0 ? a.ConstructorArguments[0].Value : null;
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
        }

        private static string ReadSidecarText(string dir, string commandName)
        {
            try
            {
                string txt = Path.Combine(dir, commandName + ".txt");
                return File.Exists(txt) ? File.ReadAllText(txt).Trim() : null;
            }
            catch { return null; }
        }

        private static string FindVideo(string dir, params string[] names)
        {
            foreach (var n in names.Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (var ext in VideoExtensions)
                {
                    string p = Path.Combine(dir, n + ext);
                    if (File.Exists(p)) return p;
                }
            return null;
        }

        private static string Rel(string root, string path) => Path.GetRelativePath(root, path);
    }
}
