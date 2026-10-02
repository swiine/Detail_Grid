namespace CivDoom.Engine;

/// <summary>
/// A walkability grid over the level plus a flow field towards one target (the player). Monsters that
/// can't see you follow the flow downhill, which routes them around walls and through doorways instead
/// of pressing against the nearest wall. One breadth-first search serves every monster.
/// </summary>
public sealed class NavGrid
{
    private const int MaxCellsPerAxis = 300;
    private const double Clearance = 0.13;

    private static readonly (int Dx, int Dy)[] Steps = { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };

    private readonly SpatialIndex _index;
    private bool[] _walkable = Array.Empty<bool>();
    private byte[] _links = Array.Empty<byte>(); // bit k set = can step in direction Steps[k]
    private int[] _distance = Array.Empty<int>();
    private (int, int) _target = (-1, -1);

    public NavGrid(SpatialIndex index, double preferredCellSize = 0.3)
    {
        _index = index;
        Min = index.Min - new Vec2(1, 1);
        Vec2 max = index.Max + new Vec2(1, 1);
        CellSize = Math.Max(preferredCellSize, Math.Max(max.X - Min.X, max.Y - Min.Y) / MaxCellsPerAxis);
        Cols = Math.Max(1, (int)Math.Ceiling((max.X - Min.X) / CellSize));
        Rows = Math.Max(1, (int)Math.Ceiling((max.Y - Min.Y) / CellSize));
        Rebuild();
    }

    public Vec2 Min { get; }
    public double CellSize { get; }
    public int Cols { get; }
    public int Rows { get; }

    /// <summary>Re-checks walkability, e.g. after gates open.</summary>
    public void Rebuild()
    {
        int n = Cols * Rows;
        _walkable = new bool[n];
        _links = new byte[n];
        _distance = new int[n];
        _target = (-1, -1);
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
                _walkable[y * Cols + x] = Reachability.HasClearance(_index, Center(x, y), Clearance);

        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
            {
                if (!_walkable[y * Cols + x]) continue;
                byte mask = 0;
                for (int k = 0; k < Steps.Length; k++)
                {
                    (int dx, int dy) = Steps[k];
                    int nx = x + dx, ny = y + dy;
                    if (!Walkable(nx, ny)) continue;
                    // Diagonals only where both straight neighbours are open (no corner cutting).
                    if (dx != 0 && dy != 0 && (!Walkable(x + dx, y) || !Walkable(x, y + dy))) continue;
                    if (_index.SegmentCrossesWall(Center(x, y), Center(nx, ny))) continue;
                    mask |= (byte)(1 << k);
                }
                _links[y * Cols + x] = mask;
            }
    }

    public Vec2 Center(int x, int y) => Min + new Vec2((x + 0.5) * CellSize, (y + 0.5) * CellSize);

    public (int X, int Y) Cell(Vec2 p) =>
        (Math.Clamp((int)Math.Floor((p.X - Min.X) / CellSize), 0, Cols - 1), Math.Clamp((int)Math.Floor((p.Y - Min.Y) / CellSize), 0, Rows - 1));

    private bool Walkable(int x, int y) => x >= 0 && y >= 0 && x < Cols && y < Rows && _walkable[y * Cols + x];

    /// <summary>Recomputes distances to <paramref name="target"/> (cheap to call: skips work if the cell hasn't changed).</summary>
    public void SetTarget(Vec2 target)
    {
        (int tx, int ty) = NearestWalkable(Cell(target));
        if ((tx, ty) == _target) return;
        _target = (tx, ty);
        Array.Fill(_distance, int.MaxValue);
        if (!Walkable(tx, ty)) return;

        var queue = new Queue<int>();
        _distance[ty * Cols + tx] = 0;
        queue.Enqueue(ty * Cols + tx);
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % Cols, y = i / Cols;
            byte mask = _links[i];
            for (int k = 0; k < Steps.Length; k++)
            {
                if ((mask & (1 << k)) == 0) continue;
                int j = (y + Steps[k].Dy) * Cols + x + Steps[k].Dx;
                if (_distance[j] != int.MaxValue) continue;
                _distance[j] = _distance[i] + 1;
                queue.Enqueue(j);
            }
        }
    }

    private (int, int) NearestWalkable((int X, int Y) c)
    {
        if (Walkable(c.X, c.Y)) return c;
        for (int r = 1; r < 4; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (Walkable(c.X + dx, c.Y + dy)) return (c.X + dx, c.Y + dy);
        return c;
    }

    /// <summary>Steps from the target to <paramref name="from"/>, or null if there's no path.</summary>
    public int? DistanceFrom(Vec2 from)
    {
        (int x, int y) = NearestWalkable(Cell(from));
        int d = Walkable(x, y) ? _distance[y * Cols + x] : int.MaxValue;
        return d == int.MaxValue ? null : d;
    }

    /// <summary>The next point to walk to from <paramref name="from"/> on the way to the target, or null if unreachable.</summary>
    public Vec2? NextWaypoint(Vec2 from)
    {
        (int x, int y) = NearestWalkable(Cell(from));
        if (!Walkable(x, y)) return null;
        int i = y * Cols + x;
        int best = _distance[i];
        if (best == int.MaxValue) return null;
        if (best == 0) return Center(x, y);

        // Look a couple of steps ahead for a smoother path.
        int cx = x, cy = y;
        for (int hop = 0; hop < 2; hop++)
        {
            int ci = cy * Cols + cx, bestK = -1, bestD = _distance[ci];
            byte mask = _links[ci];
            for (int k = 0; k < Steps.Length; k++)
            {
                if ((mask & (1 << k)) == 0) continue;
                int j = (cy + Steps[k].Dy) * Cols + cx + Steps[k].Dx;
                if (_distance[j] < bestD) { bestD = _distance[j]; bestK = k; }
            }
            if (bestK < 0) break;
            cx += Steps[bestK].Dx;
            cy += Steps[bestK].Dy;
            if (bestD == 0) break;
        }
        return Center(cx, cy);
    }
}
