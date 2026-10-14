namespace PavementBuildup.Core;

public static class EdgeProfile
{
    public static double XAt(IReadOnlyList<Pt> profile, double y)
    {
        if (profile.Count == 0) return 0;
        if (y >= profile[0].Y) return profile[0].X;
        for (int i = 0; i < profile.Count - 1; i++)
        {
            var a = profile[i];
            var b = profile[i + 1];
            double hi = Math.Max(a.Y, b.Y), lo = Math.Min(a.Y, b.Y);
            if (y > hi || y < lo) continue;
            if (Math.Abs(a.Y - b.Y) < 1e-12) return a.X;
            return a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y);
        }
        return profile[^1].X;
    }

    public static List<Pt> Clip(IReadOnlyList<Pt> profile, double top, double bottom)
    {
        var result = new List<Pt> { new(XAt(profile, top), top) };
        result.AddRange(profile.Where(p => p.Y < top && p.Y > bottom));
        result.Add(new Pt(XAt(profile, bottom), bottom));
        return result;
    }

    public static List<Pt> Band(IReadOnlyList<Pt> left, IReadOnlyList<Pt> right, double top, double bottom)
    {
        var loop = Clip(left, top, bottom);
        var rightSide = Clip(right, top, bottom);
        rightSide.Reverse();
        loop.AddRange(rightSide);
        return loop;
    }
}
