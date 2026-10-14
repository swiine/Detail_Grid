namespace PavementBuildup.Core.Design;

public enum JointSetOut { LongestEdge, DrawingAxes, FromCorner }

public sealed record SlabJointInputs
{
    public required IReadOnlyList<Pt> Boundary { get; init; }
    public IReadOnlyList<IReadOnlyList<Pt>> Holes { get; init; } = Array.Empty<IReadOnlyList<Pt>>();
    public double UnitsPerMetre { get; init; } = 1;
    public double ThicknessMm { get; init; } = 200;
    public SlabKind Kind { get; init; } = SlabKind.Unreinforced;
    public JointRuleSet? Rules { get; init; }
    public double MaxSpacingM { get; init; }
    public double? AngleRad { get; init; }
    public JointSetOut SetOut { get; init; } = JointSetOut.LongestEdge;
    public Pt? Corner { get; init; }
    public bool ExactSteps { get; init; }
}

public sealed record JointSegment(Pt A, Pt B);

public sealed record SlabJointResult(
    IReadOnlyList<JointSegment> Joints,
    IReadOnlyList<Pt> CheckPoints,
    IReadOnlyList<string> Warnings,
    double SpacingM,
    double AngleRad,
    int Panels,
    double SawcutLengthM,
    double PerimeterM,
    double AreaM2,
    double LargestPanelM,
    double WorstAspect,
    int SawcutDepthMm,
    int CornersJointed,
    Pt? SetOutCorner = null);

public static class SlabJoints
{
    private const double Eps = 1e-7;

