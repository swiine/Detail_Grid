namespace CivDoom.Engine;

/// <summary>
/// A raised floor area: a polygon whose top is <see cref="Height"/> above the ground (in wall heights).
/// Its edges are ledges: you can step up small ones, must jump onto taller ones, and can walk off the top.
/// </summary>
public sealed class Platform
{
    public Platform(IReadOnlyList<Vec2> outline, double height, int color = Terrain.DefaultColor)
    {
        if (outline.Count < 3) throw new ArgumentException("A platform needs at least three corners.", nameof(outline));
        Outline = outline;
        Height = height;
        Color = color & 0xFFFFFF;
        Min = new Vec2(outline.Min(p => p.X), outline.Min(p => p.Y));
        Max = new Vec2(outline.Max(p => p.X), outline.Max(p => p.Y));
    }

    public IReadOnlyList<Vec2> Outline { get; }
    public double Height { get; }
    public int Color { get; }
    public Vec2 Min { get; }
    public Vec2 Max { get; }

    public bool Contains(Vec2 p)
    {
        if (p.X < Min.X || p.Y < Min.Y || p.X > Max.X || p.Y > Max.Y) return false;
        bool inside = false;
        for (int i = 0, j = Outline.Count - 1; i < Outline.Count; j = i++)
        {
            Vec2 a = Outline[i], b = Outline[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    /// <summary>Distance from <paramref name="p"/> to the nearest edge of the outline.</summary>
    public double DistanceToEdge(Vec2 p)
    {
        double best = double.PositiveInfinity;
        for (int i = 0, j = Outline.Count - 1; i < Outline.Count; j = i++)
        {
            Vec2 a = Outline[j], d = Outline[i] - a;
            double len2 = d.LengthSquared;
            double t = len2 < 1e-18 ? 0 : Math.Clamp(Vec2.Dot(p - a, d) / len2, 0, 1);
            best = Math.Min(best, (a + d * t - p).LengthSquared);
        }
        return Math.Sqrt(best);
    }

    /// <summary>The platform's top split into triangles (ear clipping), for 3D display.</summary>
    public IReadOnlyList<(Vec2 A, Vec2 B, Vec2 C)> Triangulate()
    {
        var pts = Outline.ToList();
        // Ear clipping wants counter-clockwise order.
        double area = 0;
        for (int i = 0; i < pts.Count; i++) area += Vec2.Cross(pts[i], pts[(i + 1) % pts.Count]);
        if (area < 0) pts.Reverse();

        var tris = new List<(Vec2, Vec2, Vec2)>();
        var idx = Enumerable.Range(0, pts.Count).ToList();
        int guard = 0;
        while (idx.Count > 3 && guard++ < 10_000)
        {
            bool clipped = false;
            for (int k = 0; k < idx.Count; k++)
            {
                Vec2 a = pts[idx[(k - 1 + idx.Count) % idx.Count]], b = pts[idx[k]], c = pts[idx[(k + 1) % idx.Count]];
                if (Vec2.Cross(b - a, c - b) <= 1e-12) continue; // reflex or flat corner
                bool empty = true;
                foreach (int m in idx)
                {
                    Vec2 p = pts[m];
                    if (p == a || p == b || p == c) continue;
                    if (Vec2.Cross(b - a, p - a) >= 0 && Vec2.Cross(c - b, p - b) >= 0 && Vec2.Cross(a - c, p - c) >= 0) { empty = false; break; }
                }
                if (!empty) continue;
                tris.Add((a, b, c));
                idx.RemoveAt(k);
                clipped = true;
                break;
            }
            if (!clipped) break; // degenerate outline: give up on the rest
        }
        if (idx.Count == 3) tris.Add((pts[idx[0]], pts[idx[1]], pts[idx[2]]));
        return tris;
    }
}

/// <summary>Floor heights and ledges for a level.</summary>
public sealed class Terrain
{
    /// <summary>Ledges no taller than this are stepped up automatically (stairs).</summary>
    public const double StepHeight = 0.16;

    /// <summary>How high a jump lifts your feet (see <see cref="Game.JumpSpeed"/> and <see cref="Game.Gravity"/>).</summary>
    public const double JumpHeight = Game.JumpSpeed * Game.JumpSpeed / (2 * Game.Gravity);

    /// <summary>Concrete grey, for platforms that don't say otherwise.</summary>
    public const int DefaultColor = 0x7A7F8A;

    public static Terrain From(IEnumerable<PlatformSpawn> platforms) =>
        new(platforms.Where(p => p.Outline.Count >= 3 && p.Height > 1e-6).Select(p => new Platform(p.Outline, p.Height, p.Color)).ToList());

    public Terrain(IReadOnlyList<Platform> platforms)
    {
        Platforms = platforms;
        var ledges = new List<Wall>();
        foreach (Platform p in platforms)
            for (int i = 0; i < p.Outline.Count; i++)
                ledges.Add(new Wall(p.Outline[i], p.Outline[(i + 1) % p.Outline.Count], p.Color) { LedgeHeight = p.Height });
        Ledges = new SpatialIndex(ledges);
        MaxHeight = platforms.Count == 0 ? 0 : platforms.Max(p => p.Height);
    }

    public static readonly Terrain Flat = new(Array.Empty<Platform>());

    public IReadOnlyList<Platform> Platforms { get; }

    /// <summary>Every platform edge, as walls carrying <see cref="Wall.LedgeHeight"/>.</summary>
    public SpatialIndex Ledges { get; }

    public double MaxHeight { get; }

    public bool IsFlat => Platforms.Count == 0;

    /// <summary>Height of the floor at <paramref name="p"/>: the highest platform containing it, or 0.</summary>
    public double FloorAt(Vec2 p)
    {
        double h = 0;
        foreach (Platform pl in Platforms)
            if (pl.Height > h && pl.Contains(p)) h = pl.Height;
        return h;
    }

    /// <summary>The platform whose top you're on at <paramref name="p"/>, if any.</summary>
    public Platform? PlatformAt(Vec2 p)
    {
        Platform? best = null;
        foreach (Platform pl in Platforms)
            if ((best == null || pl.Height > best.Height) && pl.Contains(p)) best = pl;
        return best;
    }
}
