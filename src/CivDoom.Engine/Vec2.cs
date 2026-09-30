namespace CivDoom.Engine;

/// <summary>2D vector in world units. +X east, +Y north (same as an AutoCAD plan).</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    public Vec2 Normalized()
    {
        double len = Length;
        return len < 1e-12 ? Zero : new Vec2(X / len, Y / len);
    }

    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

    /// <summary>Z component of the 3D cross product.</summary>
    public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

    public static Vec2 FromAngle(double radians) => new(Math.Cos(radians), Math.Sin(radians));

    /// <summary>Rotates 90 degrees clockwise (the "right hand" side when facing this direction).</summary>
    public Vec2 PerpRight() => new(Y, -X);

    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
}