    public static SlabJointResult Layout(SlabJointInputs input)
    {
        if (input.Boundary.Count < 3) throw new ArgumentException("The slab boundary needs at least 3 corners.");
        if (!(input.UnitsPerMetre > 0)) throw new ArgumentException("Unknown drawing units.");
        if (!(input.ThicknessMm > 0)) throw new ArgumentException("Enter the slab thickness (mm).");
        if (input.SetOut == JointSetOut.FromCorner && input.Corner is null)
        {
            SlabJointResult? best = null;
            foreach (var corner in Corners(input.Boundary, input.UnitsPerMetre))
            {
                var r = Layout(input with { Corner = corner });
                if (r.SetOutCorner is null) continue;
                if (best is null || r.CheckPoints.Count < best.CheckPoints.Count
                    || (r.CheckPoints.Count == best.CheckPoints.Count && r.Panels < best.Panels)) best = r;
            }
            if (best is not null) return best;
        }

        double u = input.UnitsPerMetre;
        var outer = Clean(input.Boundary.Select(p => new Pt(p.X / u, p.Y / u)).ToList());
        if (outer.Count < 3 || Math.Abs(Area(outer)) < 1e-6) throw new ArgumentException("The slab boundary has no area.");
        if (Area(outer) < 0) outer.Reverse();
        var holesM = input.Holes.Select(h => Clean(h.Select(p => new Pt(p.X / u, p.Y / u)).ToList()))
            .Where(h => h.Count >= 3 && Math.Abs(Area(h)) > 1e-6 && Inside(outer, Centroid(h))).ToList();

        var set = input.Rules ?? new JointRuleSet();
        var spacing = JointLayout.SpacingLimit(new JointInputs { ThicknessMm = input.ThicknessMm, Kind = input.Kind, MaxSpacingM = input.MaxSpacingM, Rules = set });
        if (!(spacing >= 0.5)) throw new ArgumentException("The joint spacing must be at least 0.5 m.");
        double aspectLimit = Math.Max(1, set.AspectLimit);
        double minGap = Math.Max(0.5, 0.25 * spacing);
        var warnings = new List<string>();

        Pt origin = outer[0];
        Pt? setOutCorner = null;
        double angle = input.SetOut == JointSetOut.DrawingAxes ? 0 : input.AngleRad ?? LongestEdgeAngle(outer);
        var e1 = new Pt(Math.Cos(angle), Math.Sin(angle));
        var e2 = new Pt(-Math.Sin(angle), Math.Cos(angle));
        bool exact = false;
        if (input.SetOut == JointSetOut.FromCorner)
        {
            int at = input.Corner is { } c ? Nearest(outer, new Pt(c.X / u, c.Y / u)) : BestCorner(outer);
            var a = outer[at];
            var next = outer[(at + 1) % outer.Count];
            var prev = outer[(at - 1 + outer.Count) % outer.Count];
            var d1 = Unit(new Pt(next.X - a.X, next.Y - a.Y));
            var d2 = Unit(new Pt(prev.X - a.X, prev.Y - a.Y));
            if (Math.Abs(d1.X * d2.Y - d1.Y * d2.X) < Math.Sin(15 * Math.PI / 180))
                warnings.Add("The edges at that corner are nearly in line: set out square to the longest edge instead. Pick another corner.");
            else
            {
                origin = a;
                e1 = d1;
                e2 = d2;
                angle = Math.Atan2(d1.Y, d1.X);
                setOutCorner = new Pt(a.X * u, a.Y * u);
                exact = input.ExactSteps;
            }
        }
        double det = e1.X * e2.Y - e1.Y * e2.X;
        Pt ToLocal(Pt p)
        {
            double x = p.X - origin.X, y = p.Y - origin.Y;
            return new Pt((x * e2.Y - y * e2.X) / det, (e1.X * y - e1.Y * x) / det);
        }
        Pt ToMetres(Pt p) => new(origin.X + p.X * e1.X + p.Y * e2.X, origin.Y + p.X * e1.Y + p.Y * e2.Y);

        var loop = outer.Select(ToLocal).ToList();
        if (Area(loop) < 0) loop.Reverse();
        var holes = holesM.Select(h => h.Select(ToLocal).ToList()).ToList();
        var loops = new List<List<Pt>> { loop };
        loops.AddRange(holes);

        double minX = loop.Min(p => p.X), maxX = loop.Max(p => p.X), minY = loop.Min(p => p.Y), maxY = loop.Max(p => p.Y);
        if ((maxX - minX) / spacing > 2000 || (maxY - minY) / spacing > 2000)
            throw new ArgumentException("The slab is too big for the joint spacing: check the drawing units.");

        var corners = new List<Pt>();
        for (int i = 0; i < loop.Count; i++)
            if (IsReflex(loop, i)) corners.Add(loop[i]);
        foreach (var hole in holes)
        {
            double hx0 = hole.Min(p => p.X), hx1 = hole.Max(p => p.X), hy0 = hole.Min(p => p.Y), hy1 = hole.Max(p => p.Y);
            corners.AddRange(new[] { new Pt(hx0, hy0), new Pt(hx1, hy0), new Pt(hx1, hy1), new Pt(hx0, hy1) });
        }

        var keysX = Keys(corners.Select(c => c.X), minX, maxX, minGap);
        var keysY = Keys(corners.Select(c => c.Y), minY, maxY, minGap);
        List<double> allX, allY;
        if (exact)
        {
            allX = Steps(keysX, spacing, minGap);
            allY = Steps(keysY, spacing, minGap);
        }
        else
        {
            var countsX = Counts(keysX, spacing);
            var countsY = Counts(keysY, spacing);
            for (int guard = 0; guard < 200; guard++)
            {
                var xs = Lines(keysX, countsX);
                var ys = Lines(keysY, countsY);
                bool changed = false;
                for (int i = 0; i + 1 < xs.Count && !changed; i++)
                    for (int j = 0; j + 1 < ys.Count && !changed; j++)
                    {
                        double w = xs[i + 1] - xs[i], h = ys[j + 1] - ys[j];
                        if (Math.Max(w, h) / Math.Min(w, h) <= aspectLimit + 1e-9) continue;
                        if (!InRegion(loops, new Pt((xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2))) continue;
                        changed = w > h ? Split(keysX, countsX, xs[i], xs[i + 1], minGap) : Split(keysY, countsY, ys[j], ys[j + 1], minGap);
                    }
                if (!changed) break;
            }
            allX = Lines(keysX, countsX);
            allY = Lines(keysY, countsY);
        }

        var lineX = allX.Skip(1).SkipLast(1).ToList();
        var lineY = allY.Skip(1).SkipLast(1).ToList();
        var segments = new List<(Pt A, Pt B)>();
        foreach (var x in lineX)
            foreach (var (a, b) in Clip(loops, x, vertical: true)) segments.Add((new Pt(x, a), new Pt(x, b)));
        foreach (var y in lineY)
            foreach (var (a, b) in Clip(loops, y, vertical: false)) segments.Add((new Pt(a, y), new Pt(b, y)));

        int panels = 0;
        double largest = 0, worst = 1;
        for (int i = 0; i + 1 < allX.Count; i++)
            for (int j = 0; j + 1 < allY.Count; j++)
            {
                if (!InRegion(loops, new Pt((allX[i] + allX[i + 1]) / 2, (allY[j] + allY[j + 1]) / 2))) continue;
                double w = allX[i + 1] - allX[i], h = allY[j + 1] - allY[j];
                panels++;
                largest = Math.Max(largest, Math.Max(w, h));
                worst = Math.Max(worst, Math.Max(w, h) / Math.Min(w, h));
            }

        var check = new List<Pt>();
        int jointed = 0, missed = 0;
        foreach (var c in corners)
        {
            bool onJoint = lineX.Any(x => Math.Abs(x - c.X) < 1e-4) || lineY.Any(y => Math.Abs(y - c.Y) < 1e-4);
            if (onJoint) jointed++;
            else { missed++; check.Add(c); }
        }
        if (missed > 0)
            warnings.Add($"{missed} corner(s) (re-entrant or at a pit) are too close to another joint or the edge to get their own joint: trimmer bars there (circled).");

        var worldSegments = segments.Select(s => (A: ToMetres(s.A), B: ToMetres(s.B))).ToList();
        var checkM = check.Select(ToMetres).ToList();
        int acute = 0;
        for (int i = 0; i < outer.Count; i++)
        {
            var p = outer[i];
            var q = outer[(i + 1) % outer.Count];
            var d = Unit(new Pt(q.X - p.X, q.Y - p.Y));
            foreach (var (a, b) in worldSegments)
            {
                var jd = Unit(new Pt(b.X - a.X, b.Y - a.Y));
                double meet = Math.Acos(Math.Min(1, Math.Abs(d.X * jd.X + d.Y * jd.Y))) * 180 / Math.PI;
                if (meet < 1 || meet >= 60) continue;
                foreach (var end in new[] { a, b })
                    if (DistanceToSegment(end, p, q) < 1e-4) { checkM.Add(end); acute++; }
            }
        }
        if (acute > 0)
            warnings.Add($"{acute} joint end(s) meet an edge at under 60°: acute panel corners - trimmer bars, or set out from a corner on that edge (circled).");
        if (worst > aspectLimit + 1e-6)
            warnings.Add(exact
                ? $"Exact circle steps leave panels up to {worst:0.0}:1 (limit {aspectLimit:0.0#}:1) at the far ends: check them, or use equal panels."
                : $"Some panels are {worst:0.0}:1 (limit {aspectLimit:0.0#}:1) because joints are kept at corners and pits: check them.");

        double sawcut = worldSegments.Sum(s => Distance(s.A, s.B));
        double perimeter = Perimeter(outer) + holesM.Sum(Perimeter);
        double area = Math.Abs(Area(outer)) - holesM.Sum(h => Math.Abs(Area(h)));
        int depth = (int)(Math.Ceiling(input.ThicknessMm / 4 / 5 - 1e-9) * 5);
        return new SlabJointResult(
            worldSegments.Select(s => new JointSegment(new Pt(s.A.X * u, s.A.Y * u), new Pt(s.B.X * u, s.B.Y * u))).ToList(),
            checkM.Select(c => new Pt(c.X * u, c.Y * u)).ToList(), warnings, spacing, angle, panels, sawcut, perimeter, area, largest, worst, depth, jointed,
            setOutCorner);
    }

    public static IReadOnlyList<Pt> Corners(IReadOnlyList<Pt> boundary, double unitsPerMetre)
    {
        var outer = Clean(boundary.Select(p => new Pt(p.X / unitsPerMetre, p.Y / unitsPerMetre)).ToList());
        return outer.Select(p => new Pt(p.X * unitsPerMetre, p.Y * unitsPerMetre)).ToList();
    }

    private static int Nearest(List<Pt> loop, Pt p)
    {
        int best = 0;
        for (int i = 1; i < loop.Count; i++)
            if (Distance(loop[i], p) < Distance(loop[best], p)) best = i;
        return best;
    }

    private static int BestCorner(List<Pt> loop)
    {
        int best = 0;
        double score = -1;
        for (int i = 0; i < loop.Count; i++)
        {
            if (IsReflex(loop, i)) continue;
            var a = loop[i];
            var next = loop[(i + 1) % loop.Count];
            var prev = loop[(i - 1 + loop.Count) % loop.Count];
            var d1 = Unit(new Pt(next.X - a.X, next.Y - a.Y));
            var d2 = Unit(new Pt(prev.X - a.X, prev.Y - a.Y));
            if (Math.Abs(d1.X * d2.Y - d1.Y * d2.X) < Math.Sin(15 * Math.PI / 180)) continue;
            double s = Distance(a, next) + Distance(a, prev);
            if (s > score) { score = s; best = i; }
        }
        return best;
    }

    private static List<double> Steps(List<(double Pos, bool Fixed)> keys, double spacing, double minGap)
    {
        double min = keys[0].Pos, max = keys[^1].Pos;
        var lines = keys.Select(k => k.Pos).ToList();
        for (int k = (int)Math.Ceiling(min / spacing - 1e-9); k * spacing < max - 1e-9; k++)
        {
            double v = k * spacing;
            if (lines.Any(l => Math.Abs(l - v) < minGap)) continue;
            lines.Add(v);
        }
        lines.Sort();
        var filled = new List<double> { lines[0] };
        for (int i = 1; i < lines.Count; i++)
        {
            double gap = lines[i] - filled[^1];
            int n = Math.Max(1, (int)Math.Ceiling(gap / spacing - 1e-9));
            double step = gap / n;
            for (int k = 1; k < n; k++) filled.Add(filled[^1] + step);
            filled.Add(lines[i]);
        }
        return filled;
    }

    private static Pt Unit(Pt p)
    {
        double l = Math.Sqrt(p.X * p.X + p.Y * p.Y);
        return l < 1e-12 ? new Pt(1, 0) : new Pt(p.X / l, p.Y / l);
    }

    public static double LongestEdgeAngle(IReadOnlyList<Pt> loop)
    {
        double best = -1, angle = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var p = loop[i];
            var q = loop[(i + 1) % loop.Count];
            double d = Distance(p, q);
            if (d <= best) continue;
            best = d;
            angle = Math.Atan2(q.Y - p.Y, q.X - p.X);
        }
        angle %= Math.PI / 2;
        if (angle < 0) angle += Math.PI / 2;
        return angle;
    }

