using System.Globalization;
using ETWalls.Core.Design;

namespace ETWalls.Core.Drawing;

/// <summary>What a drawing item is, i.e. which layer of the CAD standard (TTW.json "Element") it goes on.</summary>
public enum Layer { Outline, Hatch, Reinforcement, Text, Dimension, Leader, Title }

/// <summary>How a closed shape is filled. Hatched shapes cover (mask) whatever is under them first, except soil.</summary>
public enum Fill { None, White, Ink, Earth, Concrete, Gravel, Steel }

public enum Anchor { Start, Middle, End }

public readonly record struct Pt(double X, double Y);

public abstract record Item(Layer Layer);

/// <summary>A polyline (open) or polygon (closed). Stroke 0 = no outline. Width is the plotted pen in paper mm.</summary>
public sealed record Shape(Pt[] Points, bool Closed, Fill Fill, double Stroke, bool Dashed, Layer Layer) : Item(Layer);

public sealed record Circle(Pt Centre, double Radius, Fill Fill, double Stroke, Layer Layer) : Item(Layer);

/// <summary>Text at a baseline point; Height is the cap height in paper mm (CAD text height). Rotation in degrees, anticlockwise on paper.</summary>
public sealed record Label(Pt At, string Text, double Height, Anchor Anchor, double Rotation, bool Bold, bool Underline, Layer Layer) : Item(Layer);

/// <summary>
/// The wall section to the CAD standard (TTW.json), drawn on "paper" in mm at a true scale, like a plotted detail:
/// y points down (as on screen), 1 m of wall = 1000 / Scale mm. One drawing for the app window, the report (SVG)
/// and the DXF / Civil 3D (converted to model metres there). A port of section.js in the preview page.
/// </summary>
public sealed class SectionDrawing
{
    public int Scale { get; init; }
    public List<Item> Items { get; } = new();
    /// <summary>Paper area to show: x, y, width, height (mm).</summary>
    public (double X, double Y, double W, double H) View { get; set; }
    public bool Mirrored { get; init; }
    /// <summary>Paper mm per metre of wall.</summary>
    public double K => 1000.0 / Scale;
}

/// <summary>Pens, text sizes and wording from the CAD standard (TTW.json) used by the section.</summary>
public static class Cad
{
    public const double PenOutline = 0.25, PenHatch = 0.13, PenReo = 0.35, PenThin = 0.18, PenGround = 0.35;
    public const double Text = 2.5, Title = 5, DimText = 2.5, Arrow = 2, BreakSize = 5;
    public const string SubgradeNote = "FOUNDATION COMPACTED TO 98% SMDD AT ±2% OMC";
    /// <summary>Standard bar format {prefix}{diameter} @ {spacing} c/c; prefix N for AS/NZS 4671 500N bars.</summary>
    public static string BarFormat(int db, int s) => $"N{db} @ {s} c/c";
    public static string ScaleFormat(int n) => $"SCALE 1:{n}";
    /// <summary>TTW.json soils rule: EARTH, scale 0.5, angle 45 (model units = metres).</summary>
    public const double EarthScale = 0.5, EarthAngle = 45;
    public static readonly int[] Scales = { 10, 20, 25, 50, 100 };
    public static readonly IReadOnlyDictionary<string, double> UcDepth = new Dictionary<string, double>
    {
        ["150UC23"] = 0.152, ["200UC46"] = 0.203, ["250UC73"] = 0.254, ["310UC97"] = 0.308,
    };

    /// <summary>Rough width of upper-case Arial at cap height h (paper mm), for layout.</summary>
    public static double TextWidth(string s, double h) => s.Length * h * 0.8;

    public static List<string> Wrap(string s, int n)
    {
        var lines = new List<string>(); var cur = "";
        foreach (var w in s.Split(' '))
        {
            if ((cur + " " + w).Trim().Length > n && cur.Length > 0) { lines.Add(cur); cur = w; }
            else cur = (cur + " " + w).Trim();
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines;
    }
}

public static class SectionBuilder
{
    static string G(double v) => v.ToString(CultureInfo.InvariantCulture);

