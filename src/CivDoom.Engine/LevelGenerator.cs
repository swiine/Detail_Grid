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

    /// <summary>Keycard doors, in cells: each sits across a doorway, its key somewhere before it.</summary>
    public List<DoorSpawn> Doors { get; } = new();

    /// <summary>Raised floors, in cells, with heights in wall heights (1 cell = 1 wall height).</summary>
    public List<PlatformSpawn> Platforms { get; } = new();

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
        foreach (DoorSpawn d in Doors) g.Doors.Add(d with { A = D(d.A), B = D(d.B) });
        foreach (PlatformSpawn p in Platforms) g.Platforms.Add(new PlatformSpawn(p.Outline.Select(D).ToList(), p.Height * cellSize, p.Color));
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

        var rewards = AddPlatforms(level, floor, rooms, arenas, finish, rng);

        Room first = rooms[0];
        level.Start = first.Center;
        Vec2 toward = rooms[1].Center - first.Center;
        level.StartAngle = Math.Atan2(toward.Y, toward.X);
        level.Exit = rooms[finish].Center;
        AddKeyDoors(level, floor, rooms, arenas, finish, size, rewards, rng);

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
            if (rewards.TryGetValue(i, out Vec2 prize))
            {
                // Something worth the climb.
                bool weapon = weaponIndex < LevelBuilder.WeaponPickups;
                level.Pickups.Add(new PickupSpawn(prize, weapon ? PickupKind.Weapon : rng.Next(2) == 0 ? PickupKind.Health : PickupKind.Ammo));
                if (weapon) weaponIndex++;
            }
            if (weaponIndex < LevelBuilder.WeaponPickups && FreeSpot(floor, r, rng, level) is { } wp)
            {
                level.Pickups.Add(new PickupSpawn(wp, PickupKind.Weapon));
                weaponIndex++;
            }
        }
        return level;
    }

    /// <summary>How many keycard doors a level of this size gets.</summary>
    public static int KeyDoors(LevelSize size) => size switch
    {
        LevelSize.Small => 1,
        LevelSize.Large => 3,
        _ => 2,
    };

    /// <summary>
    /// Locks the way into a few rooms with keycard doors (red, then blue, then yellow), spread along the
    /// level. Each key lies in a room between the previous door and its own, preferably up on a platform,
    /// so you always find it before you need it and can't get past without it.
    /// </summary>
    private static void AddKeyDoors(GeneratedLevel level, bool[,] floor, List<Room> rooms, List<int> arenas, int finish,
                                    LevelSize size, Dictionary<int, Vec2> prizes, Random rng)
    {
        int want = Math.Min(KeyDoors(size), Keycards.All.Length);
        (int, int) Cell(Vec2 c) => ((int)c.X, (int)c.Y);
        var start = Cell(rooms[0].Center);
        int previous = 0; // the last locked room (keys come after it)
        for (int k = 0; k < want; k++)
        {
            // Spread the doors out: the k-th one roughly k+1 / want+1 of the way along.
            int target = (int)Math.Round((k + 1) * finish / (double)(want + 1));
            int j = Enumerable.Range(previous + 2, Math.Max(0, finish - previous - 2))
                .Where(i => !arenas.Contains(i - 1) || i == target) // keys don't sit in boss arenas if avoidable
                .OrderBy(i => Math.Abs(i - target)).FirstOrDefault();
            if (j <= previous + 1 || j >= finish) break;

            // Lock every opening into room j that can be reached from the start without going through it.
            Room locked = rooms[j];
            var doorways = Doorways(floor, locked).Where(s =>
            {
                (int ox, int oy) = Outside(s, locked);
                return Reaches(floor, start, (ox, oy), blocked: locked);
            }).ToList();
            if (doorways.Count == 0) continue;
            // Must not be able to get round it.
            if (Reaches(floor, start, Cell(rooms[finish].Center), blocked: locked)) continue;

            int keyRoom = Enumerable.Range(previous + 1, j - previous - 1).Where(i => i > 0)
                .OrderByDescending(i => prizes.ContainsKey(i) ? 1 : 0).ThenBy(_ => rng.Next()).FirstOrDefault();
            if (keyRoom <= 0) continue;
            Vec2? keyAt = prizes.TryGetValue(keyRoom, out Vec2 prize) ? prize : FreeSpot(floor, rooms[keyRoom], rng, level);
            if (keyAt is not { } spot) continue;
            prizes.Remove(keyRoom); // the key is the prize up there now

            KeyColor color = Keycards.All[k];
            foreach ((Vec2 a, Vec2 b) in doorways) level.Doors.Add(new DoorSpawn(a, b, color));
            level.Pickups.Add(new PickupSpawn(spot, PickupKind.Key, Key: color));
            previous = j;
        }
    }

    /// <summary>The floor cell just outside a room, next to the middle of one of its doorway segments.</summary>
    private static (int X, int Y) Outside((Vec2 A, Vec2 B) s, Room r)
    {
        Vec2 mid = (s.A + s.B) / 2;
        if (s.A.X == s.B.X) return (s.A.X <= r.X ? (int)s.A.X - 1 : (int)s.A.X, (int)Math.Floor(mid.Y));
        return ((int)Math.Floor(mid.X), s.A.Y <= r.Y ? (int)s.A.Y - 1 : (int)s.A.Y);
    }

    /// <summary>Platform heights (in wall heights) used by the generator. All within a jump of the floor below.</summary>
    public const double LedgeHeight = 0.4, StepUp = 0.2;

    /// <summary>
    /// Adds the vertical parts: balconies along a room's wall you jump up onto, stepped towers to climb
    /// with a prize on top, and raised corners in boss arenas. Returns the prize spot for each room that got one.
    /// Every edge is no higher than a jump from the floor next to it, so nothing ever blocks the way through.
    /// </summary>
    private static Dictionary<int, Vec2> AddPlatforms(GeneratedLevel level, bool[,] floor, List<Room> rooms, List<int> arenas, int finish, Random rng)
    {
        var prizes = new Dictionary<int, Vec2>();
        bool AllFloor(int x, int y, int w, int h)
        {
            for (int i = x; i < x + w; i++)
                for (int j = y; j < y + h; j++)
                    if (i < 0 || j < 0 || i >= floor.GetLength(0) || j >= floor.GetLength(1) || !floor[i, j]) return false;
            return true;
        }
        void Box(double x, double y, double w, double h, double height) => level.Platforms.Add(new PlatformSpawn(new[]
        {
            new Vec2(x, y), new Vec2(x + w, y), new Vec2(x + w, y + h), new Vec2(x, y + h),
        }, height));

        for (int i = 1; i < finish; i++)
        {
            Room r = rooms[i];
            if (arenas.Contains(i))
            {
                // Raised corners: high ground for you (or cover from the boss).
                if (r.W < 8 || r.H < 7) continue;
                foreach ((int cx, int cy) in new[] { (r.X + 1, r.Y + 1), (r.X + r.W - 3, r.Y + r.H - 3) })
                    if (AllFloor(cx, cy, 2, 2)) Box(cx, cy, 2, 2, LedgeHeight);
                continue;
            }

            int roll = rng.Next(10);
            if (roll < 4 && r.W >= 6 && r.H >= 4)
            {
                // A balcony along the far wall: jump up to grab what's on it.
                bool north = rng.Next(2) == 0;
                int depth = r.H >= 7 ? 2 : 1;
                int y = north ? r.Y + r.H - depth : r.Y;
                int x0 = r.X + 1, len = r.W - 2;
                if (!AllFloor(x0, y, len, depth)) continue;
                Box(x0, y, len, depth, LedgeHeight);
                prizes[i] = new Vec2(x0 + len - 0.5 - rng.Next(len / 2), y + depth / 2.0);
            }
            else if (roll < 8 && r.W >= 7 && r.H >= 5)
            {
                // A stepped tower: three jumps up, prize on top.
                bool flip = rng.Next(2) == 0;
                int x = r.X + 1 + rng.Next(Math.Max(1, r.W - 6)), y = r.Y + 1 + rng.Next(Math.Max(1, r.H - 4));
                if (!AllFloor(x, y, 4, 2)) continue;
                double topX = flip ? x : x + 2, s1 = flip ? x + 3 : x, s2 = flip ? x + 2 : x + 1;
                Box(s1, y, 1, 1, StepUp);
                Box(s2, y, 1, 1, StepUp * 2);
                Box(topX, y, 2, 2, StepUp * 3);
                prizes[i] = new Vec2(topX + 1, y + 1);
            }
        }
        return prizes;
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
