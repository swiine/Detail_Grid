using System.Globalization;
using System.Text.RegularExpressions;

namespace CoWork.Core;

/// <summary>
/// Safety copies of drawings used in CoWork sessions. They go in the job's civil folder, found from the drawing's path:
/// <code>
///   \\ttw\dfs\ttwcad\jobs\24\241796\civil\sheets\0\0\C-ROAD.dwg
///   -> job 241796, backups in \\ttw\dfs\ttwcad\jobs\24\241796\civil\_backup\C-ROAD\
/// </code>
/// The job number is the 6-digit folder in the path. If there's no job number or no civil folder after it, the
/// backups go in a _backup folder next to the drawing instead. Each copy is named after when, why and who:
/// "2026-11-03 0915 - session start - Corey.dwg".
/// </summary>
public static class Backups
{
    public const string FolderName = "_backup";

    public sealed record Where(string? Job, string Root, bool InCivil);

    public sealed record Copy(string File, DateTime Time, string Reason, string Who);

    private static readonly Regex JobNumber = new(@"^\d{6}$", RegexOptions.Compiled);

    /// <summary>The job number (6 digits) and the _backup folder for a drawing.</summary>
    public static Where Locate(string drawing)
    {
        char sep = drawing.Contains('\\') ? '\\' : '/';
        string[] parts = drawing.Split('\\', '/');
        string[] dirs = parts.Take(Math.Max(0, parts.Length - 1)).ToArray(); // the folders the drawing is in
        string Join(int count) => string.Join(sep, dirs.Take(count)) + sep + FolderName;
        for (int i = 0; i < dirs.Length; i++)
        {
            if (!JobNumber.IsMatch(dirs[i])) continue;
            // The civil folder: the first "civil" folder inside the job.
            for (int j = i + 1; j < dirs.Length; j++)
                if (string.Equals(dirs[j], "civil", StringComparison.OrdinalIgnoreCase))
                    return new Where(dirs[i], Join(j + 1), true);
            return new Where(dirs[i], Join(dirs.Length), false);
        }
        return new Where(null, Join(dirs.Length), false);
    }

    /// <summary>The folder for one drawing's copies.</summary>
    public static string FolderFor(string drawing)
    {
        string root = Locate(drawing).Root;
        char sep = drawing.Contains('\\') ? '\\' : '/';
        string name = drawing.Split('\\', '/')[^1];
        int dot = name.LastIndexOf('.');
        string folder = new string((dot > 0 ? name[..dot] : name).Select(c => "<>:\"/\\|?*".Contains(c) || c < ' ' ? '_' : c).ToArray()).Trim();
        return root + sep + (folder.Length > 0 ? folder : "drawing");
    }

    private static string Clean(string s) =>
        new string(s.Replace(" - ", " ").Select(c => "<>:\"/\\|?*".Contains(c) || c < ' ' ? ' ' : c).ToArray()).Trim();

    /// <summary>Copies the drawing as it is on disk now. Returns the copy's path.</summary>
    public static string Make(string drawing, string reason, string who, DateTime now)
    {
        string dir = FolderFor(drawing);
        Directory.CreateDirectory(dir);
        string stamp = now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture);
        string name = $"{stamp} - {Clean(reason)} - {Clean(who)}";
        string file = Path.Combine(dir, name + ".dwg");
        for (int n = 2; File.Exists(file); n++) file = Path.Combine(dir, $"{name} ({n}).dwg");
        string tmp = file + ".tmp";
        File.Copy(drawing, tmp, overwrite: true);
        File.Move(tmp, file);
        return file;
    }

    /// <summary>A drawing's copies, newest first.</summary>
    public static List<Copy> List(string drawing)
    {
        string dir = FolderFor(drawing);
        var result = new List<Copy>();
        if (!Directory.Exists(dir)) return result;
        foreach (string f in Directory.GetFiles(dir, "*.dwg"))
        {
            string[] bits = Path.GetFileNameWithoutExtension(f).Split(" - ");
            DateTime time = bits.Length > 0 && DateTime.TryParseExact(bits[0], "yyyy-MM-dd HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime t)
                ? t : File.GetLastWriteTime(f);
            string who = bits.Length > 2 ? Regex.Replace(bits[2], @" \(\d+\)$", "") : "";
            result.Add(new Copy(f, time, bits.Length > 1 ? bits[1] : "", who));
        }
        return result.OrderByDescending(c => c.Time).ThenByDescending(c => c.File, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Keeps the newest <paramref name="keep"/> copies of a drawing and deletes the rest.</summary>
    public static void Prune(string drawing, int keep)
    {
        foreach (Copy c in List(drawing).Skip(Math.Max(1, keep)))
            try { File.Delete(c.File); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Whether someone has the drawing open for editing (AutoCAD's lock file is there).</summary>
    public static bool InUse(string drawing) => File.Exists(Path.ChangeExtension(drawing, ".dwl"));

    /// <summary>
    /// Puts a copy back as the drawing. The drawing as it was is kept first ("before restore"), so a restore can itself be
    /// undone. Refuses while the drawing is open for editing anywhere (close it first).
    /// </summary>
    public static string Restore(string drawing, string copy, string who, DateTime now)
    {
        if (InUse(drawing)) throw new IOException($"{Path.GetFileName(drawing)} is open for editing - close it everywhere first.");
        if (!File.Exists(copy)) throw new FileNotFoundException("That backup isn't there any more.", copy);
        string kept = File.Exists(drawing) ? Make(drawing, "before restore", who, now) : "";
        File.Copy(copy, drawing, overwrite: true);
        return kept;
    }
}