    private sealed record Lead(double X, double Y, string Text);

    /// <param name="full">Full: labels on leaders and a title. Compact: bar letters only (for small previews).</param>
    /// <param name="scale">Drawing scale (e.g. 25 for 1:25); 0 picks one that fits.</param>
    public static SectionDrawing Build(WallInput inp, WallResult result, bool full = true, int scale = 0, string? title = null)
    {
        var res = result.R; var reo = res.Reo;
        double tanB = Math.Tan(inp.Slope * Math.PI / 180);

        // ----- geometry in metres (x from the front of the footing, y up from its underside) -----
        var conc = new List<Pt[]>(); var block = new List<Pt[]>(); var steel = new List<Pt[]>(); var pads = new List<Pt[]>();
        var bars = new List<Pt[]>(); var dots = new List<Pt>(); var ticks = new List<Pt[]>();
        double Ht, xFrontFace, groundFront, backTop, footBack, wallTop;
        Pt[]? drain = null; Pt? pipe = null;
        var L = new List<Lead>(); var R = new List<Lead>();
        static Pt P(double x, double y) => new(x, y);

        if (inp.Type == WallType.Cantilever)
        {
            double toe = inp.Toe, t1 = inp.T1, t2 = inp.T2, tb = inp.Tb, heel = inp.HeelUsed, D = inp.D;
            Ht = inp.H + D; double B = toe + t2 + heel, hs = Ht - tb;
            conc.Add(new[] { P(0, 0), P(B, 0), P(B, tb), P(toe + t2, tb), P(toe + t1, Ht), P(toe, Ht), P(toe, tb), P(0, tb) });
            xFrontFace = toe; groundFront = D; backTop = toe + t1; footBack = B; wallTop = Ht;
            double BackAt(double y) => toe + t1 + (t2 - t1) * (Ht - y) / hs;
            if (inp.Drained)
            {
                drain = new[] { P(BackAt(tb), tb), P(BackAt(tb) + 0.3, tb), P(BackAt(Ht - 0.3) + 0.3, Ht - 0.3), P(BackAt(Ht - 0.3), Ht - 0.3) };
                pipe = P(toe + t2 + 0.12, tb + 0.07);
            }
            if (reo is not null)
            {
                double c = inp.Cover / 1000, lap = reo.LapA / 1000;
                double bA(double y) => BackAt(y) - c;
                bars.Add(new[] { P(bA(Ht - c), Ht - c), P(bA(tb + 0.02), tb + 0.02) });                                                      // A
                bars.Add(new[] { P(bA(tb + lap) - 0.02, tb + lap), P(toe + t2 - c - 0.02, c + 0.03), P(Math.Max(c, toe + t2 - c - 0.45), c + 0.03) }); // S
                bars.Add(new[] { P(toe + c, Ht - c), P(toe + c, c + 0.03), P(toe + c + 0.3, c + 0.03) });                                      // B
                bars.Add(new[] { P(c, tb - c - 0.1), P(c, tb - c), P(B - c, tb - c), P(B - c, tb - c - 0.12) });                                // E
                bars.Add(new[] { P(c, c + 0.12), P(c, c), P(B - c, c), P(B - c, c + 0.12) });                                                  // F
                for (double y = tb + 0.15; y < Ht - 0.05; y += 0.3) { dots.Add(P(toe + c + 0.016, y)); dots.Add(P(bA(y) - 0.016, y)); }        // C
                for (double x = c + 0.15; x < B - c - 0.05; x += 0.3) { dots.Add(P(x, tb - c - 0.016)); dots.Add(P(x, c + 0.016)); }          // G
                string Lbl(string m, string extra)
                {
                    var parts = reo.Schedule.First(r => r.Mark == m).Bars.Replace("N", "").Split('@');
                    return $"{Cad.BarFormat(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1].Split(' ')[0], CultureInfo.InvariantCulture))} ({m}) {extra}";
                }
                R.Add(new(bA(tb + hs * 0.7), tb + hs * 0.7, Lbl("A", "VERT. BACK FACE") + (reo.Cut is double cut && cut != 0 ? $", ALT. BARS STOP {Math.Round(cut * 1000)} ABOVE BASE" : "")));
                R.Add(new(bA(tb + lap * 0.6) - 0.02, tb + lap * 0.6, Lbl("S", $"STARTERS, LAP {G(reo.LapA)}")));
                R.Add(new(bA(tb + hs * 0.45) - 0.016, tb + 0.15 + 0.3 * Math.Floor((hs * 0.45 - 0.15) / 0.3 + 0.5), Lbl("C", "HORIZ. EACH FACE")));
                R.Add(new(toe + t2 + heel * 0.6, tb - c, Lbl("E", "TOP")));
                R.Add(new(toe + t2 + heel * 0.35, c + 0.016, Lbl("G", "LONGIT. TOP & BTM")));
                L.Add(new(toe + c, tb + hs * 0.55, Lbl("B", "VERT. FRONT FACE")));
                L.Add(new(toe * 0.5, c, Lbl("F", "BTM")));
                L.Add(new(toe + t1 * 0.5, Ht - 0.12, $"{G(inp.Fc)} MPa CONCRETE, {G(inp.Cover)} COVER"));
            }
        }
        else if (inp.Type == WallType.Post)
        {
            double H = inp.H, Dp = inp.Dp, dia = inp.Dia; Ht = Dp + H; wallTop = Ht;
            double ud = Cad.UcDepth.TryGetValue(inp.Post, out var dd) ? dd : 0.2, xc = dia / 2;
            conc.Add(new[] { P(0, 0), P(dia, 0), P(dia, Dp), P(0, Dp) });
            steel.Add(new[] { P(xc - ud / 2, 0.1), P(xc + ud / 2, 0.1), P(xc + ud / 2, Ht), P(xc - ud / 2, Ht) });
            for (double y = Dp - 0.1; y < Ht - 0.01; y += 0.2)
                pads.Add(new[] { P(xc + ud / 2, y), P(xc + ud / 2 + 0.075, y), P(xc + ud / 2 + 0.075, Math.Min(Ht, y + 0.2)), P(xc + ud / 2, Math.Min(Ht, y + 0.2)) });
            xFrontFace = xc - ud / 2; groundFront = Dp; backTop = xc + ud / 2 + 0.075; footBack = Math.Max(dia, backTop);
            if (inp.Drained) { drain = new[] { P(backTop, Dp), P(backTop + 0.3, Dp), P(backTop + 0.3, Ht - 0.3), P(backTop, Ht - 0.3) }; pipe = P(backTop + 0.15, Dp + 0.07); }
            var n = reo?.Schedule.FirstOrDefault()?.Bars ?? "6-N16";
            foreach (var x in new[] { 0.075, dia - 0.075 }) bars.Add(new[] { P(x, Dp - 0.05), P(x, 0.075) });
            R.Add(new(xc + ud / 2, Dp + H * 0.75, $"{inp.Post} STEEL POST @ {Math.Round(inp.S * 1000)} c/c, HOT-DIP GALVANISED"));
            R.Add(new(backTop - 0.03, Dp + H * 0.4, "75 THICK CONCRETE SLEEPERS BETWEEN POSTS"));
            L.Add(new(0.075, Dp * 0.4, $"Ø{Math.Round(dia * 1000)} CONCRETE PIER, {n} VERT. + R10 HELIX @ 200"));
        }
        else
        {
            double Bg = inp.Bg, Bt = inp.Bt, D = inp.D; Ht = inp.H + D; wallTop = Ht;
            var poly = new[] { P(0, 0), P(Bg, 0), P(Bg, Ht), P(Bg - Bt, Ht) };
            (inp.Type == WallType.Block ? block : conc).Add(poly);
            if (inp.Type == WallType.Block)
            {
                conc.Add(new[] { P(-0.1, -0.15), P(Bg + 0.1, -0.15), P(Bg + 0.1, 0), P(-0.1, 0) });
                for (double y = 0.2; y < Ht - 0.01; y += 0.2) { double xl = (Bg - Bt) * (y / Ht); ticks.Add(new[] { P(xl, y), P(Bg, y) }); }
            }
            xFrontFace = (Bg - Bt) * D / Ht; groundFront = D; backTop = Bg; footBack = Bg;
            if (inp.Drained) { drain = new[] { P(Bg, 0.05), P(Bg + 0.3, 0.05), P(Bg + 0.3, Ht - 0.3), P(Bg, Ht - 0.3) }; pipe = P(Bg + 0.15, 0.12); }
            R.Add(new(Bg - Bt * 0.5, Ht * 0.7, inp.Type == WallType.Block ? "SEGMENTAL RETAINING BLOCKS, CORES FILLED WITH GRAVEL" : "MASS CONCRETE N20"));
            if (inp.Type == WallType.Block) L.Add(new(-0.05, -0.075, "150 THICK CONCRETE LEVELLING PAD"));
        }
        // Soil extents
        double xL = -Math.Max(0.6, Ht * 0.25), xR = footBack + Math.Max(0.9, Ht * 0.4), yBot = -0.45;
        double TopAt(double x) => wallTop + Math.Max(0, x - backTop) * tanB;
        if (drain is not null) R.Add(new((drain[0].X + drain[1].X) / 2, (drain[0].Y + drain[2].Y) * 0.55, "300 WIDE FREE-DRAINING GRAVEL ZONE"));
        if (pipe is Pt pp) R.Add(new(pp.X + 0.05, pp.Y, "Ø100 SLOTTED AG PIPE IN FILTER SOCK"));
        R.Add(new(xR - 0.25, TopAt(xR - 0.25) - Math.Min(0.6, Ht * 0.3), $"COMPACTED SELECT BACKFILL φ' {G(inp.Phi)}°, γ {G(inp.Gamma)} kN/m³"));
        if (inp.Q > 0) R.Add(new(xR - 0.15, TopAt(xR - 0.15), $"SURCHARGE {G(inp.Q)} kPa"));
        L.Add(new(xL + 0.15, groundFront, "FINISHED GROUND LEVEL"));
        if (inp.Type != WallType.Post) L.Add(new(Math.Min(footBack * 0.5, 0.4), yBot * 0.5, $"{Cad.SubgradeNote}, ALLOWABLE BEARING {G(inp.Qa)} kPa"));

        // ----- paper: mm at the chosen scale -----
        double geoW = xR - xL, geoH = TopAt(xR) - yBot;
        if (scale <= 0) scale = Cad.Scales.FirstOrDefault(s => geoW * 1000 / s <= (full ? 175 : 110) && geoH * 1000 / s <= 170, 100);
        double fs = inp.FacesRight ? -1 : 1;   // faces right: mirror the drawing, not the text
        double k = 1000.0 / scale;
        double X(double x) => fs * x * k;
        double Y(double y) => -y * k;
        var d = new SectionDrawing { Scale = scale, Mirrored = fs < 0 };
        double bx0 = double.PositiveInfinity, by0 = double.PositiveInfinity, bx1 = double.NegativeInfinity, by1 = double.NegativeInfinity;
        void Grow(double x, double y) { bx0 = Math.Min(bx0, x); by0 = Math.Min(by0, y); bx1 = Math.Max(bx1, x); by1 = Math.Max(by1, y); }
        Pt[] Paper(IEnumerable<Pt> pts) => pts.Select(p => { var q = new Pt(X(p.X), Y(p.Y)); Grow(q.X, q.Y); return q; }).ToArray();
        void Line(double x1, double y1, double x2, double y2, double w, Layer layer) { Grow(x1, y1); Grow(x2, y2); d.Items.Add(new Shape(new[] { new Pt(x1, y1), new Pt(x2, y2) }, false, Fill.None, w, false, layer)); }
        void Text(double x, double y, string s, double h, Anchor anchor, Layer layer, bool bold = false, bool underline = false)
        {
            double w = Cad.TextWidth(s, h), x0 = anchor == Anchor.End ? x - w : anchor == Anchor.Middle ? x - w / 2 : x;
            Grow(x0, y - h); Grow(x0 + w, y + 0.5);
            d.Items.Add(new Label(new Pt(x, y), s, h, anchor, 0, bold, underline, layer));
        }
        void Tick(double x, double y) => Line(x - 0.7, y + 0.7, x + 0.7, y - 0.7, Cad.PenOutline, Layer.Dimension);
        void Arrow(Pt[] tri, Layer layer) => d.Items.Add(new Shape(tri, true, Fill.Ink, 0, false, layer));

        // Soil (EARTH), front and back, one polygon under the ground profile
        // The outline runs up the wall's front face, so the whole wall sits inside it (a clean hole in the DXF hatch).
        double topFrontX = inp.Type switch { WallType.Cantilever => inp.Toe, WallType.Post => xFrontFace, _ => inp.Bg - inp.Bt };
        var soil = new[] { P(xL, yBot), P(xL, groundFront), P(xFrontFace, groundFront), P(topFrontX, wallTop), P(backTop, wallTop), P(xR, TopAt(xR)), P(xR, yBot) };
        d.Items.Add(new Shape(Paper(soil), true, Fill.Earth, 0, false, Layer.Hatch));
        // Concrete, blocks, sleepers, steel, drainage (each covers the soil under it)
        foreach (var p in conc) d.Items.Add(new Shape(Paper(p), true, Fill.Concrete, Cad.PenOutline, false, Layer.Outline));
        foreach (var p in block) d.Items.Add(new Shape(Paper(p), true, Fill.White, Cad.PenOutline, false, Layer.Outline));
        foreach (var t in ticks) Line(X(t[0].X), Y(t[0].Y), X(t[1].X), Y(t[1].Y), Cad.PenThin, Layer.Outline);
        if (drain is not null) d.Items.Add(new Shape(Paper(drain), true, Fill.Gravel, Cad.PenThin, true, Layer.Outline));
        foreach (var p in pads) d.Items.Add(new Shape(Paper(p), true, Fill.Concrete, Cad.PenThin, false, Layer.Outline));
        foreach (var p in steel) d.Items.Add(new Shape(Paper(p), true, Fill.Steel, Cad.PenOutline, false, Layer.Outline));
        if (pipe is Pt pc) d.Items.Add(new Circle(new Pt(X(pc.X), Y(pc.Y)), Math.Max(0.05 * k, 0.8), Fill.White, Cad.PenOutline, Layer.Outline));
        // Reinforcement
        foreach (var b in bars) d.Items.Add(new Shape(Paper(b), false, Fill.None, Cad.PenReo, false, Layer.Reinforcement));
        foreach (var p in dots) d.Items.Add(new Circle(new Pt(X(p.X), Y(p.Y)), 0.45, Fill.Ink, 0, Layer.Reinforcement));
        // Ground lines (heavier) and break lines at the soil cuts
        d.Items.Add(new Shape(Paper(new[] { P(xL, groundFront), P(xFrontFace, groundFront) }), false, Fill.None, Cad.PenGround, false, Layer.Outline));
        d.Items.Add(new Shape(Paper(new[] { P(backTop, wallTop), P(xR, TopAt(xR)) }), false, Fill.None, Cad.PenGround, false, Layer.Outline));
        void ZBreakV(double x, double ya, double yb)
        {
            double ym = (ya + yb) / 2, s = Cad.BreakSize / 2;
            d.Items.Add(new Shape(new[] { new Pt(x, ya), new Pt(x, ym - s / 2), new Pt(x + s * 0.6, ym - s * 0.15), new Pt(x - s * 0.6, ym + s * 0.15), new Pt(x, ym + s / 2), new Pt(x, yb) }, false, Fill.None, Cad.PenThin, false, Layer.Outline));
        }
        void ZBreakH(double y, double xa, double xb)
        {
            double xm = (xa + xb) / 2, s = Cad.BreakSize / 2;
            d.Items.Add(new Shape(new[] { new Pt(xa, y), new Pt(xm - s / 2, y), new Pt(xm - s * 0.15, y - s * 0.6), new Pt(xm + s * 0.15, y + s * 0.6), new Pt(xm + s / 2, y), new Pt(xb, y) }, false, Fill.None, Cad.PenThin, false, Layer.Outline));
        }
        ZBreakV(X(xL), Y(groundFront), Y(yBot)); ZBreakV(X(xR), Y(TopAt(xR)), Y(yBot)); ZBreakH(Y(yBot), X(xL), X(xR));
        // Surcharge arrows
        if (inp.Q > 0)
            for (int i = 0; i < 3; i++)
            {
                double x = backTop + (i + 0.6) * (xR - backTop) / 3.2, y = Y(TopAt(x));
                Line(X(x), y - 7, X(x), y - 1.2, Cad.PenThin, Layer.Leader);
                Arrow(new[] { new Pt(X(x) - 0.7, y - 2.6), new Pt(X(x), y - 0.6), new Pt(X(x) + 0.7, y - 2.6) }, Layer.Leader);
            }

        var (dx0, dy0, dx1, dy1) = (bx0, by0, bx1, by1);
        // ----- dimensions -----
        void DimH(double xa, double xb, double yObj, double yd, string label)
        {
            double a = X(xa), b = X(xb), yo = Y(yObj), dir = yd > yo ? 1 : -1;
            Line(a, yo + dir * 1, a, yd + dir * 1.5, Cad.PenThin, Layer.Dimension); Line(b, yo + dir * 1, b, yd + dir * 1.5, Cad.PenThin, Layer.Dimension);
            Line(Math.Min(a, b) - 1.5, yd, Math.Max(a, b) + 1.5, yd, Cad.PenThin, Layer.Dimension);
            Tick(a, yd); Tick(b, yd);
            Text((a + b) / 2, yd - 0.9, label, Cad.DimText, Anchor.Middle, Layer.Dimension);
        }
        void DimV(double ya, double yb, double xObj, double xd, string label)
        {
            double a = Y(ya), b = Y(yb), xo = X(xObj), dir = xd > xo ? 1 : -1, ym = (a + b) / 2;
            Grow(xd - 4, ym); Grow(xd + 4, ym);
            Line(xo + dir * 1, a, xd + dir * 1.5, a, Cad.PenThin, Layer.Dimension); Line(xo + dir * 1, b, xd + dir * 1.5, b, Cad.PenThin, Layer.Dimension);
            Line(xd, a + 1.5, xd, b - 1.5, Cad.PenThin, Layer.Dimension);
            Tick(xd, a); Tick(xd, b);
            d.Items.Add(new Label(new Pt(xd - 0.9, ym), label, Cad.DimText, Anchor.Middle, 90, false, false, Layer.Dimension));
        }
        string Mm(double v) => Math.Floor(v * 1000 + 0.5).ToString(CultureInfo.InvariantCulture);
        double yDims1 = dy1 + 7, yDims2 = dy1 + 14, xDimL = fs > 0 ? dx0 - 7 : dx1 + 7;
        if (inp.Type == WallType.Cantilever)
        {
            double toe = inp.Toe, t1 = inp.T1, t2 = inp.T2, tb = inp.Tb, heel = inp.HeelUsed, D = inp.D, H = inp.H, B = toe + t2 + heel;
            DimH(0, toe, 0, yDims1, Mm(toe)); DimH(toe, toe + t2, 0, yDims1, Mm(t2));
            if (heel > 0) DimH(toe + t2, B, 0, yDims1, Mm(heel));
            DimH(0, B, 0, yDims2, Mm(B));
            DimV(D, Ht, xFrontFace, xDimL, Mm(H)); DimV(0, D, 0, xDimL, Mm(D));
            DimH(toe, toe + t1, Ht, Y(Ht) - 6, Mm(t1));
            DimV(0, tb, B, X(B) + fs * 6, Mm(tb));
        }
        else if (inp.Type == WallType.Post)
        {
            DimV(groundFront, Ht, xFrontFace, xDimL, Mm(inp.H)); DimV(0, groundFront, 0, xDimL, Mm(inp.Dp)); DimH(0, inp.Dia, 0, yDims1, "Ø" + Mm(inp.Dia));
        }
        else
        {
            DimH(0, inp.Bg, 0, yDims1, Mm(inp.Bg)); DimH(inp.Bg - inp.Bt, inp.Bg, Ht, Y(Ht) - 6, Mm(inp.Bt));
            DimV(groundFront, Ht, xFrontFace, xDimL, Mm(inp.H)); DimV(0, groundFront, 0, xDimL, Mm(inp.D));
        }

        // ----- leaders and labels (two columns, outside the drawing) -----
        void LeaderCol(List<Lead> items, double side)
        {
            if (items.Count == 0) return;
            double colX = side > 0 ? bx1 + 10 : bx0 - 10, h = Cad.Text, gap = 2.2, maxW = 62;
            var placed = items.Select(it => (it, tx: X(it.X), ty: Y(it.Y), lines: Cad.Wrap(it.Text, Math.Max(14, (int)Math.Floor(maxW / (h * 0.8))))))
                .OrderBy(p => p.ty).ToList();
            double cursor = double.NegativeInfinity;
            foreach (var (it, tx, ty, lines) in placed)
            {
                double hh = lines.Count * h * 1.6;
                double ly = Math.Max(ty - hh / 2 + h, cursor + h + gap);
                cursor = ly + (lines.Count - 1) * h * 1.6;
                double ex = colX - side * 3;   // landing start
                double ang = Math.Atan2(ly - h / 2 - ty, ex - tx), ah = Cad.Arrow, aw = 0.6;
                double ax = tx + Math.Cos(ang) * ah, ay = ty + Math.Sin(ang) * ah;
                Line(tx, ty, ex, ly - h / 2, Cad.PenThin, Layer.Leader);
                Line(ex, ly - h / 2, colX - side * 0.8, ly - h / 2, Cad.PenThin, Layer.Leader);
                Arrow(new[] { new Pt(tx, ty), new Pt(ax - Math.Sin(ang) * aw, ay + Math.Cos(ang) * aw), new Pt(ax + Math.Sin(ang) * aw, ay - Math.Cos(ang) * aw) }, Layer.Leader);
                for (int i = 0; i < lines.Count; i++) Text(colX, ly + i * h * 1.6, lines[i], h, side > 0 ? Anchor.Start : Anchor.End, Layer.Text);
            }
        }
        if (full) { LeaderCol(R, fs); LeaderCol(L, -fs); }
        else
        {
            // compact: bar marks only, small circles next to the bars
            var marks = R.Select(it => (it, side: 1.0)).Concat(L.Select(it => (it, side: -1.0)))
                .Select(m => (m.it, m.side, mark: System.Text.RegularExpressions.Regex.Match(m.it.Text, @"\(([A-Z])\)")))
                .Where(m => m.mark.Success);
            foreach (var (it, side, mark) in marks)
            {
                double cx = X(it.X) + fs * side * 6, cy = Y(it.Y), mx = cx + fs * side * 2.2;
                Line(X(it.X), cy, cx, cy, Cad.PenThin, Layer.Leader);
                Grow(mx - 2.2, cy - 2.2); Grow(mx + 2.2, cy + 2.2);
                d.Items.Add(new Circle(new Pt(mx, cy), 2.2, Fill.White, Cad.PenThin, Layer.Leader));
                Text(mx, cy + 1, mark.Groups[1].Value, 2.1, Anchor.Middle, Layer.Text, bold: true);
            }
        }
        // ----- title -----
        if (full)
        {
            double tx = dx0, ty = by1 + 12;
            string t = (title ?? $"Typical wall section ({inp.WallName})").ToUpperInvariant();
            Text(tx, ty, t, Cad.Title, Anchor.Start, Layer.Title);
            Line(tx, ty + 1.6, tx + Cad.TextWidth(t, Cad.Title), ty + 1.6, Cad.PenReo, Layer.Title);
            Text(tx, ty + 7, Cad.ScaleFormat(scale), Cad.Text, Anchor.Start, Layer.Title);
        }
        const double m = 4;
        d.View = (bx0 - m, by0 - m, bx1 - bx0 + 2 * m, by1 - by0 + 2 * m);
        return d;
    }
}
