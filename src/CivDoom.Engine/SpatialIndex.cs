namespace CivDoom.Engine;

/// <summary>Result of a ray cast against the level walls.</summary>
public readonly record struct RayHit(Wall Wall, double Distance, double U)
{
    /// <summary>Position along the wall in world units (used for texturing).</summary>
    public double WallOffset => U * Wall.Length;
}

/// <summary>
/// Uniform grid over the wall segments. Real drawings can contain tens of thousands of
/// segments, so every ray cast and collision query goes through this instead of a brute-force loop.
/// </summary>
public sealed class SpatialIndex
{
    private const int MaxCellsPerAxis = 512;

    private readonly List<Wall>?[] _cells;
    private readonly int[] _stamp;
    private int _stampId;

    public SpatialIndex(IReadOnlyList<Wall> walls, double preferredCellSize = 1.0)
    {
        Walls = walls;
        if (walls.Count == 0)
        {
            Min = new Vec2(-1, -1);
            Max = new Vec2(1, 1);
        }
        else
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (Wall w in walls)
            {
                minX = Math.Min(minX, Math.Min(w.A.X, w.B.X));
                minY = Math.Min(minY, Math.Min(w.A.Y, w.B.Y));
                maxX = Math.Max(maxX, Math.Max(w.A.X, w.B.X));
                maxY = Math.Max(maxY, Math.Max(w.A.Y, w.B.Y));
            }
            // Pad so segments on the boundary are strictly inside.
            Min = new Vec2(minX - 0.5, minY - 0.5);
            Max = new Vec2(maxX + 0.5, maxY + 0.5);
        }

        double w0 = Max.X - Min.X, h0 = Max.Y - Min.Y;
        CellSize = Math.Max(preferredCellSize, Math.Max(w0, h0) / MaxCellsPerAxis);
        Cols = Math.Max(1, (int)Math.Ceiling(w0 / CellSize));
        Rows = Math.Max(1, (int)Math.Ceiling(h0 / CellSize));

