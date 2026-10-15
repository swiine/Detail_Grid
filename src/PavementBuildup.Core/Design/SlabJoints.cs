namespace PavementBuildup.Core.Design;

public enum JointSetOut { AcrossWidth, LongestEdge, DrawingAxes, FromCorner }

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
    public JointSetOut SetOut { get; init; } = JointSetOut.AcrossWidth;
    public Pt? Corner { get; init; }
    public bool ExactSteps { get; init; }
    public Pt? GuidePoint { get; init; }
    public double MinAngleDeg { get; init; } = 85;
    public bool Lengthways { get; init; }
    public double LengthwaysSpacingM { get; init; }
    public bool NoJunctions { get; init; }
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
    Pt? SetOutCorner = null,
    IReadOnlyList<Pt>? Guide = null,
    IReadOnlyList<IReadOnlyList<Pt>>? LongJoints = null,
    int Strips = 1);

public static partial class SlabJoints
{
    private const double Eps = 1e-7;

    public static SlabJointResult Layout(SlabJointInputs input)
    {
        if (input.Boundary.Count < 3) throw new ArgumentException("The slab boundary needs at least 3 corners.");
        if (!(input.UnitsPerMetre > 0)) throw new ArgumentException("Unknown drawing units.");
        if (!(input.ThicknessMm > 0)) throw new ArgumentException("Enter the slab thickness (mm).");
        if (input.SetOut == JointSetOut.AcrossWidth && !input.NoJunctions && Junctions(input) is { } junction) return junction;
        if (input.SetOut == JointSetOut.AcrossWidth && input.GuidePoint is null)
        {
            var metres = Clean(input.Boundary.Select(p => new Pt(p.X / input.UnitsPerMetre, p.Y / input.UnitsPerMetre)).ToList());
            if (metres.Count >= 3 && Math.Abs(Area(metres)) > 1e-6)
            {
                if (Area(metres) < 0) metres.Reverse();
                SlabJointResult? best = null;
                foreach (var side in Sides(metres).OrderByDescending(PolylineLength))
                {
                    var mid = PointAt(side, PolylineLength(side) / 2);
                    var r = Across(input with { GuidePoint = new Pt(mid.X * input.UnitsPerMetre, mid.Y * input.UnitsPerMetre) });
                    if (best is null || Score(r) < Score(best) - 1e-9) best = r;
                }
                if (best is not null) return best;
            }
        }
        if (input.SetOut == JointSetOut.AcrossWidth) return Across(input);
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

    private static double Score(SlabJointResult r)
    {
        double wanted = 0.6 * r.AreaM2 / Math.Max(0.5, r.SpacingM);
        double shortfall = wanted <= 0 ? 0 : Math.Max(0, 1 - r.SawcutLengthM / wanted);
        return r.CheckPoints.Count + 10 * shortfall;
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

    private static SlabJointResult Across(SlabJointInputs input)
    {
        double u = input.UnitsPerMetre;
        var outer = Clean(input.Boundary.Select(p => new Pt(p.X / u, p.Y / u)).ToList());
        if (outer.Count < 3 || Math.Abs(Area(outer)) < 1e-6) throw new ArgumentException("The slab boundary has no area.");
        if (Area(outer) < 0) outer.Reverse();
        var holes = input.Holes.Select(h => Clean(h.Select(p => new Pt(p.X / u, p.Y / u)).ToList()))
            .Where(h => h.Count >= 3 && Math.Abs(Area(h)) > 1e-6 && Inside(outer, Centroid(h))).ToList();
        var set = input.Rules ?? new JointRuleSet();
        var spacing = JointLayout.SpacingLimit(new JointInputs { ThicknessMm = input.ThicknessMm, Kind = input.Kind, MaxSpacingM = input.MaxSpacingM, Rules = set });
        if (!(spacing >= 0.5)) throw new ArgumentException("The joint spacing must be at least 0.5 m.");
        double perimeter = Perimeter(outer) + holes.Sum(Perimeter);
        if (perimeter / spacing > 20000) throw new ArgumentException("The slab is too big for the joint spacing: check the drawing units.");
        var warnings = new List<string>();

        var sides = Sides(outer);
        List<Pt> guide;
        if (input.GuidePoint is { } gp)
        {
            var g = new Pt(gp.X / u, gp.Y / u);
            guide = sides.OrderBy(side => DistanceToPolyline(g, side)).First();
        }
        else guide = sides.OrderByDescending(PolylineLength).First();
        double length = PolylineLength(guide);

        var stations = new List<double>();
        if (input.ExactSteps)
            for (double st = spacing; st < length - Math.Min(0.5, 0.25 * spacing); st += spacing) stations.Add(st);
        else
        {
            int n = Math.Max(1, (int)Math.Ceiling(length / spacing - 1e-9));
            for (int k = 1; k < n; k++) stations.Add(length * k / n);
        }

        var joints = new List<(Pt A, Pt B)>();
        var check = new List<Pt>();
        double widest = 0, narrowest = double.MaxValue;
        int dogLegs = 0;
        (Pt P, Pt Q, Pt D, Pt T, Pt Tq)? Cross(double st)
        {
            var (p, t) = AtStation(guide, st, Math.Min(0.25, spacing / 10));
            var normal = new Pt(-t.Y, t.X);
            var d = normal;
            (Pt Q, Pt Tq)? hit = null;
            for (int iter = 0; iter < 4; iter++)
            {
                hit = Cast(outer, p, d);
                if (hit is null) break;
                var tq = new Pt(-hit.Value.Tq.X, -hit.Value.Tq.Y);
                if (t.X * tq.X + t.Y * tq.Y < Math.Cos(45 * Math.PI / 180)) break;
                var b = Unit(new Pt(t.X + tq.X, t.Y + tq.Y));
                var next = new Pt(-b.Y, b.X);
                if (next.X * normal.X + next.Y * normal.Y < 0) next = new Pt(-next.X, -next.Y);
                if (Math.Abs(next.X - d.X) + Math.Abs(next.Y - d.Y) < 1e-9) break;
                d = next;
            }
            if (hit is null) return null;
            return (p, hit.Value.Q, d, t, Unit(hit.Value.Tq));
        }

        const double Square = 89.5;
        static double LineAngle(Pt x, Pt y) => Math.Acos(Math.Min(1, Math.Abs(x.X * y.X + x.Y * y.Y))) * 180 / Math.PI;
        static bool Opposite(Pt t, Pt farT) => -(t.X * farT.X + t.Y * farT.Y) > Math.Cos(45 * Math.PI / 180);
        Pt Along(Pt p, Pt d, double dist) => new(p.X + d.X * dist, p.Y + d.Y * dist);
        (Pt P, Pt T, Pt N, double Width, Pt FarT)? Normal(double st)
        {
            var (p, t) = AtStation(guide, st, Math.Min(0.75, length / 8));
            var n = new Pt(-t.Y, t.X);
            if (Cast(outer, p, n) is not { } hit) return null;
            return (p, t, n, Distance(p, hit.Q), SmoothTangent(outer, hit.Q));
        }

        double margin = Math.Min(Math.Min(0.25, spacing / 10) + 0.01, length / 4);
        double lastSample = Math.Max(margin + 0.01, length - margin);
        double sampleStep = Math.Max(0.05, Math.Min(0.5, spacing / 4));
        var sampleS = new List<double>();
        for (double st = margin; ; st += sampleStep)
        {
            sampleS.Add(Math.Min(st, lastSample));
            if (st >= lastSample) break;
        }
        var normals = sampleS.Select(Normal).ToList();

        int strips = 1;
        double w = 0;
        if (input.Lengthways)
        {
            double along = input.LengthwaysSpacingM > 0 ? input.LengthwaysSpacingM : spacing;
            if (!(along >= 0.3)) throw new ArgumentException("The lengthways joint spacing must be at least 0.3 m.");
            double maxWidth = normals.Where(x => x is { } v && Opposite(v.T, v.FarT)).Select(x => x!.Value.Width).DefaultIfEmpty(0).Max();
            strips = Math.Max(1, (int)Math.Ceiling(maxWidth / along - 0.02));
            w = maxWidth / strips;
        }
        int Levels(double width) => strips <= 1 ? 0 : Math.Clamp((int)Math.Floor((width - 0.3 * w) / w + 1e-9), 0, strips - 1);

        int omitted = 0, endSkipped = 0;
        foreach (var st in stations)
        {
            if (Normal(st) is not { } nrm) continue;
            var (p, t, n, widthN, farT) = nrm;
            if (!Opposite(t, farT)) { endSkipped++; continue; }
            widest = Math.Max(widest, widthN);
            narrowest = Math.Min(narrowest, widthN);
            int levels = Levels(widthN);
            if (LineAngle(n, farT) >= Square)
            {
                foreach (var piece in AroundHoles(p, Along(p, n, widthN), holes)) joints.Add(piece);
                continue;
            }
            if (levels == 0 && Cross(st) is { } c && LineAngle(c.D, c.T) >= Square && LineAngle(c.D, c.Tq) >= Square)
            {
                foreach (var piece in AroundHoles(c.P, c.Q, holes)) joints.Add(piece);
                continue;
            }
            double h = (levels * w + widthN) / 2;
            var kink = Along(p, n, h);
            if (Inside(outer, kink) && Cast(outer, kink, n) is { } up)
            {
                var upT = SmoothTangent(outer, up.Q);
                var d2 = new Pt(-upT.Y, upT.X);
                if (d2.X * n.X + d2.Y * n.Y < 0) d2 = new Pt(-d2.X, -d2.Y);
                if (d2.X * n.X + d2.Y * n.Y > 0 && Cast(outer, kink, d2) is { } far && LineAngle(d2, SmoothTangent(outer, far.Q)) >= Square)
                {
                    foreach (var piece in AroundHoles(p, kink, holes)) joints.Add(piece);
                    foreach (var piece in AroundHoles(kink, far.Q, holes)) joints.Add(piece);
                    dogLegs++;
                    continue;
                }
            }
            omitted++;
            check.Add(p);
        }
        if (omitted > 0)
            warnings.Add($"{omitted} joint(s) couldn't be made square to both edges and were left out (circled): set those by hand.");
        if (endSkipped > 0)
            warnings.Add($"{endSkipped} joint(s) next to a slanted end were left out (they'd run into the end edge): the end panel is longer - check it against the spacing.");
        if (dogLegs > 0)
            warnings.Add($"{dogLegs} joint(s) are dog-legged (two pieces, each square to its edge) where the sides aren't parallel.");

        var longJoints = new List<List<Pt>>();
        for (int k = 1; k < strips; k++)
        {
            double offset = k * w;
            Pt? EdgeEnd(int index, bool atStart)
            {
                if (normals[index] is not { } v) return null;
                var from = Along(v.P, v.N, offset);
                var dir = atStart ? new Pt(-v.T.X, -v.T.Y) : v.T;
                return Cast(outer, from, dir) is { } hit && Distance(from, hit.Q) < Math.Max(1, spacing) && LineAngle(dir, SmoothTangent(outer, hit.Q)) >= Square ? hit.Q : null;
            }
            Pt? AtStationPoint(double st) => Normal(st) is { } v ? Along(v.P, v.N, offset) : null;
            var ok = normals.Select(x => x is { } v && Opposite(v.T, v.FarT) && v.Width >= offset + 0.3 * w).ToList();
            for (int i = 0; i < ok.Count;)
            {
                if (!ok[i]) { i++; continue; }
                int j = i;
                while (j + 1 < ok.Count && ok[j + 1]) j++;
                var startEdge = i == 0 ? EdgeEnd(0, atStart: true) : null;
                var endEdge = j == ok.Count - 1 ? EdgeEnd(ok.Count - 1, atStart: false) : null;
                double from = startEdge is not null ? double.NegativeInfinity : stations.Where(x => x >= sampleS[i] - 1e-9).DefaultIfEmpty(double.NaN).Min();
                double to = endEdge is not null ? double.PositiveInfinity : stations.Where(x => x <= sampleS[j] + 1e-9).DefaultIfEmpty(double.NaN).Max();
                if (!double.IsNaN(from) && !double.IsNaN(to) && from < to)
                {
                    var line = new List<Pt>();
                    if (startEdge is { } se) line.Add(se);
                    else if (AtStationPoint(from) is { } sp) line.Add(sp);
                    for (int m = i; m <= j; m++)
                        if (sampleS[m] > from + 1e-9 && sampleS[m] < to - 1e-9 && normals[m] is { } v) line.Add(Along(v.P, v.N, offset));
                    if (endEdge is { } ee) line.Add(ee);
                    else if (AtStationPoint(to) is { } ep) line.Add(ep);
                    if (line.Count >= 2) longJoints.AddRange(SplitAtHoles(line, holes));
                }
                i = j + 1;
            }
        }

        int missed = 0;
        for (int i = 0; i < outer.Count; i++)
        {
            if (!IsReflex(outer, i) || Turn(outer, i) <= 30) continue;
            if (joints.Any(j => DistanceToSegment(outer[i], j.A, j.B) < 0.05)) continue;
            if (longJoints.Any(l => DistanceToPolyline(outer[i], l) < 0.05)) continue;
            missed++;
            check.Add(outer[i]);
        }
        if (missed > 0)
            warnings.Add($"{missed} inside corner(s) are not on a joint: trimmer bars there, or move a joint onto the corner (circled).");

        double gap = stations.Count == 0 ? length : stations.Zip(stations.Skip(1).Append(length), (a, b) => b - a).Prepend(stations[0]).Max();
        double width0 = widest > 0 ? widest : 0;
        double aspect = width0 > 0 && gap > 0 ? Math.Max(gap, width0) / Math.Min(gap, width0) : 1;
        double sawcut = joints.Sum(j => Distance(j.A, j.B)) + longJoints.Sum(PolylineLength);
        double area = Math.Abs(Area(outer)) - holes.Sum(h => Math.Abs(Area(h)));
        int depth = (int)(Math.Ceiling(input.ThicknessMm / 4 / 5 - 1e-9) * 5);
        Pt W(Pt m) => new(m.X * u, m.Y * u);
        return new SlabJointResult(
            joints.Select(j => new JointSegment(W(j.A), W(j.B))).ToList(), check.Select(W).ToList(), warnings, spacing,
            Math.Atan2(guide[^1].Y - guide[0].Y, guide[^1].X - guide[0].X), (stations.Count + 1) * strips, sawcut, perimeter, area,
            Math.Max(gap, strips > 1 ? width0 / strips : width0), aspect, depth, 0, null, guide.Select(W).ToList(),
            longJoints.Select(l => (IReadOnlyList<Pt>)l.Select(W).ToList()).ToList(), strips);
    }

    private static double Turn(List<Pt> loop, int i)
    {
        var a = loop[(i - 1 + loop.Count) % loop.Count];
        var b = loop[i];
        var c = loop[(i + 1) % loop.Count];
        var d1 = Unit(new Pt(b.X - a.X, b.Y - a.Y));
        var d2 = Unit(new Pt(c.X - b.X, c.Y - b.Y));
        return Math.Acos(Math.Clamp(d1.X * d2.X + d1.Y * d2.Y, -1, 1)) * 180 / Math.PI;
    }

    private static Pt SmoothTangent(List<Pt> loop, Pt q, double reach = 0.75)
    {
        int edge = 0;
        double best = double.MaxValue;
        for (int i = 0; i < loop.Count; i++)
        {
            double d = DistanceToSegment(q, loop[i], loop[(i + 1) % loop.Count]);
            if (d < best) { best = d; edge = i; }
        }
        Pt Walk(int step)
        {
            var at = q;
            double left = reach;
            int i = step > 0 ? (edge + 1) % loop.Count : edge;
            for (int guard = 0; guard < loop.Count; guard++)
            {
                var next = loop[i];
                double d = Distance(at, next);
                if (d >= left) return new Pt(at.X + (next.X - at.X) * left / d, at.Y + (next.Y - at.Y) * left / d);
                left -= d;
                at = next;
                if (Turn(loop, i) > 30) return at;
                i = step > 0 ? (i + 1) % loop.Count : (i - 1 + loop.Count) % loop.Count;
            }
            return at;
        }
        var a = Walk(-1);
        var b = Walk(+1);
        return Distance(a, b) < 1e-9 ? Unit(new Pt(loop[(edge + 1) % loop.Count].X - loop[edge].X, loop[(edge + 1) % loop.Count].Y - loop[edge].Y)) : Unit(new Pt(b.X - a.X, b.Y - a.Y));
    }

    private static Pt? DogLeg(List<Pt> loop, Pt p, Pt q, Pt t, Pt tq)
    {
        var n1 = new Pt(-t.Y, t.X);
        if (n1.X * (q.X - p.X) + n1.Y * (q.Y - p.Y) < 0) n1 = new Pt(-n1.X, -n1.Y);
        var n2 = new Pt(-tq.Y, tq.X);
        if (n2.X * (p.X - q.X) + n2.Y * (p.Y - q.Y) < 0) n2 = new Pt(-n2.X, -n2.Y);
        double den = n1.X * n2.Y - n1.Y * n2.X;
        if (Math.Abs(den) < Math.Sin(0.5 * Math.PI / 180)) return null;
        double a = ((q.X - p.X) * n2.Y - (q.Y - p.Y) * n2.X) / den;
        double b = ((q.X - p.X) * n1.Y - (q.Y - p.Y) * n1.X) / den;
        double width = Distance(p, q);
        if (a <= 0.05 * width || b <= 0.05 * width || a > width || b > width) return null;
        var k = new Pt(p.X + n1.X * a, p.Y + n1.Y * a);
        return Inside(loop, k) ? k : null;
    }

    private static List<Pt> Extend(List<Pt> loop, List<Pt> line, double reach)
    {
        var result = new List<Pt>(line);
        Pt Dir(Pt from, Pt to) => Unit(new Pt(to.X - from.X, to.Y - from.Y));
        if (Cast(loop, result[0], Dir(result[1], result[0])) is { } start && Distance(start.Q, result[0]) < reach) result.Insert(0, start.Q);
        if (Cast(loop, result[^1], Dir(result[^2], result[^1])) is { } finish && Distance(finish.Q, result[^1]) < reach) result.Add(finish.Q);
        return result;
    }

    private static IEnumerable<List<Pt>> SplitAtHoles(List<Pt> line, List<List<Pt>> holes)
    {
        if (holes.Count == 0) { yield return line; yield break; }
        var current = new List<Pt>();
        for (int i = 0; i + 1 < line.Count; i++)
        {
            foreach (var (a, b) in AroundHoles(line[i], line[i + 1], holes))
            {
                if (current.Count > 0 && Distance(current[^1], a) > 1e-6)
                {
                    if (current.Count >= 2) yield return current;
                    current = new List<Pt>();
                }
                if (current.Count == 0) current.Add(a);
                current.Add(b);
            }
        }
        if (current.Count >= 2) yield return current;
    }

    private static List<List<Pt>> Sides(List<Pt> loop)
    {
        var corners = new List<int>();
        for (int i = 0; i < loop.Count; i++)
            if (Turn(loop, i) > 30) corners.Add(i);
        var sides = new List<List<Pt>>();
        if (corners.Count == 0)
        {
            sides.Add(loop.Append(loop[0]).ToList());
            return sides;
        }
        for (int k = 0; k < corners.Count; k++)
        {
            int from = corners[k], to = corners[(k + 1) % corners.Count];
            var side = new List<Pt> { loop[from] };
            for (int i = (from + 1) % loop.Count; ; i = (i + 1) % loop.Count)
            {
                side.Add(loop[i]);
                if (i == to) break;
            }
            sides.Add(side);
        }
        return sides;
    }

    private static double PolylineLength(List<Pt> line)
    {
        double total = 0;
        for (int i = 0; i + 1 < line.Count; i++) total += Distance(line[i], line[i + 1]);
        return total;
    }

    private static double DistanceToPolyline(Pt p, List<Pt> line)
    {
        double best = double.MaxValue;
        for (int i = 0; i + 1 < line.Count; i++) best = Math.Min(best, DistanceToSegment(p, line[i], line[i + 1]));
        return best;
    }

    private static Pt PointAt(List<Pt> line, double s)
    {
        for (int i = 0; i + 1 < line.Count; i++)
        {
            double seg = Distance(line[i], line[i + 1]);
            if (s <= seg || i + 2 == line.Count)
            {
                double f = seg < 1e-12 ? 0 : Math.Clamp(s / seg, 0, 1);
                return new Pt(line[i].X + (line[i + 1].X - line[i].X) * f, line[i].Y + (line[i + 1].Y - line[i].Y) * f);
            }
            s -= seg;
        }
        return line[^1];
    }

    private static (Pt P, Pt T) AtStation(List<Pt> line, double s, double delta)
    {
        double length = PolylineLength(line);
        var p = PointAt(line, s);
        var a = PointAt(line, Math.Max(0, s - delta));
        var b = PointAt(line, Math.Min(length, s + delta));
        return (p, Unit(new Pt(b.X - a.X, b.Y - a.Y)));
    }

    private static (Pt Q, Pt Tq)? Cast(List<Pt> loop, Pt p, Pt d)
    {
        double best = double.MaxValue;
        Pt q = default, tq = default;
        for (int i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            double ex = b.X - a.X, ey = b.Y - a.Y;
            double den = d.X * ey - d.Y * ex;
            if (Math.Abs(den) < 1e-12) continue;
            double t = ((a.X - p.X) * ey - (a.Y - p.Y) * ex) / den;
            double w = ((a.X - p.X) * d.Y - (a.Y - p.Y) * d.X) / den;
            if (t < 1e-4 || w < -1e-9 || w > 1 + 1e-9 || t >= best) continue;
            best = t;
            q = new Pt(p.X + d.X * t, p.Y + d.Y * t);
            tq = new Pt(ex, ey);
        }
        return best == double.MaxValue ? null : (q, Unit(tq));
    }

    private static IEnumerable<(Pt A, Pt B)> AroundHoles(Pt a, Pt b, List<List<Pt>> holes)
    {
        var cuts = new List<double> { 0, 1 };
        foreach (var hole in holes)
            for (int i = 0; i < hole.Count; i++)
            {
                var p = hole[i];
                var q = hole[(i + 1) % hole.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y, ex = q.X - p.X, ey = q.Y - p.Y;
                double den = dx * ey - dy * ex;
                if (Math.Abs(den) < 1e-12) continue;
                double t = ((p.X - a.X) * ey - (p.Y - a.Y) * ex) / den;
                double w = ((p.X - a.X) * dy - (p.Y - a.Y) * dx) / den;
                if (t > 0 && t < 1 && w >= 0 && w <= 1) cuts.Add(t);
            }
        cuts.Sort();
        for (int i = 0; i + 1 < cuts.Count; i++)
        {
            double m = (cuts[i] + cuts[i + 1]) / 2;
            var mid = new Pt(a.X + (b.X - a.X) * m, a.Y + (b.Y - a.Y) * m);
            if (holes.Any(h => Inside(h, mid)) || cuts[i + 1] - cuts[i] < 1e-9) continue;
            yield return (new Pt(a.X + (b.X - a.X) * cuts[i], a.Y + (b.Y - a.Y) * cuts[i]),
                          new Pt(a.X + (b.X - a.X) * cuts[i + 1], a.Y + (b.Y - a.Y) * cuts[i + 1]));
        }
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
