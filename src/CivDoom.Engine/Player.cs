namespace CivDoom.Engine;

/// <summary>Your character, as seen from behind in the finish-line cutscene.</summary>
public sealed class PlayerDesign
{
    public required string Name { get; init; }

    /// <summary>World height of the picture (1.0 = wall height).</summary>
    public double Size { get; init; } = 0.62;

    public required SpriteImage Idle { get; init; }
    public required SpriteImage Run { get; init; }
    public required SpriteImage Run2 { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    private static PlayerDesign? _builtIn;

    public static PlayerDesign BuiltIn => _builtIn ??= Parse(DefaultText(), new List<string>(), null);

    public static string DefaultText() => DesignFolder.EmbeddedText("Player", "player");

    private static readonly string[] Pictures = { "idle", "run", "run2" };

    public static PlayerDesign Parse(string text, List<string> warnings, string? folder)
    {
        DesignText d = DesignText.Parse(text, Pictures, warnings);
        SpriteImage idle = d.Image(folder, "player", "") ?? d.RequiredPicture("idle");
        bool fromImage = folder != null && ImageSprites.Find(folder, "player") != null;
        SpriteImage run = d.Image(folder, "player", "_run") ?? (fromImage ? idle : d.PictureOr("run", idle, "idle"));
        SpriteImage run2 = d.Image(folder, "player", "_run2") ?? (fromImage ? ImageSprites.Mirror(run) : d.PictureOr("run2", ImageSprites.Mirror(run), "run (mirrored)"));
        return new PlayerDesign
        {
            Name = d.Text("name") ?? "Marine",
            Size = d.Number("size", 0.62, 0.1, 3),
            Idle = idle,
            Run = run,
            Run2 = run2,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Loads <paramref name="folder"/>\player.txt (creating it if missing). Never throws: problems fall back
    /// to the built-in marine with a warning.
    /// </summary>
    public static PlayerDesign Load(string folder)
    {
        var warnings = new List<string>();
        string path = Path.Combine(folder, "player.txt");
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(path, DefaultText());
            }
            var fileWarnings = new List<string>();
            PlayerDesign p = Parse(File.ReadAllText(path), fileWarnings, folder);
            warnings.AddRange(fileWarnings.Select(w => $"player.txt: {w}"));
            return new PlayerDesign { Name = p.Name, Size = p.Size, Idle = p.Idle, Run = p.Run, Run2 = p.Run2, Warnings = warnings };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            warnings.Add($"player.txt: {ex.Message} (using the original marine)");
            PlayerDesign b = BuiltIn;
            return new PlayerDesign { Name = b.Name, Size = b.Size, Idle = b.Idle, Run = b.Run, Run2 = b.Run2, Warnings = warnings };
        }
    }
}

/// <summary>Everything editable that a game uses, loaded from the folders next to the plugin.</summary>
public sealed record GameContent(MonsterSet Monsters, WeaponSet Weapons, ThemeSet Themes, PlayerDesign Player)
{
    public static GameContent BuiltIn => new(MonsterSet.BuiltIn, WeaponSet.BuiltIn, ThemeSet.BuiltIn, PlayerDesign.BuiltIn);

    /// <summary>Loads monsters\, weapons\, themes\ and player\ from <paramref name="root"/> (each created if missing).</summary>
    public static GameContent Load(string root) => new(
        MonsterSet.Load(Path.Combine(root, "monsters")),
        WeaponSet.Load(Path.Combine(root, "weapons")),
        ThemeSet.Load(Path.Combine(root, "themes")),
        PlayerDesign.Load(Path.Combine(root, "player")));
}