    private static List<(double Pos, bool Fixed)> Keys(IEnumerable<double> wanted, double min, double max, double minGap)
    {
        var keys = new List<(double, bool)> { (min, true), (max, true) };
        foreach (var v in wanted.OrderBy(v => v))
        {
            if (v - min < minGap || max - v < minGap) continue;
            if (keys.Any(k => Math.Abs(k.Item1 - v) < minGap)) continue;
            keys.Add((v, true));
        }
        keys.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return keys;
    }

    private static List<int> Counts(List<(double Pos, bool Fixed)> keys, double spacing)
    {
        var counts = new List<int>();
        for (int i = 0; i + 1 < keys.Count; i++)
            counts.Add(Math.Max(1, (int)Math.Ceiling((keys[i + 1].Pos - keys[i].Pos) / spacing - 1e-9)));
        return counts;
    }

    private static List<double> Lines(List<(double Pos, bool Fixed)> keys, List<int> counts)
    {
        var lines = new List<double> { keys[0].Pos };
        for (int i = 0; i + 1 < keys.Count; i++)
        {
            double a = keys[i].Pos, b = keys[i + 1].Pos;
            for (int k = 1; k <= counts[i]; k++) lines.Add(a + (b - a) * k / counts[i]);
        }
        return lines;
    }

