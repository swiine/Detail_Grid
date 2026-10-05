namespace CivDoom.Engine;

public enum GateRule
{
    /// <summary>Every boss in the level must be defeated.</summary>
    AllBosses,

    /// <summary>Only the final boss (the one closest to the finish line) must be defeated.</summary>
    FinalBoss,
}

public enum PickupKind
{
    Health,
    Ammo,
    Weapon,

    /// <summary>A keycard (see <see cref="PickupSpawn.Key"/>).</summary>
    Key,
}

/// <param name="Kind">Monster id (its file name, e.g. "proxy"), or null for a random monster.</param>
public readonly record struct EnemySpawn(Vec2 Position, string? Kind = null);

/// <param name="Weapon">For weapon pickups: the weapon id (its file name, e.g. "shotgun"), or null to deal one out.</param>
/// <param name="Key">For key pickups: which keycard.</param>
public readonly record struct PickupSpawn(Vec2 Position, PickupKind Kind, string? Weapon = null, KeyColor Key = KeyColor.None);

/// <summary>A locked door segment and the keycard that opens it.</summary>
public readonly record struct DoorSpawn(Vec2 A, Vec2 B, KeyColor Key);

/// <summary>A raised floor in a drawing or generated level: a closed outline and the height of its top.</summary>
/// <param name="Height">Height of the top in world units (wall heights), or in drawing units inside <see cref="DrawingGeometry"/>.</param>
public readonly record struct PlatformSpawn(IReadOnlyList<Vec2> Outline, double Height, int Color = Terrain.DefaultColor);

/// <summary>A playable map. All coordinates are in world units where one unit equals the wall height.</summary>
public sealed class Level
{
    public Level(
        string name,
        IReadOnlyList<Wall> walls,
        Vec2 playerStart,
        double playerAngle,
        IReadOnlyList<EnemySpawn> enemies,
        IReadOnlyList<PickupSpawn> pickups)
    {
        Name = name;
        Walls = walls;
        PlayerStart = playerStart;
        PlayerAngle = playerAngle;
        Enemies = enemies;
        Pickups = pickups;
        Index = new SpatialIndex(walls);
    }

    public string Name { get; }

    /// <summary>Drawing coordinates of world (0, 0), for levels built from a drawing.</summary>
    public Vec2 DrawingOrigin { get; init; }

    /// <summary>Drawing units per world unit (the wall height in drawing units).</summary>
    public double DrawingScale { get; init; } = 1;

    public Vec2 ToDrawing(Vec2 world) => DrawingOrigin + world * DrawingScale;

    /// <summary>The finish line, if the level has one. Reaching it once the gate rule is met wins.</summary>
    public Vec2? Exit { get; init; }

    /// <summary>Theme id (a themes\ folder name), <see cref="ThemeSet.RandomTheme"/>, or null for Classic.</summary>
    public string? ThemeId { get; init; }

    /// <summary>What opens the gates (and the finish line).</summary>
    public GateRule GateRule { get; init; } = GateRule.AllBosses;

    public IEnumerable<Wall> Gates => Walls.Where(w => w.IsGate);

    /// <summary>Locked doors (each opens with its keycard).</summary>
    public IEnumerable<Wall> Doors => Walls.Where(w => w.IsDoor);

    /// <summary>Raised floors and their ledges (flat if the level has none).</summary>
    public Terrain Terrain { get; init; } = Terrain.Flat;

    public IReadOnlyList<Wall> Walls { get; }
    public Vec2 PlayerStart { get; }

    /// <summary>Facing direction in radians, counter-clockwise from +X (AutoCAD convention).</summary>
    public double PlayerAngle { get; }

    public IReadOnlyList<EnemySpawn> Enemies { get; }
    public IReadOnlyList<PickupSpawn> Pickups { get; }
    public SpatialIndex Index { get; }
}
