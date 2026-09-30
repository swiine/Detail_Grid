namespace CivDoom.Engine;

/// <summary>A 2D line segment pulled out of a drawing, in drawing units.</summary>
public readonly record struct DrawingSegment(Vec2 A, Vec2 B, int Color);

/// <summary>Raw geometry harvested from a CAD drawing, before it is scaled into world units.</summary>
public sealed class DrawingGeometry
{
    public List<DrawingSegment> Segments { get; } = new();

    /// <summary>Points (e.g. POINT or COGO point entities) that become enemy spawns.</summary>
    public List<Vec2> EnemyPoints { get; } = new();

    public Vec2? PlayerStart { get; set; }

    /// <summary>Radians, counter-clockwise from +X.</summary>
    public double PlayerAngle { get; set; }
}

public static class LevelBuilder
{
    /// <summary>Hard cap so a monster site plan can't make the game unplayably slow.</summary>
    public const int MaxWalls = 150_000;

    public const double PlayerRadius = 0.12;

    /// <summary>
    /// Converts drawing geometry into a level. <paramref name="wallHeight"/> is the wall height in
    /// drawing units (e.g. 10 for a plan in feet); it becomes one world unit.
    /// </summary>
    public static Level FromDrawing(DrawingGeometry geometry, double wallHeight, int seed, string name = "Current Drawing")
    {
        if (wallHeight <= 0 || double.IsNaN(wallHeight)) throw new ArgumentOutOfRangeException(nameof(wallHeight));

        Vec2 origin = geometry.PlayerStart ?? ExtentsCenter(geometry);
        double scale = 1.0 / wallHeight;
        Vec2 ToWorld(Vec2 p) => (p - origin) * scale;

        var walls = new List<Wall>(Math.Min(geometry.Segments.Count, MaxWalls));
        foreach (DrawingSegment s in geometry.Segments)
        {
            Vec2 a = ToWorld(s.A), b = ToWorld(s.B);
            if (!IsFinite(a) || !IsFinite(b) || (b - a).LengthSquared < 1e-8) continue;
            walls.Add(new Wall(a, b, s.Color));
            if (walls.Count >= MaxWalls) break;
        }

        var index = new SpatialIndex(walls);
        Vec2 start = FindClearSpot(index, Vec2.Zero, PlayerRadius * 1.5);

        var rng = new Random(seed);
        List<EnemySpawn> enemies;
        Reachability? reach = null;
        if (geometry.EnemyPoints.Count > 0)
        {
            enemies = geometry.EnemyPoints
                .Select(ToWorld)
                .Where(IsFinite)
                .Select((p, i) => new EnemySpawn(p, i % 5 == 4 ? EnemyKind.Brute : EnemyKind.Imp))
                .ToList();
        }
        else
        {
            reach = new Reachability(index, start, PlayerRadius);
            enemies = AutoPlaceEnemies(index, reach, start, rng);
        }

        reach ??= new Reachability(index, start, PlayerRadius);
        List<PickupSpawn> pickups = AutoPlacePickups(index, reach, start, enemies.Count, rng);

        return new Level(name, walls, start, geometry.PlayerAngle, enemies, pickups);
    }

    private static bool IsFinite(Vec2 v) => double.IsFinite(v.X) && double.IsFinite(v.Y);

    private static Vec2 ExtentsCenter(DrawingGeometry g)
    {
        if (g.Segments.Count == 0) return Vec2.Zero;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (DrawingSegment s in g.Segments)
        {
            minX = Math.Min(minX, Math.Min(s.A.X, s.B.X));
            minY = Math.Min(minY, Math.Min(s.A.Y, s.B.Y));
            maxX = Math.Max(maxX, Math.Max(s.A.X, s.B.X));
            maxY = Math.Max(maxY, Math.Max(s.A.Y, s.B.Y));
        }
        return new Vec2((minX + maxX) / 2, (minY + maxY) / 2);
    }

    /// <summary>Spirals outward from <paramref name="p"/> until a spot with no wall within <paramref name="radius"/>.</summary>
    public static Vec2 FindClearSpot(SpatialIndex index, Vec2 p, double radius)
    {
        if (Reachability.HasClearance(index, p, radius)) return p;
        for (int ring = 1; ring <= 200; ring++)
        {
            double r = ring * radius;
            int steps = 8 * ring;
            for (int i = 0; i < steps; i++)
            {
                double a = i * 2 * Math.PI / steps;
                Vec2 q = p + Vec2.FromAngle(a) * r;
                if (Reachability.HasClearance(index, q, radius)) return q;
            }
        }
        return p;
    }

    private static List<EnemySpawn> AutoPlaceEnemies(SpatialIndex index, Reachability reach, Vec2 start, Random rng)
    {
        List<Vec2> cells = reach.ReachableCells().ToList();
        double area = cells.Count * reach.CellSize * reach.CellSize;
        int count = Math.Clamp((int)(area / 20), 3, 30);

        List<Vec2> spots = PickSpots(index, cells, start, count, minFromStart: 4, minSeparation: 1.5, clearance: 0.3, rng);
        if (spots.Count < count)
        {
            // Small or cramped drawing: relax the constraints rather than spawning nobody.
            spots.AddRange(PickSpots(index, cells, start, count - spots.Count, 1.5, 0.6, 0.2, rng, spots));
        }

        return spots.Select((p, i) => new EnemySpawn(p, i % 5 == 4 ? EnemyKind.Brute : EnemyKind.Imp)).ToList();
    }

    private static List<PickupSpawn> AutoPlacePickups(SpatialIndex index, Reachability reach, Vec2 start, int enemyCount, Random rng)
    {
        List<Vec2> cells = reach.ReachableCells().ToList();
        int count = Math.Clamp(enemyCount / 2 + 2, 2, 20);
        List<Vec2> spots = PickSpots(index, cells, start, count, minFromStart: 1.5, minSeparation: 1.0, clearance: 0.15, rng);
        return spots.Select((p, i) => new PickupSpawn(p, i % 2 == 0 ? PickupKind.Ammo : PickupKind.Health)).ToList();
    }

    private static List<Vec2> PickSpots(
        SpatialIndex index, List<Vec2> cells, Vec2 start, int count,
        double minFromStart, double minSeparation, double clearance, Random rng, List<Vec2>? existing = null)
    {
        var result = new List<Vec2>();
        if (cells.Count == 0 || count <= 0) return result;
        int attempts = Math.Min(cells.Count * 4, 20_000);
        for (int i = 0; i < attempts && result.Count < count; i++)
        {
            Vec2 p = cells[rng.Next(cells.Count)];
            if (Vec2.Distance(p, start) < minFromStart) continue;
            if (result.Any(q => Vec2.Distance(p, q) < minSeparation)) continue;
            if (existing != null && existing.Any(q => Vec2.Distance(p, q) < minSeparation)) continue;
            if (!Reachability.HasClearance(index, p, clearance)) continue;
            result.Add(p);
        }
        return result;
    }
}
