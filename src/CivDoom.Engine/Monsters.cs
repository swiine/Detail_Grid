namespace CivDoom.Engine;

/// <summary>How a monster fights.</summary>
public enum Behavior
{
    /// <summary>Keeps a medium distance and circle-strafes while shooting.</summary>
    Skirmisher,

    /// <summary>Zig-zags straight at you and attacks up close.</summary>
    Rusher,

    /// <summary>Hangs back, retreats if you push in, and leads its shots.</summary>
    Sniper,

    /// <summary>Advances relentlessly and never backs off.</summary>
    Tank,
}

/// <summary>Everything that defines one kind of monster: stats plus its pictures.</summary>
public sealed class MonsterDesign
{
    /// <summary>File name without extension, lower case (e.g. "proxy"). Used by level markers.</summary>
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

    /// <summary>How far above the floor it hovers (0 = walks on the floor).</summary>
    public double FloatHeight { get; init; }

    public Behavior Behavior { get; init; } = Behavior.Skirmisher;

    /// <summary>Distance it tries to fight from (wall heights).</summary>
    public double PreferredRange { get; init; } = 4;

    /// <summary>0-1: how much it moves sideways while fighting.</summary>
    public double Strafe { get; init; } = 0.4;

    /// <summary>0-1: how far ahead of a moving player it aims.</summary>
    public double AimLead { get; init; } = 0.3;

    /// <summary>0-1: chance to side-step when you aim at it and fire.</summary>
    public double Dodge { get; init; } = 0.3;

    /// <summary>Damage of its close-range hit (0 = it only shoots).</summary>
    public int MeleeMin { get; init; }
    public int MeleeMax { get; init; }

    /// <summary>How much it visually glitches: 0 = solid, 1 = falling apart (rows tear, colours flicker).</summary>
    public double Glitch { get; init; }

    /// <summary>Bosses guard the finish line and never turn up as random monsters.</summary>
    public bool Boss { get; init; }

    /// <summary>Shots fired per attack, fanned out across <see cref="ShotSpread"/> degrees.</summary>
    public int Shots { get; init; } = 1;

    public double ShotSpread { get; init; } = 15;

    /// <summary>Colour of its shots (0xRRGGBB), or null for the standard orange fireball.</summary>
    public int? ProjectileColor { get; init; }

    private SpriteImage? _projectile;

