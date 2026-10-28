namespace ETWalls.Core.Drawing;

/// <summary>
/// The hatch patterns as repeating tiles on paper, shared by the app window (GDI+) and the report (SVG).
/// A tile is Size x Size in pattern units; Unit = paper mm per pattern unit; Angle rotates it (degrees, anticlockwise).
/// EARTH is AutoCAD's definition (TTW.json soils rule: scale 0.5, angle 45). Hatch scales follow the TTW pavement tool:
/// the standard's scale is on paper (acadiso mm pattern x 0.5), so one EARTH repeat is 6.35 mm on paper at any drawing
/// scale, as in the plotted details. AR-CONC, GRAVEL and the steel hatch are look-alikes at a fixed paper size.
/// </summary>
public sealed record PatternTile(double Size, double Unit, double Angle, IReadOnlyList<(Pt A, Pt B)> Lines, IReadOnlyList<(Pt C, double R)> Dots, IReadOnlyList<Pt[]> Outlines, double Pen);

public static class Patterns
{
    /// <summary>acad.pat EARTH (y up): three 0-degree and three 90-degree dash families, dash .25 / gap .25, repeat .5.</summary>
    public static PatternTile Earth(int drawScale)
    {
        double unit = Cad.EarthScale * 25.4;   // acadiso EARTH (12.7 mm repeat) x 0.5 = 6.35 mm on paper
        var lines = new List<(Pt, Pt)>();
        static Pt P(double x, double y) => new(x, 0.5 - y);   // pattern space is y-up; paper is y-down
        foreach (var y in new[] { 0, 0.09375, 0.1875 })
        {
            lines.Add((P(0, y), P(0.25, y)));
            lines.Add((P(0.25, y + 0.25), P(0.5, y + 0.25)));
        }
        foreach (var x in new[] { 0.03125, 0.125, 0.21875 })
        {
            lines.Add((P(x, 0.21875), P(x, 0.46875)));
            double x2 = x + 0.25;
            lines.Add((P(x2, 0.46875), P(x2, 0.5)));
            lines.Add((P(x2, 0), P(x2, 0.21875)));
        }
        return new PatternTile(0.5, unit, Cad.EarthAngle, lines, Array.Empty<(Pt, double)>(), Array.Empty<Pt[]>(), Cad.PenHatch);
    }

    public static readonly PatternTile Concrete = new(9, 1, 0, Array.Empty<(Pt, Pt)>(),
        new (Pt, double)[] { (new(1.2, 1.5), 0.18), (new(5.6, 0.9), 0.14), (new(3.4, 4.2), 0.2), (new(7.7, 5.1), 0.15), (new(1.8, 7.4), 0.16), (new(6.2, 7.9), 0.2), (new(8.4, 2.6), 0.12), (new(4.6, 6.3), 0.12) },
        new[] { Tri(2.6, 2.2, 0.9, -0.2, -0.4, 0.8), Tri(6.4, 3.6, 0.7, 0.5, -0.9, 0.3), Tri(3.9, 8.1, 0.8, -0.5, 0.1, 0.8), Tri(0.4, 5, 0.7, 0.1, -0.4, 0.6) }, Cad.PenHatch);

    public static readonly PatternTile Gravel = new(7, 1, 0, Array.Empty<(Pt, Pt)>(), Array.Empty<(Pt, double)>(),
        new[]
        {
            Rel(0.6, 0.8, (1.4, -0.3), (0.6, 1), (-0.9, 0.9), (-1.2, -0.4)),
            Rel(4, 1.2, (1.5, 0.2), (0.3, 1.2), (-1.3, 0.5), (-0.8, -0.8)),
            Rel(1.5, 4, (1.2, -0.2), (0.7, 1.1), (-1, 0.9), (-1.2, -0.6)),
            Rel(4.6, 4.6, (1.3, 0.1), (0.4, 1), (-0.9, 0.8), (-1.1, -0.5)),
        }, Cad.PenHatch);

    public static readonly PatternTile Steel = new(1.6, 1, 45, new[] { (new Pt(0, 0), new Pt(0, 1.6)) }, Array.Empty<(Pt, double)>(), Array.Empty<Pt[]>(), Cad.PenHatch);

    static Pt[] Tri(double x, double y, double dx1, double dy1, double dx2, double dy2) =>
        new[] { new Pt(x, y), new Pt(x + dx1, y + dy1), new Pt(x + dx1 + dx2, y + dy1 + dy2) };

    static Pt[] Rel(double x, double y, params (double dx, double dy)[] steps)
    {
        var pts = new List<Pt> { new(x, y) };
        foreach (var (dx, dy) in steps) { x += dx; y += dy; pts.Add(new(x, y)); }
        return pts.ToArray();
    }

    public static PatternTile? For(Fill fill, int drawScale) => fill switch
    {
        Fill.Earth => Earth(drawScale),
        Fill.Concrete => Concrete,
        Fill.Gravel => Gravel,
        Fill.Steel => Steel,
        _ => null,
    };
}
