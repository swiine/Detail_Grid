using System.Runtime.CompilerServices;
using System.Text;

namespace CoWork.Core;

/// <summary>
/// CoWork's log: what went wrong, where and when. Written on the PC (%LOCALAPPDATA%\CoWork\cowork.log) and copied
/// to the shared folder (CoWork-data\logs\&lt;login&gt;.txt) so whoever looks after CoWork can see it. Each file is
/// kept under about 1 MB (the older half moves to .old). Never throws.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _local, _shared;
    private static string _who = "", _version = "";
    public const long MaxBytes = 1_000_000;

    public static string? LocalFile => _local;
    public static string? SharedFile => _shared;

    public static void Init(string localFolder, string? dataFolder, string user, string machine, string version)
    {
        lock (Gate)
        {
            _local = Path.Combine(localFolder, "cowork.log");
            _shared = string.IsNullOrWhiteSpace(dataFolder) ? null : Path.Combine(dataFolder, "logs", Rooms.Safe(user) + ".txt");
            _who = $"{user}@{machine}";
            _version = version;
        }
        Info($"CoWork {version} started on {machine}");
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string where, Exception ex) => Write("ERROR", $"{where}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    /// <summary>For catch blocks that carry on: notes the problem and where it was, without stopping anything.</summary>
    public static void Swallowed(Exception ex, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        string where = $"{Path.GetFileNameWithoutExtension(file)}.{member}:{line}";
        lock (Gate)
        {
            // The same spot failing over and over (e.g. every tick) is written once a minute, not every time.
            if (_lastSeen.TryGetValue(where, out DateTime t) && DateTime.UtcNow - t < TimeSpan.FromMinutes(1)) return;
            _lastSeen[where] = DateTime.UtcNow;
        }
        Write("WARN", $"{where}: {ex.GetType().Name}: {ex.Message}");
    }

    private static readonly Dictionary<string, DateTime> _lastSeen = new();

    private static void Write(string level, string message)
    {
        string text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level,-5} [{_version} {_who}] {message.TrimEnd()}\n";
        lock (Gate)
        {
            foreach (string? f in new[] { _local, _shared })
            {
                if (f == null) continue;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(f)!);
                    if (File.Exists(f) && new FileInfo(f).Length > MaxBytes) File.Move(f, f + ".old", overwrite: true);
                    File.AppendAllText(f, text, Encoding.UTF8);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // logging must never break CoWork
            }
        }
    }

    /// <summary>The last lines of this PC's log (for a problem report).</summary>
    public static string Tail(int lines = 300)
    {
        try
        {
            if (_local == null || !File.Exists(_local)) return "";
            string[] all = File.ReadAllLines(_local);
            return string.Join("\n", all.Skip(Math.Max(0, all.Length - lines)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>A problem report: what the person says happened, plus details and the recent log. Returns its path.</summary>
    public static string Report(string dataFolder, string user, string machine, string what, string details)
    {
        string dir = Path.Combine(dataFolder, "logs", "reports");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, $"{Rooms.Safe(user)}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(file, $"Problem report from {user} on {machine}, {DateTime.Now:d MMM yyyy HH:mm}\n\nWhat happened:\n{what}\n\n{details}\n\n--- recent log ---\n{Tail()}\n");
        Info("Problem report sent: " + Path.GetFileName(file));
        return file;
    }

    /// <summary>Reports in the shared folder, newest first.</summary>
    public static List<string> Reports(string dataFolder)
    {
        string dir = Path.Combine(dataFolder, "logs", "reports");
        return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.txt").OrderByDescending(File.GetLastWriteTimeUtc).ToList() : new();
    }
}
