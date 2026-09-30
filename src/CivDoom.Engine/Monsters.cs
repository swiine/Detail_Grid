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
    public static string DefaultText(string id) => DesignFolder.EmbeddedText("Monsters", id);

    /// <summary>Writes the shipped monster files into <paramref name="folder"/>.</summary>
    public static void WriteDefaults(string folder) => DesignFolder.WriteDefaults(folder, "Monsters", DefaultFiles);

    /// <summary>
    /// Loads every *.txt in <paramref name="folder"/>. Creates the folder with the default monsters if it
    /// doesn't exist. Never throws: broken files are reported in <see cref="Warnings"/> and skipped.
    /// </summary>
    public static MonsterSet Load(string folder)
    {
        var warnings = new List<string>();
        List<MonsterDesign> designs = DesignFolder.Load(
            folder, "monster", () => WriteDefaults(folder), MonsterFile.Parse, BuiltIn.Designs, d => d.Id, warnings);
        return new MonsterSet(designs, warnings);
    }
}

/// <summary>Reads the monster text format (see Monsters/imp.txt for a commented example).</summary>
public static class MonsterFile
{
    public const int MaxPictureSize = DesignText.MaxPictureSize;

    private static readonly string[] Pictures = { "idle", "walk", "attack", "dead" };

    /// <exception cref="FormatException">The file can't be used at all (e.g. no [idle] picture).</exception>
    public static MonsterDesign Parse(string id, string text, List<string> warnings)
    {
        DesignText d = DesignText.Parse(text, Pictures, warnings);
        SpriteImage idle = d.RequiredPicture("idle");
        (int dMin, int dMax) = d.Range("damage", 6, 13);
        return new MonsterDesign
        {
            Id = id,
            Name = d.Text("name") ?? DesignText.Capitalize(id),
            Health = (int)d.Number("health", 50, 1, 100_000),
            Speed = d.Number("speed", 1.6, 0, 20),
            Size = d.Number("size", 0.6, 0.05, 5),
            DamageMin = dMin,
            DamageMax = dMax,
            FireballSpeed = d.Number("fireball speed", 4.5, 0.1, 50),
            AttackDelay = d.Number("attack delay", 1.4, 0.1, 60),
            SpawnWeight = d.Number("spawn weight", 1, 0, 1000),
            Idle = idle,
            Walk = d.PictureOr("walk", idle, "idle"),
            Attack = d.PictureOr("attack", idle, "idle"),
            Dead = d.PictureOr("dead", idle, "idle"),
        };
    }
}
