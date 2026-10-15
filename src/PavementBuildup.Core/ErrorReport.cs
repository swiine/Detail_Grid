using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace PavementBuildup.Core;

public static class ErrorLog
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PavementBuildup");
    public static string FilePath => Path.Combine(Folder, "errors.log");

    public static void Write(Exception ex, string where)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var info = new FileInfo(FilePath);
            if (info.Exists && info.Length > 2_000_000) File.Move(FilePath, Path.Combine(Folder, "errors.old.log"), overwrite: true);
            File.AppendAllText(FilePath, $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss}  {where}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

public static class ErrorReport
{
    public static string Create(string folder, string app, string version, string description, byte[]? screenshotPng, IEnumerable<string> extraFiles)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"TTW Pavement problem report {DateTime.Now:yyyy-MM-dd HHmm}.zip");
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"TTW Pavement problem report {DateTime.Now:yyyy-MM-dd HHmm} ({i}).zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var about = new StringBuilder()
            .AppendLine($"Program: {app} {version}")
            .AppendLine($"When: {DateTime.Now:yyyy-MM-dd HH:mm}")
            .AppendLine($"User: {Environment.UserName}")
            .AppendLine($"Windows: {RuntimeInformation.OSDescription}")
            .AppendLine($".NET: {RuntimeInformation.FrameworkDescription}")
            .AppendLine()
            .AppendLine("What happened:")
            .AppendLine(string.IsNullOrWhiteSpace(description) ? "(not described)" : description.Trim());
        Add(zip, "about.txt", Encoding.UTF8.GetBytes(about.ToString()));
        if (screenshotPng is { Length: > 0 }) Add(zip, "screenshot.png", screenshotPng);
        foreach (var file in new[] { ErrorLog.FilePath, Path.Combine(ErrorLog.Folder, "errors.old.log") }.Concat(extraFiles).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(file)) continue;
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                var bytes = ms.ToArray();
                if (Path.GetFileName(file).Equals("usage.jsonl", StringComparison.OrdinalIgnoreCase))
                    bytes = Encoding.UTF8.GetBytes(string.Join("\n", Encoding.UTF8.GetString(bytes).Split('\n').TakeLast(300)));
                Add(zip, Path.GetFileName(file), bytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return path;
    }

    private static void Add(ZipArchive zip, string name, byte[] data)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(data);
    }

    public static IEnumerable<string> SettingsFiles() =>
        new[] { "usage.jsonl", "standalone.json", "presets.json", "standard.json", "joints.json" }.Select(f => Path.Combine(ErrorLog.Folder, f));
}
