using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CivDoom.Engine;

/// <summary>
/// The shared text format behind monster and weapon files:
/// <c>setting = value</c> lines, a <c>[colors]</c> section, and named picture sections.
/// Parsing is forgiving: problems become warnings; only unusable files throw <see cref="FormatException"/>.
/// </summary>
public sealed class DesignText
{
    public const int MaxPictureSize = 128;

    private static readonly Regex SectionLine = new(@"^\[\s*([A-Za-z]+)\s*\]$");
    private static readonly Regex SettingLine = new(@"^([A-Za-z][A-Za-z _]*?)\s*=\s*([^#]*)");
    private static readonly Regex ColorLine = new(@"^(\S)\s*=\s*#?([0-9A-Fa-f]{6})\b");

    private readonly Dictionary<string, (string Value, int Line)> _settings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<char, int> _palette = new();
    private readonly Dictionary<string, (List<string> Rows, int Line)> _pictures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpriteImage> _built = new(StringComparer.OrdinalIgnoreCase);

    private DesignText(List<string> warnings) => Warnings = warnings;

    public List<string> Warnings { get; }

    public static DesignText Parse(string text, IReadOnlyCollection<string> pictureNames, List<string> warnings)
    {
        var d = new DesignText(warnings);
        string section = "";
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int lineNo = i + 1;
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            Match m = SectionLine.Match(line);
            if (m.Success)
            {
                section = m.Groups[1].Value.ToLowerInvariant();
                if (section is "colors" or "colours") section = "colors";
                else if (pictureNames.Contains(section)) d._pictures[section] = (new List<string>(), lineNo);
                else warnings.Add($"line {lineNo}: unknown section [{m.Groups[1].Value}]");
                continue;
            }

            if (section == "")
            {
                m = SettingLine.Match(line);
                if (m.Success) d._settings[Normalize(m.Groups[1].Value)] = (m.Groups[2].Value.Trim(), lineNo);
                else warnings.Add($"line {lineNo}: expected 'setting = value'");
            }
            else if (section == "colors")
            {
                m = ColorLine.Match(line);
                if (m.Success) d._palette[m.Groups[1].Value[0]] = int.Parse(m.Groups[2].Value, NumberStyles.HexNumber);
                else warnings.Add($"line {lineNo}: expected a colour like 'R = A8322A'");
            }
            else if (d._pictures.TryGetValue(section, out var pic))
            {
                pic.Rows.Add(line);
            }
        }
        return d;
    }

    /// <summary>The named picture, or null if the file doesn't have it.</summary>
    public SpriteImage? Picture(string name)
    {
        if (_built.TryGetValue(name, out SpriteImage? done)) return done;
        if (!_pictures.TryGetValue(name, out var p) || p.Rows.Count == 0) return null;
        return _built[name] = BuildPicture(name, p.Rows, p.Line);
    }

    public SpriteImage RequiredPicture(string name) =>
        Picture(name) ?? throw new FormatException($"needs a [{name}] picture");

    /// <summary>The named picture, or <paramref name="fallback"/> with a warning.</summary>
    public SpriteImage PictureOr(string name, SpriteImage fallback, string fallbackName)
    {
        SpriteImage? p = Picture(name);
        if (p != null) return p;
        Warnings.Add($"no [{name}] picture, using [{fallbackName}]");
        return fallback;
    }

    private SpriteImage BuildPicture(string name, List<string> rows, int line)
    {
        int width = rows.Max(r => r.Length);
        if (width > MaxPictureSize || rows.Count > MaxPictureSize)
            throw new FormatException($"[{name}] is {width} x {rows.Count}; the limit is {MaxPictureSize} x {MaxPictureSize}");
        if (rows.Any(r => r.Length != width))
            Warnings.Add($"[{name}] (line {line}) has rows of different widths; short rows were padded with '.'");

        var px = new int[width * rows.Count];
        var unknown = new SortedSet<char>();
        for (int y = 0; y < rows.Count; y++)
        {
            for (int x = 0; x < rows[y].Length; x++)
            {
                char ch = rows[y][x];
                if (ch == '.' || ch == ' ') continue;
                if (_palette.TryGetValue(ch, out int rgb)) px[y * width + x] = unchecked((int)0xFF000000) | rgb;
                else unknown.Add(ch);
            }
        }
        if (unknown.Count > 0)
            Warnings.Add($"[{name}] uses letters with no colour: {string.Join(" ", unknown)} (drawn see-through)");
        return new SpriteImage(width, rows.Count, px);
    }

    public string? Text(string key) =>
        _settings.TryGetValue(Normalize(key), out var v) && v.Value.Length > 0 ? v.Value : null;

    public double Number(string key, double fallback, double min, double max)
    {
        if (!_settings.TryGetValue(Normalize(key), out var v)) return fallback;
        if (double.TryParse(v.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d))
        {
            if (d >= min && d <= max) return d;
            Warnings.Add($"line {v.Line}: {key} must be between {min} and {max}");
            return Math.Clamp(d, min, max);
        }
        Warnings.Add($"line {v.Line}: '{v.Value}' is not a number");
        return fallback;
    }

    public (int Min, int Max) Range(string key, int min, int max)
    {
        if (!_settings.TryGetValue(Normalize(key), out var v)) return (min, max);
        string[] parts = v.Value.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 1 or 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int a)
            && int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int b)
            && a >= 0 && b >= 0)
        {
            return (Math.Min(a, b), Math.Max(a, b));
        }
        Warnings.Add($"line {v.Line}: {key} should look like '6-13' or '10'");
        return (min, max);
    }

    public bool Flag(string key, bool fallback)
    {
        if (!_settings.TryGetValue(Normalize(key), out var v)) return fallback;
        switch (v.Value.ToLowerInvariant())
        {
            case "yes" or "true" or "1" or "on": return true;
            case "no" or "false" or "0" or "off": return false;
            default:
                Warnings.Add($"line {v.Line}: {key} should be yes or no");
                return fallback;
        }
    }

    private static string Normalize(string key) => key.Replace(" ", "").Replace("_", "").ToLowerInvariant();

    internal static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

