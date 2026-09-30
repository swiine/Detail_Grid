namespace CivDoom.Engine;

public enum LevelSize
{
    Small,
    Medium,
    Large,
}

/// <summary>A generated level as drafting-friendly pieces, in grid units (1 = one cell).</summary>
public sealed class GeneratedLevel
{
    /// <summary>Closed wall outlines (outer boundary of each floor area, plus pillars). No repeated closing vertex.</summary>
    public List<List<Vec2>> Walls { get; } = new();

    public Vec2 Start { get; set; }

    /// <summary>Radians, counter-clockwise from +X.</summary>
    public double StartAngle { get; set; }

    public List<EnemySpawn> Monsters { get; } = new();
    public List<PickupSpawn> Pickups { get; } = new();

    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>Floor cells, [x, y] with y up.</summary>
    public bool[,] Floor { get; init; } = new bool[0, 0];

    /// <summary>
    /// Converts to <see cref="DrawingGeometry"/> as if drawn at <paramref name="origin"/> with
    /// <paramref name="cellSize"/> drawing units per cell (handy for testing and previews).
    /// </summary>
    public DrawingGeometry ToGeometry(Vec2 origin, double cellSize, int color = 0x8C8C8C)
    {
        Vec2 D(Vec2 p) => origin + p * cellSize;
        var g = new DrawingGeometry { PlayerStart = D(Start), PlayerAngle = StartAngle };
        foreach (List<Vec2> loop in Walls)
            for (int i = 0; i < loop.Count; i++)
                g.Segments.Add(new DrawingSegment(D(loop[i]), D(loop[(i + 1) % loop.Count]), color));
        g.Monsters.AddRange(Monsters.Select(m => m with { Position = D(m.Position) }));
        g.Pickups.AddRange(Pickups.Select(p => p with { Position = D(p.Position) }));
        return g;
    }
}

/// <summary>Rooms-and-corridors level generator.</summary>
public static class LevelGenerator
{
    private readonly record struct Room(int X, int Y, int W, int H)
    {
        public Vec2 Center => new(X + W / 2.0, Y + H / 2.0);
        public bool Overlaps(Room o, int gap) =>
            X - gap < o.X + o.W && o.X - gap < X + W && Y - gap < o.Y + o.H && o.Y - gap < Y + H;
    }

    public static GeneratedLevel Generate(int seed, LevelSize size = LevelSize.Medium)
    {
        var rng = new Random(seed);
        (int w, int h, int rooms) = size switch
        {
            LevelSize.Small => (28, 20, 5),
            LevelSize.Large => (64, 48, 14),
            _ => (44, 32, 9),
        };

        var floor = new bool[w, h];
        var placed = new List<Room>();
        for (int attempt = 0; attempt < rooms * 40 && placed.Count < rooms; attempt++)
        {
            int rw = rng.Next(4, 10), rh = rng.Next(4, 9);
            var r = new Room(rng.Next(1, w - rw - 1), rng.Next(1, h - rh - 1), rw, rh);
            if (placed.Any(o => o.Overlaps(r, 2))) continue;
            placed.Add(r);
        }

        foreach (Room r in placed) Carve(floor, r.X, r.Y, r.W, r.H);

        // Chain the rooms left-to-right with 2-wide L-shaped corridors, plus one loop back for variety.
        placed.Sort((a, b) => a.Center.X.CompareTo(b.Center.X));
        for (int i = 1; i < placed.Count; i++) Corridor(floor, placed[i - 1], placed[i], rng);
        if (placed.Count > 3) Corridor(floor, placed[0], placed[placed.Count / 2 + 1], rng);

        // Pillars in the bigger rooms (they become little closed outlines to walk around).
        foreach (Room r in placed.Skip(1))
        {
            if (r.W < 7 || r.H < 6 || rng.Next(2) == 0) continue;
            int px = r.X + rng.Next(2, r.W - 3), py = r.Y + rng.Next(2, r.H - 3);
            floor[px, py] = false;
            if (rng.Next(2) == 0 && px + 1 < r.X + r.W - 2) floor[px + 1, py] = false;
        }

        RemovePinches(floor);

        var level = new GeneratedLevel { Width = w, Height = h, Floor = floor };
        level.Walls.AddRange(TraceOutlines(floor));

        Room first = placed[0];
        level.Start = first.Center;
        Vec2 toward = (placed.Count > 1 ? placed[1].Center : first.Center + new Vec2(1, 0)) - first.Center;
        level.StartAngle = Math.Atan2(toward.Y, toward.X);

        int weaponIndex = 0;
        foreach (Room r in placed.Skip(1))
        {
            int monsters = Math.Max(1, r.W * r.H / 14);
            for (int i = 0; i < monsters; i++)
            {
                if (FreeSpot(floor, r, rng, level) is { } p) level.Monsters.Add(new EnemySpawn(p));
            }
            if (FreeSpot(floor, r, rng, level) is { } item)
                level.Pickups.Add(new PickupSpawn(item, rng.Next(2) == 0 ? PickupKind.Health : PickupKind.Ammo));
            if (weaponIndex < LevelBuilder.WeaponPickups && FreeSpot(floor, r, rng, level) is { } wp)
            {
                level.Pickups.Add(new PickupSpawn(wp, PickupKind.Weapon));
                weaponIndex++;
            }
        }
        return level;
    }

