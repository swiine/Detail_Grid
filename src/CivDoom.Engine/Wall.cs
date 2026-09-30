namespace CivDoom.Engine;

/// <summary>A vertical, full-height wall standing on the segment A-B.</summary>
public sealed class Wall
{
    public Wall(Vec2 a, Vec2 b, int color)
    {
        A = a;
        B = b;
        Color = color & 0xFFFFFF;
        Vec2 d = b - a;
        Length = d.Length;
        Normal = d.Normalized().PerpRight();
    }

    public Vec2 A { get; }
    public Vec2 B { get; }

    /// <summary>0xRRGGBB base color (usually taken from the drawing entity/layer color).</summary>
    public int Color { get; }

    public double Length { get; }

    /// <summary>Index assigned by <see cref="SpatialIndex"/>.</summary>
    internal int Id { get; set; }
    public Vec2 Normal { get; }

    /// <summary>Closest point on the segment to <paramref name="p"/>.</summary>
    public Vec2 ClosestPoint(Vec2 p)
    {
        Vec2 d = B - A;
        double len2 = d.LengthSquared;
        if (len2 < 1e-18) return A;
        double t = Math.Clamp(Vec2.Dot(p - A, d) / len2, 0, 1);
        return A + d * t;
    }
}
