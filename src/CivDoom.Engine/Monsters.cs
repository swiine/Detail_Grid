using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CivDoom.Engine;

/// <summary>Everything that defines one kind of monster: stats plus its pictures.</summary>
public sealed class MonsterDesign
{
    /// <summary>File name without extension, lower case (e.g. "imp"). Used by level markers.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }
    public int Health { get; init; } = 50;
    public double Speed { get; init; } = 1.6;

    /// <summary>World height of the idle picture (1.0 = wall height).</summary>
    public double Size { get; init; } = 0.6;

    public int DamageMin { get; init; } = 6;
    public int DamageMax { get; init; } = 13;
    public double FireballSpeed { get; init; } = 4.5;
    public double AttackDelay { get; init; } = 1.4;
    public double SpawnWeight { get; init; } = 1;

    public required SpriteImage Idle { get; init; }
    public required SpriteImage Walk { get; init; }
    public required SpriteImage Attack { get; init; }
    public required SpriteImage Dead { get; init; }

    /// <summary>Collision radius, derived from the size.</summary>
    public double Radius => Math.Clamp(Size * 0.24, 0.08, 0.45);

    /// <summary>World units per sprite pixel, so every frame is drawn at the same scale as [idle].</summary>
    public double PixelSize => Size / Idle.Height;
}

/// <summary>The monsters available to a game, normally loaded from the "monsters" folder of text files.</summary>
public sealed class MonsterSet
{
    private static readonly string[] DefaultFiles = { "imp", "brute" };
    private static MonsterSet? _builtIn;

    public MonsterSet(IReadOnlyList<MonsterDesign> designs, IReadOnlyList<string>? warnings = null)
    {
        if (designs.Count == 0) throw new ArgumentException("At least one monster is required.", nameof(designs));
        Designs = designs;
        Warnings = warnings ?? Array.Empty<string>();
    }

    public IReadOnlyList<MonsterDesign> Designs { get; }

    /// <summary>Problems found while loading (shown to the player in game).</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>The monsters that ship with the game.</summary>
    public static MonsterSet BuiltIn => _builtIn ??= new MonsterSet(
        DefaultFiles.Select(id => MonsterFile.Parse(id, DefaultText(id), new List<string>())).ToList());

    public MonsterDesign? Find(string? id) =>
        id == null ? null : Designs.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Picks a monster by id, or a weighted-random one when the id is empty or unknown.</summary>
    public MonsterDesign Resolve(string? id, Random rng)
    {
        if (Find(id) is { } found) return found;
        double total = Designs.Sum(d => Math.Max(0, d.SpawnWeight));
        if (total <= 0) return Designs[rng.Next(Designs.Count)];
        double roll = rng.NextDouble() * total;
        foreach (MonsterDesign d in Designs)
        {
            roll -= Math.Max(0, d.SpawnWeight);
            if (roll < 0) return d;
        }
        return Designs[^1];
    }

    /// <summary>The text of a shipped monster file (embedded in the engine DLL).</summary>
    public static string DefaultText(string id)
    {
        using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"CivDoom.Engine.Monsters.{id}.txt");
        if (s == null) throw new InvalidOperationException($"Missing embedded monster file '{id}'.");
        using var reader = new StreamReader(s);
        return reader.ReadToEnd();
    }

    /// <summary>Writes the shipped monster files into <paramref name="folder"/>.</summary>
    public static void WriteDefaults(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (string id in DefaultFiles)
        {
            string path = Path.Combine(folder, id + ".txt");
            if (!File.Exists(path)) File.WriteAllText(path, DefaultText(id));
        }
    }

    /// <summary>
    /// Loads every *.txt in <paramref name="folder"/>. Creates the folder with the default monsters if it
    /// doesn't exist. Never throws: broken files are reported in <see cref="Warnings"/> and skipped.
    /// </summary>
    public static MonsterSet Load(string folder)
    {
        var warnings = new List<string>();
        try
        {
            if (!Directory.Exists(folder)) WriteDefaults(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Couldn't create {folder}: {ex.Message}");
            return new MonsterSet(BuiltIn.Designs, warnings);
        }

        var designs = new List<MonsterDesign>();
        foreach (string path in Directory.GetFiles(folder, "*.txt").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string file = Path.GetFileName(path);
            string id = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            try
            {
                var fileWarnings = new List<string>();
                designs.Add(MonsterFile.Parse(id, File.ReadAllText(path), fileWarnings));
                warnings.AddRange(fileWarnings.Select(w => $"{file}: {w}"));
            }
            catch (FormatException ex)
            {
                MonsterDesign? fallback = BuiltIn.Find(id);
                warnings.Add($"{file}: {ex.Message}" + (fallback != null ? " (using the original)" : " (skipped)"));
                if (fallback != null) designs.Add(fallback);
            }
            catch (IOException ex)
            {
                warnings.Add($"{file}: {ex.Message}");
            }
        }

        if (designs.Count == 0)
        {
            warnings.Add("No monster files found, using the originals.");
            designs.AddRange(BuiltIn.Designs);
        }
        return new MonsterSet(designs, warnings);
    }
}

/// <summary>Reads the monster text format (see Monsters/imp.txt for a commented example).</summary>
public static class MonsterFile
{
    public const int MaxPictureSize = 128;

    private static readonly Regex SectionLine = new(@"^\[\s*([A-Za-z]+)\s*\]$");
    private static readonly Regex SettingLine = new(@"^([A-Za-z][A-Za-z _]*?)\s*=\s*([^#]*)");
    private static readonly Regex ColorLine = new(@"^(\S)\s*=\s*#?([0-9A-Fa-f]{6})\b");
    private static readonly string[] Pictures = { "idle", "walk", "attack", "dead" };