    private static void Carve(bool[,] floor, int x, int y, int w, int h)
    {
        for (int i = Math.Max(1, x); i < Math.Min(floor.GetLength(0) - 1, x + w); i++)
            for (int j = Math.Max(1, y); j < Math.Min(floor.GetLength(1) - 1, y + h); j++)
                floor[i, j] = true;
    }

    private static void Corridor(bool[,] floor, Room a, Room b, Random rng)
    {
        int ax = (int)a.Center.X, ay = (int)a.Center.Y, bx = (int)b.Center.X, by = (int)b.Center.Y;
        if (rng.Next(2) == 0)
        {
            Carve(floor, Math.Min(ax, bx), ay - 1, Math.Abs(bx - ax) + 2, 2);
            Carve(floor, bx - 1, Math.Min(ay, by) - 1, 2, Math.Abs(by - ay) + 2);
        }
        else
        {
            Carve(floor, ax - 1, Math.Min(ay, by) - 1, 2, Math.Abs(by - ay) + 2);
            Carve(floor, Math.Min(ax, bx), by - 1, Math.Abs(bx - ax) + 2, 2);
        }
    }

    /// <summary>
    /// Fills in diagonal-only contacts (two floor cells touching at a corner) so every outline vertex has a
    /// single way out, which keeps the traced polylines simple and closed.
    /// </summary>
    private static void RemovePinches(bool[,] floor)
    {
        int w = floor.GetLength(0), h = floor.GetLength(1);
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int x = 1; x < w - 2; x++)
            {
                for (int y = 1; y < h - 2; y++)
                {
                    bool a = floor[x, y], b = floor[x + 1, y], c = floor[x, y + 1], d = floor[x + 1, y + 1];
                    if (a && d && !b && !c) { floor[x + 1, y] = true; changed = true; }
                    else if (b && c && !a && !d) { floor[x, y] = true; changed = true; }
                }
            }
        }
    }

    /// <summary>
    /// Walks the boundary between floor and solid cells. Edges are directed with the floor on the left,
    /// so they chain into closed loops; collinear points are dropped.
    /// </summary>
    internal static List<List<Vec2>> TraceOutlines(bool[,] floor)
    {
        int w = floor.GetLength(0), h = floor.GetLength(1);
        bool F(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && floor[x, y];

        var next = new Dictionary<(int, int), (int, int)>();
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                if (!F(x, y)) continue;
                if (!F(x, y - 1)) next[(x, y)] = (x + 1, y);             // bottom edge, going east
                if (!F(x + 1, y)) next[(x + 1, y)] = (x + 1, y + 1);     // right edge, going north
                if (!F(x, y + 1)) next[(x + 1, y + 1)] = (x, y + 1);     // top edge, going west
                if (!F(x - 1, y)) next[(x, y + 1)] = (x, y);             // left edge, going south
            }
        }

        var loops = new List<List<Vec2>>();
        var visited = new HashSet<(int, int)>();
        foreach ((int, int) startPt in next.Keys.OrderBy(k => k.Item2).ThenBy(k => k.Item1))
        {
            if (visited.Contains(startPt)) continue;
            var pts = new List<(int X, int Y)>();
            (int, int) p = startPt;
            while (visited.Add(p))
            {
                pts.Add(p);
                if (!next.TryGetValue(p, out p)) break;
            }

            var loop = new List<Vec2>();
            for (int i = 0; i < pts.Count; i++)
            {
                var prev = pts[(i - 1 + pts.Count) % pts.Count];
                var cur = pts[i];
                var nxt = pts[(i + 1) % pts.Count];
                bool collinear = (cur.X - prev.X) * (nxt.Y - cur.Y) - (cur.Y - prev.Y) * (nxt.X - cur.X) == 0;
                if (!collinear) loop.Add(new Vec2(cur.X, cur.Y));
            }
            if (loop.Count >= 3) loops.Add(loop);
        }
        return loops;
    }

    private static Vec2? FreeSpot(bool[,] floor, Room r, Random rng, GeneratedLevel level)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            int x = r.X + rng.Next(1, Math.Max(2, r.W - 1)), y = r.Y + rng.Next(1, Math.Max(2, r.H - 1));
            if (!floor[x, y]) continue;
            var p = new Vec2(x + 0.5, y + 0.5);
            if (Vec2.Distance(p, level.Start) < 3) continue;
            if (level.Monsters.Any(m => Vec2.Distance(m.Position, p) < 1)) continue;
            if (level.Pickups.Any(q => Vec2.Distance(q.Position, p) < 1)) continue;
            return p;
        }
        return null;
    }
}
