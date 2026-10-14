namespace PavementBuildup;

internal static class AppFolders
{
    private static string AppDir => AppContext.BaseDirectory;

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

    public static string DayFolder(DateTime day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static void TidyToday() => Tidy(Path.Combine(AppDir, "_Exports", DayFolder(DateTime.Today)));

    public static string ForDesign(string? jobNumber, string? title)
    {
        var dir = Path.Combine(Today, Part(jobNumber, "No job number"), Part(title, "Untitled"));
        try
        {
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Today;
        }
    }

    public static void Tidy(string folder)
    {
        try
        {
            var root = Path.GetFullPath(Path.Combine(AppDir, "_Exports"));
            var dir = new DirectoryInfo(folder);
            while (dir is not null && dir.FullName.Length > root.Length && dir.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                if (!dir.Exists || dir.EnumerateFileSystemInfos().Any()) break;
                dir.Delete();
                dir = dir.Parent;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    public static string Part(string? text, string fallback)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
        var s = new string((text ?? "").Select(c => bad.Contains(c) ? '-' : c).ToArray()).Trim().TrimEnd('.', ' ');
        if (s.Length > 60) s = s[..60].TrimEnd('.', ' ');
        return s.Length == 0 ? fallback : s;
    }

    public static void FileLooseExports()
    {
        try
        {
            var root = Path.Combine(AppDir, "_Exports");
            if (!Directory.Exists(root)) return;
            foreach (var file in Directory.EnumerateFiles(root).ToList())
            {
                var info = new FileInfo(file);
                if (info.Attributes.HasFlag(FileAttributes.Hidden) || info.Name.StartsWith('~')) continue;
                var dir = Path.Combine(root, DayFolder(info.LastWriteTime.Date));
                Directory.CreateDirectory(dir);
                var target = Path.Combine(dir, info.Name);
                for (int i = 2; File.Exists(target); i++)
                    target = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(info.Name)} ({i}){info.Extension}");
                try { File.Move(file, target); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {  }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

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