        _cells = new List<Wall>[Cols * Rows];
        _stamp = new int[walls.Count];
        for (int i = 0; i < walls.Count; i++)
        {
            walls[i].Id = i;
            Insert(walls[i]);
        }
    }

    public IReadOnlyList<Wall> Walls { get; }
    public Vec2 Min { get; }
    public Vec2 Max { get; }
    public double CellSize { get; }
    public int Cols { get; }
    public int Rows { get; }

    private void Insert(Wall w)
    {
        int c0 = CellX(Math.Min(w.A.X, w.B.X)), c1 = CellX(Math.Max(w.A.X, w.B.X));
        int r0 = CellY(Math.Min(w.A.Y, w.B.Y)), r1 = CellY(Math.Max(w.A.Y, w.B.Y));
        for (int r = r0; r <= r1; r++)
        {
            for (int c = c0; c <= c1; c++)
            {
                Vec2 cmin = new(Min.X + c * CellSize, Min.Y + r * CellSize);
                if (!SegmentTouchesBox(w.A, w.B, cmin, cmin + new Vec2(CellSize, CellSize))) continue;
                ref List<Wall>? cell = ref _cells[r * Cols + c];
                (cell ??= new List<Wall>()).Add(w);
            }
        }
    }

    private int CellX(double x) => Math.Clamp((int)Math.Floor((x - Min.X) / CellSize), 0, Cols - 1);
    private int CellY(double y) => Math.Clamp((int)Math.Floor((y - Min.Y) / CellSize), 0, Rows - 1);

    private static bool SegmentTouchesBox(Vec2 a, Vec2 b, Vec2 bmin, Vec2 bmax)
    {
        // Slab test with a tiny epsilon so segments lying exactly on cell borders are kept.
        const double eps = 1e-9;
        double t0 = 0, t1 = 1;
        Vec2 d = b - a;
        return Clip(-d.X, a.X - (bmin.X - eps)) && Clip(d.X, (bmax.X + eps) - a.X)
            && Clip(-d.Y, a.Y - (bmin.Y - eps)) && Clip(d.Y, (bmax.Y + eps) - a.Y);

        bool Clip(double p, double q)
        {
            if (Math.Abs(p) < 1e-15) return q >= 0;
            double r = q / p;
            if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }
    }

    /// <summary>Finds the nearest wall hit by the ray <paramref name="origin"/> + t * <paramref name="dir"/>.</summary>
    /// <remarks>Distances are returned in units of <paramref name="dir"/>'s length (not normalized on purpose:
    /// the renderer passes camera-plane rays so the distance is already perpendicular / fisheye-free).</remarks>
    public RayHit? CastRay(Vec2 origin, Vec2 dir, double maxDistance)
    {
        if (Walls.Count == 0 || dir.LengthSquared < 1e-18) return null;
        NextStamp();

        // Clip the ray against the grid bounds.
        double tEnter = 0, tLeave = maxDistance;
        if (!ClipAxis(origin.X, dir.X, Min.X, Max.X, ref tEnter, ref tLeave)) return null;
        if (!ClipAxis(origin.Y, dir.Y, Min.Y, Max.Y, ref tEnter, ref tLeave)) return null;

        Vec2 start = origin + dir * tEnter;
        int cx = CellX(start.X), cy = CellY(start.Y);
        int stepX = dir.X > 0 ? 1 : -1, stepY = dir.Y > 0 ? 1 : -1;

        double tMaxX = double.PositiveInfinity, tMaxY = double.PositiveInfinity;
        double tDeltaX = double.PositiveInfinity, tDeltaY = double.PositiveInfinity;
        if (Math.Abs(dir.X) > 1e-15)
        {
            double boundary = Min.X + (cx + (stepX > 0 ? 1 : 0)) * CellSize;
            tMaxX = (boundary - origin.X) / dir.X;
            tDeltaX = CellSize / Math.Abs(dir.X);
        }
        if (Math.Abs(dir.Y) > 1e-15)
        {
            double boundary = Min.Y + (cy + (stepY > 0 ? 1 : 0)) * CellSize;
            tMaxY = (boundary - origin.Y) / dir.Y;
            tDeltaY = CellSize / Math.Abs(dir.Y);
        }

        Wall? best = null;
        double bestT = maxDistance, bestU = 0;
        while (true)
        {
            List<Wall>? cell = _cells[cy * Cols + cx];
            if (cell != null)
            {
                foreach (Wall w in cell)
                {
                    int id = w.Id;
                    if (_stamp[id] == _stampId) continue;
                    _stamp[id] = _stampId;
                    if (w.IsOpen) continue;
                    if (Intersect(origin, dir, w, out double t, out double u) && t < bestT)
                    {
                        best = w; bestT = t; bestU = u;
                    }
                }
            }

            double cellExit = Math.Min(tMaxX, tMaxY);
            if (best != null && bestT <= cellExit) break;
            if (cellExit > tLeave) break;

            if (tMaxX < tMaxY) { cx += stepX; tMaxX += tDeltaX; }
            else { cy += stepY; tMaxY += tDeltaY; }
            if (cx < 0 || cy < 0 || cx >= Cols || cy >= Rows) break;
        }

        return best == null ? null : new RayHit(best, bestT, bestU);
    }

    /// <summary>
    /// Every segment crossed by the ray within <paramref name="maxDistance"/>, nearest first (used for
    /// platform ledges, which you can see over). <paramref name="results"/> is cleared first.
    /// </summary>
    public void CastAll(Vec2 origin, Vec2 dir, double maxDistance, List<RayHit> results)
    {
        results.Clear();
        if (Walls.Count == 0 || dir.LengthSquared < 1e-18) return;
        NextStamp();
        double tEnter = 0, tLeave = maxDistance;
        if (!ClipAxis(origin.X, dir.X, Min.X, Max.X, ref tEnter, ref tLeave)) return;
        if (!ClipAxis(origin.Y, dir.Y, Min.Y, Max.Y, ref tEnter, ref tLeave)) return;

        Vec2 start = origin + dir * tEnter;
        int cx = CellX(start.X), cy = CellY(start.Y);
        int stepX = dir.X > 0 ? 1 : -1, stepY = dir.Y > 0 ? 1 : -1;
        double tMaxX = double.PositiveInfinity, tMaxY = double.PositiveInfinity;
        double tDeltaX = double.PositiveInfinity, tDeltaY = double.PositiveInfinity;
        if (Math.Abs(dir.X) > 1e-15)
        {
            tMaxX = (Min.X + (cx + (stepX > 0 ? 1 : 0)) * CellSize - origin.X) / dir.X;
            tDeltaX = CellSize / Math.Abs(dir.X);
        }
        if (Math.Abs(dir.Y) > 1e-15)
        {
            tMaxY = (Min.Y + (cy + (stepY > 0 ? 1 : 0)) * CellSize - origin.Y) / dir.Y;
            tDeltaY = CellSize / Math.Abs(dir.Y);
        }

        while (true)
        {
            List<Wall>? cell = _cells[cy * Cols + cx];
            if (cell != null)
            {
                foreach (Wall w in cell)
                {
                    if (_stamp[w.Id] == _stampId) continue;
                    _stamp[w.Id] = _stampId;
                    if (Intersect(origin, dir, w, out double t, out double u) && t < maxDistance) results.Add(new RayHit(w, t, u));
                }
            }
            if (Math.Min(tMaxX, tMaxY) > tLeave) break;
            if (tMaxX < tMaxY) { cx += stepX; tMaxX += tDeltaX; }
            else { cy += stepY; tMaxY += tDeltaY; }
            if (cx < 0 || cy < 0 || cx >= Cols || cy >= Rows) break;
        }
        if (results.Count > 1) results.Sort((a, b) => a.Distance.CompareTo(b.Distance));
    }

    private static bool ClipAxis(double o, double d, double min, double max, ref double t0, ref double t1)
    {
        if (Math.Abs(d) < 1e-15) return o >= min && o <= max;
        double a = (min - o) / d, b = (max - o) / d;
        if (a > b) (a, b) = (b, a);
        t0 = Math.Max(t0, a);
        t1 = Math.Min(t1, b);
        return t0 <= t1;
    }

    /// <summary>Ray/segment intersection. <paramref name="u"/> is the 0..1 position along the wall.</summary>
    public static bool Intersect(Vec2 origin, Vec2 dir, Wall w, out double t, out double u)
    {
        Vec2 e = w.B - w.A;
        double denom = Vec2.Cross(dir, e);
        t = u = 0;
        if (Math.Abs(denom) < 1e-15) return false;
        Vec2 ap = w.A - origin;
        t = Vec2.Cross(ap, e) / denom;
        u = Vec2.Cross(ap, dir) / denom;
        return t > 1e-9 && u >= 0 && u <= 1;
    }

    /// <summary>True when nothing blocks the straight line between two points.</summary>
    public bool HasLineOfSight(Vec2 from, Vec2 to)
    {
        Vec2 d = to - from;
        double len = d.Length;
        if (len < 1e-9) return true;
        return CastRay(from, d / len, len) == null;
    }

    /// <summary>Walls whose grid cells overlap the given box.</summary>
    public List<Wall> Query(Vec2 min, Vec2 max)
    {
        var result = new List<Wall>();
        if (Walls.Count == 0 || max.X < Min.X || max.Y < Min.Y || min.X > Max.X || min.Y > Max.Y) return result;
        NextStamp();
        int c0 = CellX(min.X), c1 = CellX(max.X), r0 = CellY(min.Y), r1 = CellY(max.Y);
        for (int r = r0; r <= r1; r++)
        {
            for (int c = c0; c <= c1; c++)
            {
                List<Wall>? cell = _cells[r * Cols + c];
                if (cell == null) continue;
                foreach (Wall w in cell)
                {
                    if (_stamp[w.Id] == _stampId) continue;
                    _stamp[w.Id] = _stampId;
                    if (!w.IsOpen) result.Add(w);
                }
            }
        }
        return result;
    }

    private void NextStamp()
    {
        _stampId++;
        if (_stampId == int.MaxValue) { Array.Clear(_stamp); _stampId = 1; }
    }

    /// <summary>True when the segment p-q crosses any wall (used to stop tunnelling).</summary>
    public bool SegmentCrossesWall(Vec2 p, Vec2 q)
    {
        Vec2 d = q - p;
        if (d.LengthSquared < 1e-18) return false;
        Vec2 min = new(Math.Min(p.X, q.X), Math.Min(p.Y, q.Y));
        Vec2 max = new(Math.Max(p.X, q.X), Math.Max(p.Y, q.Y));
        foreach (Wall w in Query(min, max))
        {
            if (Intersect(p, d, w, out double t, out _) && t <= 1) return true;
        }
        return false;
    }
}