    /// <summary>The picture for its shots.</summary>
    public SpriteImage ProjectileSprite => _projectile ??= ProjectileColor is { } c
        ? ImageSprites.Tint(Art.FireballSprite, c, 0.55)
        : Art.FireballSprite;

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
    private static readonly string[] DefaultFiles =
    {
        "proxy", "xref", "fatalerror", "strayvertex", "notresponding", "roguehatch",
        "bluescreen", "infiniteregen", "corrupteddwg", "exception", "licence",
    };
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
        DefaultFiles.Select(id => MonsterFile.Parse(id, DefaultText(id), new List<string>(), null)).ToList());

    /// <summary>Spawn id meaning "one random boss".</summary>
    public const string RandomBoss = "@boss";

    public IEnumerable<MonsterDesign> Bosses => Designs.Where(d => d.Boss);

    public MonsterDesign? Find(string? id) =>
        id == null ? null : Designs.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Picks a monster by id; <see cref="RandomBoss"/> picks one of the bosses; an empty or unknown id picks
    /// a weighted-random ordinary (non-boss) monster.
    /// </summary>
    public MonsterDesign Resolve(string? id, Random rng)
    {
        if (id == RandomBoss)
        {
            List<MonsterDesign> bosses = Bosses.ToList();
            if (bosses.Count > 0) return Weighted(bosses, rng);
            return Designs.OrderByDescending(d => d.Health).First(); // no boss files: the toughest monster
        }
        if (Find(id) is { } found) return found;
        List<MonsterDesign> pool = Designs.Where(d => !d.Boss).ToList();
        return Weighted(pool.Count > 0 ? pool : Designs.ToList(), rng);
    }

    private static MonsterDesign Weighted(List<MonsterDesign> pool, Random rng)
    {
        double total = pool.Sum(d => Math.Max(0, d.SpawnWeight));
        if (total <= 0) return pool[rng.Next(pool.Count)];
        double roll = rng.NextDouble() * total;
        foreach (MonsterDesign d in pool)
        {
            roll -= Math.Max(0, d.SpawnWeight);
            if (roll < 0) return d;
        }
        return pool[^1];
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

/// <summary>Reads the monster text format (see Monsters/proxy/proxy.txt for a commented example).</summary>
public static class MonsterFile
{
    public const int MaxPictureSize = DesignText.MaxPictureSize;

    private static readonly string[] Pictures = { "idle", "walk", "attack", "dead" };

    /// <summary>
    /// Parses a monster file. When <paramref name="folder"/> is given, pictures named after the file
    /// (imp.png / imp.jpg, imp_walk.png, imp_attack.png, imp_dead.png) replace the character art.
    /// </summary>
    /// <exception cref="FormatException">The file can't be used at all (e.g. no [idle] picture and no image).</exception>
    public static MonsterDesign Parse(string id, string text, List<string> warnings, string? folder = null)
    {
        DesignText d = DesignText.Parse(text, Pictures, warnings);
        SpriteImage? image = d.Image(folder, id, "");
        if (image != null) return Build(d, id, image,
            d.Image(folder, id, "_walk") ?? ImageSprites.Mirror(image),
            d.Image(folder, id, "_attack") ?? ImageSprites.Tint(image, 0xFF8A1C, 0.35),
            d.Image(folder, id, "_dead") ?? ImageSprites.Squash(image));

        SpriteImage idle = d.RequiredPicture("idle");
        return Build(d, id, idle, d.PictureOr("walk", idle, "idle"), d.PictureOr("attack", idle, "idle"), d.PictureOr("dead", idle, "idle"));
    }

    private static MonsterDesign Build(DesignText d, string id, SpriteImage idle, SpriteImage walk, SpriteImage attack, SpriteImage dead)
    {
        Behavior behavior = Behavior.Skirmisher;
        if ((d.Text("behavior") ?? d.Text("behaviour")) is { } b)
        {
            if (!Enum.TryParse(b.Trim(), true, out behavior))
            {
                d.Warnings.Add("behavior should be skirmisher, rusher, sniper or tank");
                behavior = Behavior.Skirmisher;
            }
        }
        (int Min, int Max) melee = d.Range("melee damage", 0, 0);
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
            FloatHeight = d.Number("float height", 0, 0, 3),
            Boss = d.Flag("boss", false),
            Glitch = d.Number("glitch", 0, 0, 1),
            Behavior = behavior,
            PreferredRange = d.Number("preferred range", behavior switch
            {
                Behavior.Rusher => 0.4,
                Behavior.Sniper => 7,
                Behavior.Tank => 2.5,
                _ => 4,
            }, 0, 30),
            Strafe = d.Number("strafe", behavior == Behavior.Tank ? 0.15 : 0.45, 0, 1),
            AimLead = d.Number("aim lead", behavior == Behavior.Sniper ? 0.7 : 0.3, 0, 1),
            Dodge = d.Number("dodge", behavior == Behavior.Tank ? 0.05 : 0.3, 0, 1),
            MeleeMin = melee.Min,
            MeleeMax = melee.Max,
            Shots = (int)d.Number("shots", 1, 1, 20),
            ShotSpread = d.Number("shot spread", 15, 0, 180),
            ProjectileColor = ParseColor(d.Text("shot color")),
            Idle = idle,
            Walk = walk,
            Attack = attack,
            Dead = dead,
        };
    }

    private static int? ParseColor(string? text)
    {
        if (text == null) return null;
        string t = text.Trim().TrimStart('#');
        return t.Length == 6 && int.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out int rgb) ? rgb : null;
    }
}