    /// <exception cref="FormatException">The file can't be used at all (e.g. no [idle] picture).</exception>
    public static MonsterDesign Parse(string id, string text, List<string> warnings)
    {
        var settings = new Dictionary<string, (string Value, int Line)>(StringComparer.OrdinalIgnoreCase);
        var palette = new Dictionary<char, int>();
        var pictures = new Dictionary<string, (List<string> Rows, int Line)>(StringComparer.OrdinalIgnoreCase);
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
                if (section == "colors" || section == "colours") section = "colors";
                else if (Pictures.Contains(section)) pictures[section] = (new List<string>(), lineNo);
                else warnings.Add($"line {lineNo}: unknown section [{m.Groups[1].Value}]");
                continue;
            }

            if (section == "")
            {
                m = SettingLine.Match(line);
                if (m.Success) settings[Normalize(m.Groups[1].Value)] = (m.Groups[2].Value.Trim(), lineNo);
                else warnings.Add($"line {lineNo}: expected 'setting = value'");
            }
            else if (section == "colors")
            {
                m = ColorLine.Match(line);
                if (m.Success) palette[m.Groups[1].Value[0]] = int.Parse(m.Groups[2].Value, NumberStyles.HexNumber);
                else warnings.Add($"line {lineNo}: expected a colour like 'R = A8322A'");
            }
            else if (pictures.TryGetValue(section, out var pic))
            {
                pic.Rows.Add(line);
            }
        }

        if (!pictures.TryGetValue("idle", out var idleRows) || idleRows.Rows.Count == 0)
            throw new FormatException("needs an [idle] picture");

        SpriteImage idle = BuildPicture("idle", idleRows.Rows, idleRows.Line, palette, warnings);
        SpriteImage Optional(string name)
        {
            if (pictures.TryGetValue(name, out var p) && p.Rows.Count > 0) return BuildPicture(name, p.Rows, p.Line, palette, warnings);
            warnings.Add($"no [{name}] picture, using [idle]");
            return idle;
        }

        (int dMin, int dMax) = ReadRange(settings, "damage", 6, 13, warnings);
        return new MonsterDesign
        {
            Id = id,
            Name = settings.TryGetValue("name", out var n) && n.Value.Length > 0 ? n.Value : Capitalize(id),
            Health = (int)ReadNumber(settings, "health", 50, 1, 100_000, warnings),
            Speed = ReadNumber(settings, "speed", 1.6, 0, 20, warnings),
            Size = ReadNumber(settings, "size", 0.6, 0.05, 5, warnings),
            DamageMin = dMin,
            DamageMax = dMax,
            FireballSpeed = ReadNumber(settings, "fireballspeed", 4.5, 0.1, 50, warnings),
            AttackDelay = ReadNumber(settings, "attackdelay", 1.4, 0.1, 60, warnings),
            SpawnWeight = ReadNumber(settings, "spawnweight", 1, 0, 1000, warnings),
            Idle = idle,
            Walk = Optional("walk"),
            Attack = Optional("attack"),
            Dead = Optional("dead"),
        };
    }

    private static SpriteImage BuildPicture(string name, List<string> rows, int line, Dictionary<char, int> palette, List<string> warnings)
    {
        int width = rows.Max(r => r.Length);
        if (width > MaxPictureSize || rows.Count > MaxPictureSize)
            throw new FormatException($"[{name}] is {width} x {rows.Count}; the limit is {MaxPictureSize} x {MaxPictureSize}");
        if (rows.Any(r => r.Length != width))
            warnings.Add($"[{name}] (line {line}) has rows of different widths; short rows were padded with '.'");

        var px = new int[width * rows.Count];
        var unknown = new SortedSet<char>();
        for (int y = 0; y < rows.Count; y++)
        {
            for (int x = 0; x < rows[y].Length; x++)
            {
                char ch = rows[y][x];
                if (ch == '.' || ch == ' ') continue;
                if (palette.TryGetValue(ch, out int rgb)) px[y * width + x] = unchecked((int)0xFF000000) | rgb;
                else unknown.Add(ch);
            }
        }
        if (unknown.Count > 0)
            warnings.Add($"[{name}] uses letters with no colour: {string.Join(" ", unknown)} (drawn see-through)");
        return new SpriteImage(width, rows.Count, px);
    }

    private static double ReadNumber(Dictionary<string, (string Value, int Line)> s, string key, double fallback, double min, double max, List<string> warnings)
    {
        if (!s.TryGetValue(key, out var v)) return fallback;
        if (double.TryParse(v.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d))
        {
            if (d >= min && d <= max) return d;
            warnings.Add($"line {v.Line}: {key} must be between {min} and {max}");
            return Math.Clamp(d, min, max);
        }
        warnings.Add($"line {v.Line}: '{v.Value}' is not a number");
        return fallback;
    }

    private static (int, int) ReadRange(Dictionary<string, (string Value, int Line)> s, string key, int min, int max, List<string> warnings)
    {
        if (!s.TryGetValue(key, out var v)) return (min, max);
        string[] parts = v.Value.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 1 or 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int a)
            && int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int b)
            && a >= 0 && b >= 0)
        {
            return (Math.Min(a, b), Math.Max(a, b));
        }
        warnings.Add($"line {v.Line}: {key} should look like '6-13' or '10'");
        return (min, max);
    }

    private static string Normalize(string key) => key.Replace(" ", "").Replace("_", "").ToLowerInvariant();

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