    private static bool Split(List<(double Pos, bool Fixed)> keys, List<int> counts, double from, double to, double minGap)
    {
        double mid = (from + to) / 2;
        for (int i = 0; i + 1 < keys.Count; i++)
        {
            if (mid < keys[i].Pos || mid > keys[i + 1].Pos) continue;
            double length = keys[i + 1].Pos - keys[i].Pos;
            if (length / (counts[i] + 1) < minGap) return false;
            counts[i]++;
            return true;
        }
        return false;
    }

    private static List<(double A, double B)> Clip(List<List<Pt>> loops, double c, bool vertical)
    {
        var hits = new List<double>();
        foreach (var loop in loops)
            for (int i = 0; i < loop.Count; i++)
            {
                var p = loop[i];
                var q = loop[(i + 1) % loop.Count];
                double pc = vertical ? p.X : p.Y, qc = vertical ? q.X : q.Y;
                double pa = vertical ? p.Y : p.X, qa = vertical ? q.Y : q.X;
                if (Math.Abs(pc - c) < Eps) pc += 2 * Eps;
                if (Math.Abs(qc - c) < Eps) qc += 2 * Eps;
                if ((pc - c) * (qc - c) >= 0) continue;
                hits.Add(pa + (qa - pa) * (c - pc) / (qc - pc));
            }
        hits.Sort();
        var spans = new List<(double, double)>();
        for (int i = 0; i + 1 < hits.Count; i += 2)
            if (hits[i + 1] - hits[i] > 1e-6) spans.Add((hits[i], hits[i + 1]));
        return spans;
    }

