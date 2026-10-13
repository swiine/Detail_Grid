namespace PavementBuildup;

/// <summary>
/// The release folders around the app (TTW_Pave_Tool):
///   Standalone Tool\TTW Pavement.exe, Standalone Tool\_Exports, _support\_Standards.
/// Saves go to _Exports and the standards list reads _support\_Standards when they're there.
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
