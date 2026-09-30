namespace CivDoom.Engine;

public enum EnemyKind
{
    /// <summary>Fast, fragile fireball thrower.</summary>
    Imp,

    /// <summary>Slow, tough, hits hard.</summary>
    Brute,
}

public enum PickupKind
{
    Health,
    Ammo,
}

public readonly record struct EnemySpawn(Vec2 Position, EnemyKind Kind);

public readonly record struct PickupSpawn(Vec2 Position, PickupKind Kind);

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