/// <summary>Loads a folder of design .txt files, creating it from the embedded defaults when missing.</summary>
internal static class DesignFolder
{
    public static string EmbeddedText(string folder, string id)
    {
        using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"CivDoom.Engine.{folder}.{id}.txt");
        if (s == null) throw new InvalidOperationException($"Missing embedded file {folder}/{id}.txt.");
        using var reader = new StreamReader(s);
        return reader.ReadToEnd();
    }

    public static void WriteDefaults(string folder, string resourceFolder, IEnumerable<string> ids)
    {
        Directory.CreateDirectory(folder);
        foreach (string id in ids)
        {
            string path = Path.Combine(folder, id + ".txt");
            if (!File.Exists(path)) File.WriteAllText(path, EmbeddedText(resourceFolder, id));
        }
    }

    /// <summary>Never throws: broken files are reported in <paramref name="warnings"/> and replaced by the built-in version if there is one.</summary>
    public static List<T> Load<T>(
        string folder, string what, Action writeDefaults, Func<string, string, List<string>, T> parse,
        IReadOnlyList<T> builtIns, Func<T, string> idOf, List<string> warnings) where T : class
    {
        try
        {
            if (!Directory.Exists(folder)) writeDefaults();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Couldn't create {folder}: {ex.Message}");
            return builtIns.ToList();
        }

        var result = new List<T>();
        foreach (string path in Directory.GetFiles(folder, "*.txt").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string file = Path.GetFileName(path);
            string id = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            try
            {
                var fileWarnings = new List<string>();
                result.Add(parse(id, File.ReadAllText(path), fileWarnings));
                warnings.AddRange(fileWarnings.Select(w => $"{file}: {w}"));
            }
            catch (FormatException ex)
            {
                T? fallback = builtIns.FirstOrDefault(b => idOf(b) == id);
                warnings.Add($"{file}: {ex.Message}" + (fallback != null ? " (using the original)" : " (skipped)"));
                if (fallback != null) result.Add(fallback);
            }
            catch (IOException ex)
            {
                warnings.Add($"{file}: {ex.Message}");
            }
        }

        if (result.Count == 0)
        {
            warnings.Add($"No {what} files found, using the originals.");
            result.AddRange(builtIns);
        }
        return result;
    }
}
