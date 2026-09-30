namespace CivDoom.Engine;

public enum PickupKind
{
    Health,
    Ammo,
    Weapon,
}

/// <param name="Kind">Monster id (its file name, e.g. "imp"), or null for a random monster.</param>
public readonly record struct EnemySpawn(Vec2 Position, string? Kind = null);

/// <param name="Weapon">For weapon pickups: the weapon id (its file name, e.g. "shotgun"), or null to deal one out.</param>
public readonly record struct PickupSpawn(Vec2 Position, PickupKind Kind, string? Weapon = null);

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
    public IReadOnlyList<Wall> Walls { get; }
    public Vec2 PlayerStart { get; }

    /// <summary>Facing direction in radians, counter-clockwise from +X (AutoCAD convention).</summary>
    public double PlayerAngle { get; }

    public IReadOnlyList<EnemySpawn> Enemies { get; }
    public IReadOnlyList<PickupSpawn> Pickups { get; }
    public SpatialIndex Index { get; }
}
