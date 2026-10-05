namespace CivDoom.Engine;

/// <summary>A vertical, full-height wall standing on the segment A-B.</summary>
public sealed class Wall
{
    public Wall(Vec2 a, Vec2 b, int color, bool isGate = false)
    {
        IsGate = isGate;
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

    /// <summary>A locked gate: solid until the level's unlock rule is met, then it opens.</summary>
    public bool IsGate { get; }

    /// <summary>A locked door: solid until you walk up to it holding this keycard.</summary>
    public KeyColor Key { get; init; }

    public bool IsDoor => Key != KeyColor.None;

    /// <summary>For platform edges: the height of the platform top (0 for ordinary walls).</summary>
    public double LedgeHeight { get; init; }

    /// <summary>An open gate no longer blocks movement, shots or sight.</summary>
    public bool IsOpen { get; set; }

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
