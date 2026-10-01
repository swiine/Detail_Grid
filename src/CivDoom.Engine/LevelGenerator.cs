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

    /// <summary>The finish line, in the dead-end room behind the last boss arena.</summary>
    public Vec2? Exit { get; set; }

    /// <summary>Gate segments across the finish room's doorway(s). Closed until the bosses are beaten.</summary>
    public List<(Vec2 A, Vec2 B)> Gates { get; } = new();

    /// <summary>Wall thickness in cells (each outline gets a parallel outer face this far out). 0 = single lines.</summary>
    public double WallThickness { get; set; }

    /// <summary>The outer faces of the walls, one per outline in <see cref="Walls"/> (empty if no thickness).</summary>
    public List<List<Vec2>> OuterWalls { get; } = new();

    /// <summary>Where each boss fight happens (also in <see cref="Monsters"/>, as random-boss spawns), in order.</summary>
    public List<Vec2> Bosses { get; } = new();

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
        var g = new DrawingGeometry { PlayerStart = D(Start), PlayerAngle = StartAngle, Exit = Exit is { } e ? D(e) : null };
        foreach (List<Vec2> loop in Walls)
            for (int i = 0; i < loop.Count; i++)
                g.Segments.Add(new DrawingSegment(D(loop[i]), D(loop[(i + 1) % loop.Count]), color));
        foreach (List<Vec2> loop in OuterWalls)
            for (int i = 0; i < loop.Count; i++)
                g.Segments.Add(new DrawingSegment(D(loop[i]), D(loop[(i + 1) % loop.Count]), color));
        foreach ((Vec2 a, Vec2 b) in Gates) g.GateSegments.Add(new DrawingSegment(D(a), D(b), LevelBuilder.GateColor));
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

    /// <summary>How many boss fights a level of this size gets (inclusive range).</summary>
    public static (int Min, int Max) BossRange(LevelSize size) => size switch
    {
        LevelSize.Small => (1, 2),
        LevelSize.Large => (4, 5),
        _ => (2, 3),
    };

    /// <summary>
    /// Generates a level laid out as: start room, then rooms of monsters leading to a boss arena, more rooms,
    /// the next arena, and so on, ending in a dead-end finish room behind the last arena. Every arena is a
    /// choke point: you can't reach the finish line without passing through each of them.
    /// </summary>
    /// <param name="bosses">Number of boss fights, or null to pick from <see cref="BossRange"/>.</param>
    /// <param name="wallThickness">Wall thickness in cells (1 cell = 1 wall height); 0 for single-line walls.</param>
    public static GeneratedLevel Generate(int seed, LevelSize size = LevelSize.Medium, int? bosses = null, double wallThickness = 0.2)
    {
        (int min, int max) = BossRange(size);
        int count = Math.Clamp(bosses ?? new Random(seed).Next(min, max + 1), 0, 8);
        GeneratedLevel? level = null;
        for (int attempt = 0; attempt < 200 && level == null; attempt++)
            level = TryGenerate(seed + attempt * 7919, size, count, validate: true);
        level ??= TryGenerate(seed, size, count, validate: false)!;

        // Solid walls of constant thickness. Rooms are at least 2 cells apart, so faces never touch.
        double t = Math.Clamp(wallThickness, 0, 0.45);
        level.WallThickness = t;
        if (t > 0) level.OuterWalls.AddRange(level.Walls.Select(loop => OffsetOutline(loop, t)));
        return level;
    }

    private static GeneratedLevel? TryGenerate(int seed, LevelSize size, int bossCount, bool validate)
    {
        var rng = new Random(seed);

        // Rooms sit in a grid of slots visited in a snake (left to right, up a row, right to left...),
        // so the level is one linear chain with straight corridors only between neighbouring rooms.
        (int cols, int rows) = size switch
        {
            LevelSize.Small => (3, 2),
            LevelSize.Large => (6, 3),
            _ => (5, 2),
        };
        const int slotW = 13, slotH = 11;
        int w = cols * slotW + 2, h = rows * slotH + 2;
        int n = cols * rows;
        if (n < 2 * bossCount + 2) return null;

        int finish = n - 1;
        var arenas = new List<int>();
        for (int k = 1; k <= bossCount; k++) arenas.Add((int)Math.Round(k * (n - 2) / (double)bossCount));
        if (arenas.Distinct().Count() != arenas.Count || arenas.Any(i => i < 1 || i >= finish)) return null;

        (int Col, int Row) Slot(int i)
        {
            int row = i / cols, c = i % cols;
            return (row % 2 == 0 ? c : cols - 1 - c, row);
        }

        var rooms = new List<Room>();
        for (int i = 0; i < n; i++)
        {
            (int col, int row) = Slot(i);
            int sx = 1 + col * slotW, sy = 1 + row * slotH;
            bool arena = arenas.Contains(i);
            int rw = arena ? slotW - 3 : rng.Next(5, slotW - 3);
            int rh = arena ? slotH - 3 : rng.Next(4, slotH - 3);
            // Keep a margin inside the slot, and always cover the slot's centre lines so the straight
            // corridors (which run along those lines) connect.
            int cx = sx + slotW / 2, cy = sy + slotH / 2;
            int x = Math.Clamp(sx + 1 + rng.Next(0, slotW - 2 - rw + 1), Math.Max(sx + 1, cx + 2 - rw), Math.Min(sx + slotW - 1 - rw, cx - 1));
            int y = Math.Clamp(sy + 1 + rng.Next(0, slotH - 2 - rh + 1), Math.Max(sy + 1, cy + 2 - rh), Math.Min(sy + slotH - 1 - rh, cy - 1));
            rooms.Add(new Room(x, y, rw, rh));
        }

        var floor = new bool[w, h];
        foreach (Room r in rooms) Carve(floor, r.X, r.Y, r.W, r.H);
        for (int i = 1; i < n; i++)
        {
            (int c0, int r0) = Slot(i - 1);
            (int c1, int r1) = Slot(i);
            int ax = 1 + c0 * slotW + slotW / 2, ay = 1 + r0 * slotH + slotH / 2;
            int bx = 1 + c1 * slotW + slotW / 2, by = 1 + r1 * slotH + slotH / 2;
            if (r0 == r1) Carve(floor, Math.Min(ax, bx), ay - 1, Math.Abs(bx - ax) + 1, 2);
            else Carve(floor, ax - 1, Math.Min(ay, by), 2, Math.Abs(by - ay) + 1);
        }

        // Pillars (cover) in the bigger ordinary rooms and in arenas, away from the room centre.
        for (int i = 1; i < finish; i++)
        {
            Room r = rooms[i];
            if (r.W < 7 || r.H < 6 || (!arenas.Contains(i) && rng.Next(2) == 0)) continue;
            int px = r.X + rng.Next(2, r.W - 3), py = r.Y + rng.Next(2, r.H - 3);
            if (Math.Abs(px + 0.5 - r.Center.X) < 2 && Math.Abs(py + 0.5 - r.Center.Y) < 2) continue;
            floor[px, py] = false;
            if (rng.Next(2) == 0 && px + 1 < r.X + r.W - 2) floor[px + 1, py] = false;
        }

        RemovePinches(floor);

        if (validate)
        {
            (int, int) Cell(Vec2 c) => ((int)c.X, (int)c.Y);
            (int, int) start = Cell(rooms[0].Center), end = Cell(rooms[finish].Center);
            if (!floor[start.Item1, start.Item2] || !floor[end.Item1, end.Item2]) return null;
            if (!Reaches(floor, start, end, blocked: null)) return null;
            foreach (int i in arenas)
                if (Reaches(floor, start, end, blocked: rooms[i])) return null;
        }

        var level = new GeneratedLevel { Width = w, Height = h, Floor = floor };
        level.Walls.AddRange(TraceOutlines(floor));
        level.Gates.AddRange(Doorways(floor, rooms[finish]));

        Room first = rooms[0];
        level.Start = first.Center;
        Vec2 toward = rooms[1].Center - first.Center;
        level.StartAngle = Math.Atan2(toward.Y, toward.X);
        level.Exit = rooms[finish].Center;

        foreach (int i in arenas)
        {
            Room r = rooms[i];
            Vec2 spot = floor[(int)r.Center.X, (int)r.Center.Y] ? r.Center : FreeSpot(floor, r, rng, level) ?? r.Center;
            level.Monsters.Add(new EnemySpawn(spot, MonsterSet.RandomBoss));
            level.Bosses.Add(spot);
        }

        int weaponIndex = 0;
        for (int i = 1; i < finish; i++)
        {
            if (arenas.Contains(i)) continue;
            Room r = rooms[i];
            int monsters = Math.Max(1, r.W * r.H / 14);
            for (int m = 0; m < monsters; m++)
            {
                if (FreeSpot(floor, r, rng, level) is { } p) level.Monsters.Add(new EnemySpawn(p));
            }

            // Stock up before a boss fight; otherwise a random item.
            bool beforeArena = arenas.Contains(i + 1);
            if (FreeSpot(floor, r, rng, level) is { } item)
                level.Pickups.Add(new PickupSpawn(item, beforeArena || rng.Next(2) == 0 ? PickupKind.Health : PickupKind.Ammo));
            if (beforeArena && FreeSpot(floor, r, rng, level) is { } ammo)
                level.Pickups.Add(new PickupSpawn(ammo, PickupKind.Ammo));
            if (weaponIndex < LevelBuilder.WeaponPickups && FreeSpot(floor, r, rng, level) is { } wp)
            {
                level.Pickups.Add(new PickupSpawn(wp, PickupKind.Weapon));
                weaponIndex++;
            }
        }
        return level;
    }

    /// <summary>
    /// Segments across every opening in a room's outline (where a corridor comes in), used as gates.
    /// </summary>
    private static IEnumerable<(Vec2 A, Vec2 B)> Doorways(bool[,] floor, Room r)
    {
        int w = floor.GetLength(0), h = floor.GetLength(1);
        bool F(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && floor[x, y];

        IEnumerable<(Vec2, Vec2)> Runs(int count, Func<int, bool> open, Func<int, int, (Vec2, Vec2)> segment)
        {
            int start = -1;
            for (int i = 0; i <= count; i++)
            {
                bool o = i < count && open(i);
                if (o && start < 0) start = i;
                if (!o && start >= 0) { yield return segment(start, i); start = -1; }
            }
        }

        // An opening is a floor cell just outside the room next to a floor cell just inside it.
        foreach (var s in Runs(r.H, i => F(r.X - 1, r.Y + i) && F(r.X, r.Y + i), (a, b) => (new Vec2(r.X, r.Y + a), new Vec2(r.X, r.Y + b)))) yield return s;
        foreach (var s in Runs(r.H, i => F(r.X + r.W, r.Y + i) && F(r.X + r.W - 1, r.Y + i), (a, b) => (new Vec2(r.X + r.W, r.Y + a), new Vec2(r.X + r.W, r.Y + b)))) yield return s;
        foreach (var s in Runs(r.W, i => F(r.X + i, r.Y - 1) && F(r.X + i, r.Y), (a, b) => (new Vec2(r.X + a, r.Y), new Vec2(r.X + b, r.Y)))) yield return s;
        foreach (var s in Runs(r.W, i => F(r.X + i, r.Y + r.H) && F(r.X + i, r.Y + r.H - 1), (a, b) => (new Vec2(r.X + a, r.Y + r.H), new Vec2(r.X + b, r.Y + r.H)))) yield return s;
    }

    /// <summary>
    /// Offsets a traced outline (floor on its left) to its right by <paramref name="t"/>, i.e. into the
    /// solid side, giving the outer face of a wall of constant thickness. Outlines are axis-aligned, so
    /// each corner moves by the sum of its two edge normals (an exact mitre for right angles).
    /// </summary>
    internal static List<Vec2> OffsetOutline(List<Vec2> loop, double t)
    {
        var result = new List<Vec2>(loop.Count);
        for (int i = 0; i < loop.Count; i++)
        {
            Vec2 prev = loop[(i - 1 + loop.Count) % loop.Count], cur = loop[i], next = loop[(i + 1) % loop.Count];
            Vec2 n1 = (cur - prev).Normalized().PerpRight(), n2 = (next - cur).Normalized().PerpRight();
            result.Add(cur + (n1 + n2) * t);
        }
        return result;
    }

    /// <summary>Flood fill over floor cells, optionally treating a room as solid.</summary>
    private static bool Reaches(bool[,] floor, (int X, int Y) from, (int X, int Y) to, Room? blocked)
    {
        int w = floor.GetLength(0), h = floor.GetLength(1);
        bool Open(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && floor[x, y]
            && !(blocked is { } b && x >= b.X && x < b.X + b.W && y >= b.Y && y < b.Y + b.H);
        if (!Open(from.X, from.Y)) return false;
        var seen = new bool[w, h];
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue(from);
        seen[from.X, from.Y] = true;
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            if ((x, y) == to) return true;
            foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (!Open(nx, ny) || seen[nx, ny]) continue;
                seen[nx, ny] = true;
                queue.Enqueue((nx, ny));
            }
        }
        return false;
    }

    private static void Carve(bool[,] floor, int x, int y, int w, int h)
    {
        for (int i = Math.Max(1, x); i < Math.Min(floor.GetLength(0) - 1, x + w); i++)
            for (int j = Math.Max(1, y); j < Math.Min(floor.GetLength(1) - 1, y + h); j++)
                floor[i, j] = true;
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
