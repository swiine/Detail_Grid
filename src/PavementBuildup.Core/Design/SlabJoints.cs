namespace PavementBuildup.Core.Design;

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
    int CornersJointed);

public static class SlabJoints
{
    private const double Eps = 1e-7;

    public static SlabJointResult Layout(SlabJointInputs input)
    {
        if (input.Boundary.Count < 3) throw new ArgumentException("The slab boundary needs at least 3 corners.");
        if (!(input.UnitsPerMetre > 0)) throw new ArgumentException("Unknown drawing units.");
        if (!(input.ThicknessMm > 0)) throw new ArgumentException("Enter the slab thickness (mm).");

        double u = input.UnitsPerMetre;
        var outer = Clean(input.Boundary.Select(p => new Pt(p.X / u, p.Y / u)).ToList());
        if (outer.Count < 3 || Math.Abs(Area(outer)) < 1e-6) throw new ArgumentException("The slab boundary has no area.");
        if (Area(outer) < 0) outer.Reverse();

        var set = input.Rules ?? new JointRuleSet();
        var spacing = JointLayout.SpacingLimit(new JointInputs { ThicknessMm = input.ThicknessMm, Kind = input.Kind, MaxSpacingM = input.MaxSpacingM, Rules = set });
        if (!(spacing >= 0.5)) throw new ArgumentException("The joint spacing must be at least 0.5 m.");
        double aspectLimit = Math.Max(1, set.AspectLimit);
        double minGap = Math.Max(0.5, 0.25 * spacing);

        double angle = input.AngleRad ?? LongestEdgeAngle(outer);
        var origin = outer[0];
        Pt ToLocal(Pt p) => Rotate(new Pt(p.X - origin.X, p.Y - origin.Y), -angle);
        Pt ToWorld(Pt p) { var r = Rotate(p, angle); return new Pt((r.X + origin.X) * u, (r.Y + origin.Y) * u); }

        var loop = outer.Select(ToLocal).ToList();
        var holes = new List<List<Pt>>();
        foreach (var h in input.Holes)
        {
            var hole = Clean(h.Select(p => ToLocal(new Pt(p.X / u, p.Y / u))).ToList());
            if (hole.Count >= 3 && Math.Abs(Area(hole)) > 1e-6 && Inside(loop, Centroid(hole))) holes.Add(hole);
        }
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

        var lineX = Lines(keysX, countsX).Skip(1).SkipLast(1).ToList();
        var lineY = Lines(keysY, countsY).Skip(1).SkipLast(1).ToList();
        var segments = new List<(Pt A, Pt B)>();
        foreach (var x in lineX)
            foreach (var (a, b) in Clip(loops, x, vertical: true)) segments.Add((new Pt(x, a), new Pt(x, b)));
        foreach (var y in lineY)
            foreach (var (a, b) in Clip(loops, y, vertical: false)) segments.Add((new Pt(a, y), new Pt(b, y)));

        var allX = Lines(keysX, countsX);
        var allY = Lines(keysY, countsY);
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
        var warnings = new List<string>();
        int jointed = 0, missed = 0;
        foreach (var c in corners)
        {
            bool onJoint = lineX.Any(x => Math.Abs(x - c.X) < 1e-4) || lineY.Any(y => Math.Abs(y - c.Y) < 1e-4);
            if (onJoint) jointed++;
            else { missed++; check.Add(c); }
        }
        if (missed > 0)
            warnings.Add($"{missed} corner(s) (re-entrant or at a pit) are too close to another joint or the edge to get their own joint: trimmer bars there (circled).");

        int acute = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var p = loop[i];
            var q = loop[(i + 1) % loop.Count];
            double deg = Math.Abs(Math.Atan2(q.Y - p.Y, q.X - p.X) * 180 / Math.PI) % 180;
            if (deg > 90) deg = 180 - deg;
            if (deg < 1 || deg > 89) continue;
            foreach (var (a, b) in segments)
            {
                bool vertical = Math.Abs(a.X - b.X) < 1e-9;
                double meet = vertical ? 90 - deg : deg;
                if (meet >= 60) continue;
                foreach (var end in new[] { a, b })
                    if (DistanceToSegment(end, p, q) < 1e-4) { check.Add(end); acute++; }
            }
        }
        if (acute > 0)
            warnings.Add($"{acute} joint end(s) meet a skewed edge at under 60°: acute panel corners - trimmer bars or turn those joints square to the edge (circled).");
        if (worst > aspectLimit + 1e-6)
            warnings.Add($"Some panels are {worst:0.0}:1 (limit {aspectLimit:0.0#}:1) because joints are kept at corners and pits: check them.");

        double sawcut = segments.Sum(s => Distance(s.A, s.B));
        double perimeter = loops.Sum(Perimeter);
        double area = Math.Abs(Area(loop)) - holes.Sum(h => Math.Abs(Area(h)));
        int depth = (int)(Math.Ceiling(input.ThicknessMm / 4 / 5 - 1e-9) * 5);
        return new SlabJointResult(
            segments.Select(s => new JointSegment(ToWorld(s.A), ToWorld(s.B))).ToList(),
            check.Select(ToWorld).ToList(), warnings, spacing, angle, panels, sawcut, perimeter, area, largest, worst, depth, jointed);
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

    private static Pt Rotate(Pt p, double a) => new(p.X * Math.Cos(a) - p.Y * Math.Sin(a), p.X * Math.Sin(a) + p.Y * Math.Cos(a));

    private static double Distance(Pt a, Pt b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double DistanceToSegment(Pt p, Pt a, Pt b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len = dx * dx + dy * dy;
        double t = len == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len, 0, 1);
        return Distance(p, new Pt(a.X + t * dx, a.Y + t * dy));
    }
}
