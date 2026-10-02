using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace TTWLinemarking
{
    internal static class Paths
    {
        public const string CompanyLinetypeFolder = @"A:\Civil\AutoCAD\Global\Australia\NSW\_default\Linetypes";
        public const string LogoFileName = "ttw_logo.png";

        public static string PluginFolder => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        // Company drive first. A "Linetypes" folder next to the DLL is the fallback, for machines
        // without the A:\ drive.
        public static IReadOnlyList<string> LinetypeFolders
        {
            get
            {
                var folders = new List<string> { CompanyLinetypeFolder };
                if (PluginFolder != null) folders.Add(Path.Combine(PluginFolder, "Linetypes"));
                return folders;
            }
        }

        public static string FindLinFile(string fileName) =>
            LinetypeFolders.Select(f => Path.Combine(f, fileName)).FirstOrDefault(File.Exists);

        public static string LogoPath => PluginFolder == null ? null : Path.Combine(PluginFolder, LogoFileName);
    }
}