    private static bool InRegion(List<List<Pt>> loops, Pt p) => loops.Count(l => Inside(l, p)) % 2 == 1;

    private static bool Inside(IReadOnlyList<Pt> loop, Pt p)
    {
        bool inside = false;
        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
        {
            var a = loop[i];
            var b = loop[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    private static bool IsReflex(List<Pt> loop, int i)
    {
        var a = loop[(i - 1 + loop.Count) % loop.Count];
        var b = loop[i];
        var c = loop[(i + 1) % loop.Count];
        double cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
        double scale = Distance(a, b) * Distance(b, c);
        return scale > 0 && cross / scale < -0.05;
    }

    private static List<Pt> Clean(List<Pt> pts)
    {
        var result = new List<Pt>();
        foreach (var p in pts)
            if (result.Count == 0 || Distance(result[^1], p) > 1e-6) result.Add(p);
        if (result.Count > 1 && Distance(result[0], result[^1]) < 1e-6) result.RemoveAt(result.Count - 1);
        for (int i = 0; result.Count > 3 && i < result.Count;)
        {
            var a = result[(i - 1 + result.Count) % result.Count];
            var b = result[i];
            var c = result[(i + 1) % result.Count];
            double cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
            if (Math.Abs(cross) < 1e-9 * Math.Max(1, Distance(a, b) * Distance(b, c))) result.RemoveAt(i); else i++;
        }
        return result;
    }

    private static double Area(IReadOnlyList<Pt> loop)
    {
        double a = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var p = loop[i];
            var q = loop[(i + 1) % loop.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return a / 2;
    }

    private static Pt Centroid(IReadOnlyList<Pt> loop) => new(loop.Average(p => p.X), loop.Average(p => p.Y));

    private static double Perimeter(List<Pt> loop)
    {
        double total = 0;
        for (int i = 0; i < loop.Count; i++) total += Distance(loop[i], loop[(i + 1) % loop.Count]);
        return total;
    }

    private static double Distance(Pt a, Pt b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double DistanceToSegment(Pt p, Pt a, Pt b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len = dx * dx + dy * dy;
        double t = len == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len, 0, 1);
        return Distance(p, new Pt(a.X + t * dx, a.Y + t * dy));
    }
}
