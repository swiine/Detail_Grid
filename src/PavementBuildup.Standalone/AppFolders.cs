namespace PavementBuildup;

/// <summary>
/// The release folders around the app (TTW_Pave_Tool):
///   Standalone Tool\TTW Pavement.exe, Standalone Tool\_Exports, _support\_Standards.
/// Saves go to a folder for today in _Exports (_Exports\2026-10-13) and the standards list reads
/// _support\_Standards when they're there.
/// </summary>
internal static class AppFolders
{
    private static string AppDir => AppContext.BaseDirectory;

    /// <summary>Standalone Tool\_Exports (created when first needed), or Documents if it can't be.</summary>
    public static string Exports
    {
        get
        {
            var dir = Path.Combine(AppDir, "_Exports");
            try
            {
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
        }
    }

    /// <summary>Today's export folder, _Exports\yyyy-MM-dd (created now, so a save dialog can open in it).</summary>
    public static string Today
    {
        get
        {
            var root = Exports;
            var dir = Path.Combine(root, DayFolder(DateTime.Today));
            try
            {
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return root;
            }
        }
    }

    /// <summary>"2026-10-13": year, month, day (sorts in date order).</summary>
    public static string DayFolder(DateTime day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Removes today's folder again if nothing was saved into it (e.g. the save was cancelled).</summary>
    public static void TidyToday()
    {
        try
        {
            var dir = Path.Combine(AppDir, "_Exports", DayFolder(DateTime.Today));
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Files put straight into _Exports are moved into a folder for the day they were last changed
    /// (_Exports\yyyy-MM-dd). A file with the same name already there keeps both ("name (2).dxf").
    /// </summary>
    public static void FileLooseExports()
    {
        try
        {
            var root = Path.Combine(AppDir, "_Exports");
            if (!Directory.Exists(root)) return;
            foreach (var file in Directory.EnumerateFiles(root).ToList())
            {
                var info = new FileInfo(file);
                if (info.Attributes.HasFlag(FileAttributes.Hidden) || info.Name.StartsWith('~')) continue; // temp / lock files
                var dir = Path.Combine(root, DayFolder(info.LastWriteTime.Date));
                Directory.CreateDirectory(dir);
                var target = Path.Combine(dir, info.Name);
                for (int i = 2; File.Exists(target); i++)
                    target = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(info.Name)} ({i}){info.Extension}");
                try { File.Move(file, target); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* open in another program: leave it */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>_support\_Standards next to the Standalone Tool folder, or null if it isn't there.</summary>
    public static string? Standards
    {
        get
        {
            try
            {
                var dir = Path.GetFullPath(Path.Combine(AppDir, "..", "_support", "_Standards"));
                return Directory.Exists(dir) ? dir : null;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                return null;
            }
        }
    }
}
