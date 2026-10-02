namespace CivDoom.Engine;

/// <summary>A 2D line segment pulled out of a drawing, in drawing units.</summary>
public readonly record struct DrawingSegment(Vec2 A, Vec2 B, int Color);

/// <summary>Raw geometry harvested from a CAD drawing, before it is scaled into world units.</summary>
public sealed class DrawingGeometry
{
    public List<DrawingSegment> Segments { get; } = new();

    /// <summary>Points (e.g. POINT or COGO point entities) that become random enemies.</summary>
    public List<Vec2> EnemyPoints { get; } = new();

    /// <summary>Monster markers (DOOM-MONSTER blocks), in drawing units. Kind null = random monster.</summary>
    public List<EnemySpawn> Monsters { get; } = new();

    /// <summary>Item and weapon markers (DOOM-HEALTH / DOOM-AMMO / DOOM-WEAPON blocks), in drawing units.</summary>
    public List<PickupSpawn> Pickups { get; } = new();

    /// <summary>Wall height suggested by the drawing (the scale of its DOOM-START block), if any.</summary>
    public double? SuggestedWallHeight { get; set; }

    public Vec2? PlayerStart { get; set; }

    /// <summary>The finish line (a DOOM-EXIT block), in drawing units.</summary>
    public Vec2? Exit { get; set; }

    /// <summary>Gate linework (anything on the DOOM-GATE layer), in drawing units.</summary>
    public List<DrawingSegment> GateSegments { get; } = new();

    /// <summary>
    /// Raised floors (closed polylines on the DOOM-PLATFORM layer), in drawing units: the outline, and the
    /// height of the top (the polyline's elevation).
    /// </summary>
    public List<PlatformSpawn> Platforms { get; } = new();

    /// <summary>Elevation of the ground floor (the DOOM-START block's Z): platform heights are measured from here.</summary>
    public double FloorElevation { get; set; }

    /// <summary>What opens the gates.</summary>
    public GateRule GateRule { get; set; } = GateRule.AllBosses;

    /// <summary>Theme id from a DOOM-THEME-&lt;NAME&gt; block, if any.</summary>
    public string? ThemeId { get; set; }

    /// <summary>Radians, counter-clockwise from +X.</summary>
    public double PlayerAngle { get; set; }
}

public static class LevelBuilder
{
    /// <summary>Hard cap so a monster site plan can't make the game unplayably slow.</summary>
    public const int MaxWalls = 150_000;

    public const double PlayerRadius = 0.12;

    /// <summary>Platforms are capped just under the wall tops (walls have no top faces to stand on).</summary>
    public const double MaxPlatformHeight = 0.9;

    /// <summary>Base colour of gate walls (drawn with hazard stripes).</summary>
    public const int GateColor = 0xE0C020;

    /// <summary>Weapon pickups placed in drawings (the shipped set has six weapons besides the pistol).</summary>
    public const int WeaponPickups = 6;

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
        foreach (DrawingSegment s in geometry.GateSegments)
        {
            Vec2 a = ToWorld(s.A), b = ToWorld(s.B);
            if (IsFinite(a) && IsFinite(b) && (b - a).LengthSquared >= 1e-8) walls.Add(new Wall(a, b, GateColor, isGate: true));
        }

        var index = new SpatialIndex(walls);
        Vec2 start = FindClearSpot(index, Vec2.Zero, PlayerRadius * 1.5);

        var rng = new Random(seed);
        List<EnemySpawn> enemies;
        Reachability? reach = null;
        if (geometry.EnemyPoints.Count > 0 || geometry.Monsters.Count > 0)
        {
            enemies = geometry.EnemyPoints.Select(p => new EnemySpawn(p))
                .Concat(geometry.Monsters)
                .Select(e => e with { Position = ToWorld(e.Position) })
                .Where(e => IsFinite(e.Position))
                .ToList();
        }
        else
        {
            reach = new Reachability(index, start, PlayerRadius);
            enemies = AutoPlaceEnemies(index, reach, start, rng);
        }

        // Pickups drawn as blocks are used as-is; only the kinds the drawing doesn't mark are auto-placed.
        List<PickupSpawn> pickups = geometry.Pickups
            .Select(p => p with { Position = ToWorld(p.Position) })
            .Where(p => IsFinite(p.Position))
            .ToList();
        bool markedItems = pickups.Any(p => p.Kind != PickupKind.Weapon);
        bool markedWeapons = pickups.Any(p => p.Kind == PickupKind.Weapon);
        if (!markedItems || !markedWeapons)
        {
            reach ??= new Reachability(index, start, PlayerRadius);
            pickups.AddRange(AutoPlacePickups(index, reach, start, enemies.Count, rng, !markedItems, !markedWeapons));
        }

        return new Level(name, walls, start, geometry.PlayerAngle, enemies, pickups)
        {
            DrawingOrigin = origin,
            DrawingScale = wallHeight,
            Exit = geometry.Exit is { } exit ? ToWorld(exit) : null,
            GateRule = geometry.GateRule,
            ThemeId = geometry.ThemeId,
            Terrain = Terrain.From(geometry.Platforms
                .Select(p => new PlatformSpawn(p.Outline.Select(ToWorld).ToList(), Math.Min(MaxPlatformHeight, (p.Height - geometry.FloorElevation) * scale), p.Color))
                .Where(p => p.Outline.All(IsFinite) && double.IsFinite(p.Height))),
        };
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

        return spots.Select(p => new EnemySpawn(p)).ToList();
    }

    private static List<PickupSpawn> AutoPlacePickups(
        SpatialIndex index, Reachability reach, Vec2 start, int enemyCount, Random rng, bool items, bool weapons)
    {
        List<Vec2> cells = reach.ReachableCells().ToList();
        int count = items ? Math.Clamp(enemyCount / 2 + 2, 2, 20) : 0;
        List<Vec2> spots = PickSpots(index, cells, start, count, minFromStart: 1.5, minSeparation: 1.0, clearance: 0.15, rng);
        var result = spots.Select((p, i) => new PickupSpawn(p, i % 2 == 0 ? PickupKind.Ammo : PickupKind.Health)).ToList();

        // Weapons are dealt out by the game (one of each you don't start with), so leave the id open.
        List<Vec2> weaponSpots = PickSpots(index, cells, start, weapons ? WeaponPickups : 0, minFromStart: 1.0, minSeparation: 1.5, clearance: 0.15, rng, spots);
        result.AddRange(weaponSpots.Select(p => new PickupSpawn(p, PickupKind.Weapon)));
        return result;
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
