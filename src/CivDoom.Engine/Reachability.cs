namespace CivDoom.Engine;

/// <summary>
/// Flood fills a coarse grid from the player start to find floor space the player can actually walk to.
/// Used to auto-place enemies and pickups in drawings that don't mark them explicitly.
/// </summary>
public sealed class Reachability
{
    private const int MaxCellsPerAxis = 400;

    private readonly bool[] _reachable;

    public Reachability(SpatialIndex index, Vec2 start, double clearance, double preferredCellSize = 0.25)
    {
        // Explore one cell of margin around the walls so open drawings stay bounded.
        Min = index.Min - new Vec2(1, 1);
        Vec2 max = index.Max + new Vec2(1, 1);
        CellSize = Math.Max(preferredCellSize, Math.Max(max.X - Min.X, max.Y - Min.Y) / MaxCellsPerAxis);
        Cols = Math.Max(1, (int)Math.Ceiling((max.X - Min.X) / CellSize));
        Rows = Math.Max(1, (int)Math.Ceiling((max.Y - Min.Y) / CellSize));
        _reachable = new bool[Cols * Rows];

        int sx = (int)Math.Floor((start.X - Min.X) / CellSize);
        int sy = (int)Math.Floor((start.Y - Min.Y) / CellSize);
        if (sx < 0 || sy < 0 || sx >= Cols || sy >= Rows) return;

        var queue = new Queue<(int X, int Y)>();
        _reachable[sy * Cols + sx] = true;
        queue.Enqueue((sx, sy));
        Span<(int, int)> steps = stackalloc (int, int)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            Vec2 from = CellCenter(x, y);
            foreach ((int dx, int dy) in steps)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= Cols || ny >= Rows) continue;
                int idx = ny * Cols + nx;
                if (_reachable[idx]) continue;
                Vec2 to = CellCenter(nx, ny);
                if (index.SegmentCrossesWall(from, to)) continue;
                if (!HasClearance(index, to, clearance)) continue;
                _reachable[idx] = true;
                queue.Enqueue((nx, ny));
            }
        }
    }

    public Vec2 Min { get; }
    public double CellSize { get; }
    public int Cols { get; }
    public int Rows { get; }

    public Vec2 CellCenter(int x, int y) => Min + new Vec2((x + 0.5) * CellSize, (y + 0.5) * CellSize);

    public bool IsReachable(Vec2 p)
    {
        int x = (int)Math.Floor((p.X - Min.X) / CellSize);
        int y = (int)Math.Floor((p.Y - Min.Y) / CellSize);
        return x >= 0 && y >= 0 && x < Cols && y < Rows && _reachable[y * Cols + x];
    }

    public IEnumerable<Vec2> ReachableCells()
    {
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
                if (_reachable[y * Cols + x]) yield return CellCenter(x, y);
    }

    public int ReachableCount => _reachable.Count(r => r);

    public static bool HasClearance(SpatialIndex index, Vec2 p, double radius)
    {
        Vec2 r = new(radius, radius);
        foreach (Wall w in index.Query(p - r, p + r))
        {
            if ((w.ClosestPoint(p) - p).LengthSquared < radius * radius) return false;
        }
        return true;
    }
}
