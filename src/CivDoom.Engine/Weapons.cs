namespace CivDoom.Engine;

/// <summary>Everything that defines one weapon: how it fires plus its pictures.</summary>
public sealed class WeaponDesign
{
    /// <summary>File name without extension, lower case (e.g. "shotgun").</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Number key that selects it (1-9). Weapons can share a slot; pressing the key again cycles.</summary>
    public int Slot { get; init; } = 2;

    public int DamageMin { get; init; } = 12;
    public int DamageMax { get; init; } = 25;

    /// <summary>Bullets (or flames) per shot.</summary>
    public int Pellets { get; init; } = 1;

    /// <summary>Total width of the random spread cone, in degrees.</summary>
    public double Spread { get; init; } = 2;

    public double FireDelay { get; init; } = 0.3;

    /// <summary>Shared ammo pool name (e.g. "bullets"), or "none" for weapons that never run out.</summary>
    public string AmmoType { get; init; } = "bullets";

    public int AmmoPerShot { get; init; } = 1;
    public bool StartWith { get; init; }
    public int StartAmmo { get; init; }
    public int MaxAmmo { get; init; } = 200;

    /// <summary>Ammo from an ammo box while this weapon is held.</summary>
    public int BoxAmmo { get; init; } = 20;

    /// <summary>Ammo that comes with the weapon when you pick it up.</summary>
    public int PickupAmmo { get; init; } = 20;

    /// <summary>Melee reach in wall heights. 0 = a gun with unlimited range.</summary>
    public double Range { get; init; }

    /// <summary>Melee: width of the swing in degrees. Everything inside it gets hit.</summary>
    public double Sweep { get; init; } = 30;

    /// <summary>0 = instant hit. Otherwise shots are flying projectiles (rockets, flames).</summary>
    public double ProjectileSpeed { get; init; }

    /// <summary>How far projectiles fly before fizzling out (0 = until they hit something).</summary>
    public double ProjectileRange { get; init; }

    public double ProjectileSize { get; init; } = 0.12;
    public double SplashRadius { get; init; }
    public int SplashDamage { get; init; }

    /// <summary>World height of the [pickup] picture lying on the floor.</summary>
    public double PickupSize { get; init; } = 0.22;

    public required SpriteImage Hand { get; init; }
    public required SpriteImage Fire { get; init; }
    public required SpriteImage Pickup { get; init; }
    public SpriteImage? Projectile { get; init; }

    public bool UsesAmmo => AmmoPerShot > 0 && !string.Equals(AmmoType, "none", StringComparison.OrdinalIgnoreCase);
    public bool IsMelee => Range > 0 && ProjectileSpeed <= 0;
}

/// <summary>The weapons available to a game, normally loaded from the "weapons" folder.</summary>
public sealed class WeaponSet
{
    private static readonly string[] DefaultFiles = { "chainsaw", "katana", "pistol", "shotgun", "chaingun", "rocketlauncher", "flamethrower" };
    private static WeaponSet? _builtIn;

    public WeaponSet(IEnumerable<WeaponDesign> designs, IReadOnlyList<string>? warnings = null)
    {
        Designs = designs.OrderBy(d => d.Slot).ThenBy(d => d.Id, StringComparer.Ordinal).ToList();
        if (Designs.Count == 0) throw new ArgumentException("At least one weapon is required.", nameof(designs));
        Warnings = warnings ?? Array.Empty<string>();
    }

    /// <summary>Sorted by slot.</summary>
    public IReadOnlyList<WeaponDesign> Designs { get; }

    public IReadOnlyList<string> Warnings { get; }

    public static WeaponSet BuiltIn => _builtIn ??= new WeaponSet(
        DefaultFiles.Select(id => WeaponFile.Parse(id, DefaultText(id), new List<string>())));

    public WeaponDesign? Find(string? id) =>
        id == null ? null : Designs.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    public static string DefaultText(string id) => DesignFolder.EmbeddedText("Weapons", id);

    public static void WriteDefaults(string folder) => DesignFolder.WriteDefaults(folder, "Weapons", DefaultFiles);

    public static WeaponSet Load(string folder)
    {
        var warnings = new List<string>();
        List<WeaponDesign> designs = DesignFolder.Load(
            folder, "weapon", () => WriteDefaults(folder), WeaponFile.Parse, BuiltIn.Designs, d => d.Id, warnings);
        return new WeaponSet(designs, warnings);
    }
}

/// <summary>Reads the weapon text format (see Weapons/shotgun.txt for a commented example).</summary>
public static class WeaponFile
{
    private static readonly string[] Pictures = { "hand", "fire", "pickup", "projectile" };

    /// <exception cref="FormatException">The file can't be used at all (e.g. no [hand] picture).</exception>
    public static WeaponDesign Parse(string id, string text, List<string> warnings)
    {
        DesignText d = DesignText.Parse(text, Pictures, warnings);
        SpriteImage hand = d.RequiredPicture("hand");
        (int dMin, int dMax) = d.Range("damage", 12, 25);
        return new WeaponDesign
        {
            Id = id,
            Name = d.Text("name") ?? DesignText.Capitalize(id),
            Slot = (int)d.Number("slot", 2, 1, 9),
            DamageMin = dMin,
            DamageMax = dMax,
            Pellets = (int)d.Number("pellets", 1, 1, 50),
            Spread = d.Number("spread", 2, 0, 180),
            FireDelay = d.Number("fire delay", 0.3, 0.02, 10),
            AmmoType = (d.Text("ammo type") ?? "bullets").ToLowerInvariant(),
            AmmoPerShot = (int)d.Number("ammo per shot", 1, 0, 1000),
            StartWith = d.Flag("start with", false),
            StartAmmo = (int)d.Number("start ammo", 0, 0, 100_000),
            MaxAmmo = (int)d.Number("max ammo", 200, 1, 100_000),
            BoxAmmo = (int)d.Number("box ammo", 20, 0, 100_000),
            PickupAmmo = (int)d.Number("pickup ammo", 20, 0, 100_000),
            Range = d.Number("range", 0, 0, 50),
            Sweep = d.Number("sweep", 30, 1, 360),
            ProjectileSpeed = d.Number("projectile speed", 0, 0, 100),
            ProjectileRange = d.Number("projectile range", 0, 0, 200),
            ProjectileSize = d.Number("projectile size", 0.12, 0.01, 2),
            SplashRadius = d.Number("splash radius", 0, 0, 20),
            SplashDamage = (int)d.Number("splash damage", 0, 0, 100_000),
            PickupSize = d.Number("pickup size", 0.22, 0.02, 3),
            Hand = hand,
            Fire = d.Picture("fire") ?? hand,
            Pickup = d.PictureOr("pickup", hand, "hand"),
            Projectile = d.Picture("projectile"),
        };
    }
}
