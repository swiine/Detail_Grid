namespace PavementBuildup.Core.Design;

public static partial class SlabJoints
{
    private sealed record Return(int First, int Last, Pt S, Pt E, Pt M, Pt N);

    private const double SquareDeg = 89.5;

    private static double LineAngleDeg(Pt a, Pt b) => Math.Acos(Math.Min(1, Math.Abs(a.X * b.X + a.Y * b.Y))) * 180 / Math.PI;

    private static double FullAngle(Pt a, Pt b) => Math.Acos(Math.Clamp(a.X * b.X + a.Y * b.Y, -1, 1)) * 180 / Math.PI;

    private static double SignedCross(List<Pt> loop, int i)
    {
        var a = loop[(i - 1 + loop.Count) % loop.Count];
        var b = loop[i];
        var c = loop[(i + 1) % loop.Count];
        return (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
    }

    private static List<Return> FindReturns(List<Pt> loop)
    {
        int n = loop.Count;
        bool Gentle(int i) => SignedCross(loop, i) < 0 && Turn(loop, i) > 0.3 && Turn(loop, i) < 30;
        var result = new List<Return>();
        int start = Enumerable.Range(0, n).FirstOrDefault(i => !Gentle(i), -1);
        if (start < 0) return result;
        for (int step = 1; step <= n; step++)
        {
            int i = (start + step) % n;
            if (!Gentle(i)) continue;
            var run = new List<int> { i };
            while (Gentle((run[^1] + 1) % n) && run.Count < n) run.Add((run[^1] + 1) % n);
            step += run.Count - 1;
            double turn = run.Sum(k => Turn(loop, k));
            if (turn < 30 || run.Count < 3) continue;
            var pts = run.Select(k => loop[k]).ToList();
            double len = PolylineLength(pts);
            var m = PointAt(pts, len / 2);
            var before = PointAt(pts, len / 2 - Math.Min(0.5, len / 8));
            var after = PointAt(pts, len / 2 + Math.Min(0.5, len / 8));
            var t = Unit(new Pt(after.X - before.X, after.Y - before.Y));
            result.Add(new Return(run[0], run[^1], pts[0], pts[^1], m, new Pt(-t.Y, t.X)));
        }
        return result;
    }

    private static bool Crosses((Pt A, Pt B) s, (Pt A, Pt B) o)
    {
        static double Cr(Pt a, Pt b, Pt c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        double d1 = Cr(s.A, s.B, o.A), d2 = Cr(s.A, s.B, o.B), d3 = Cr(o.A, o.B, s.A), d4 = Cr(o.A, o.B, s.B);
        return d1 * d2 < -1e-12 && d3 * d4 < -1e-12;
    }

    private static (double Ti, double Tj)? RayMeet(Pt p, Pt d, Pt q, Pt e)
    {
        double den = d.X * e.Y - d.Y * e.X;
        if (Math.Abs(den) < 1e-9) return null;
        double ti = ((q.X - p.X) * e.Y - (q.Y - p.Y) * e.X) / den;
        double tj = ((q.X - p.X) * d.Y - (q.Y - p.Y) * d.X) / den;
        return (ti, tj);
    }

    private static int InsertOnBoundary(List<Pt> poly, Pt p)
    {
        for (int i = 0; i < poly.Count; i++)
            if (Distance(poly[i], p) < 1e-7) return i;
        int best = 0;
        for (int i = 0; i < poly.Count; i++)
            if (DistanceToSegment(p, poly[i], poly[(i + 1) % poly.Count]) < DistanceToSegment(p, poly[best], poly[(best + 1) % poly.Count])) best = i;
        poly.Insert(best + 1, p);
        return best + 1;
    }

    private static (List<Pt>, List<Pt>) SplitLoop(List<Pt> poly, Pt a, Pt b)
    {
        var pts = new List<Pt>(poly);
        InsertOnBoundary(pts, a);
        InsertOnBoundary(pts, b);
        int ia = Nearest(pts, a), ib = Nearest(pts, b);
        List<Pt> Walk(int from, int to)
        {
            var list = new List<Pt>();
            for (int i = from; ; i = (i + 1) % pts.Count)
            {
                list.Add(pts[i]);
                if (i == to) break;
            }
            return list;
        }
        return (Walk(ia, ib), Walk(ib, ia));
    }

    private static SlabJointResult? Junctions(SlabJointInputs input)
    {
        double u = input.UnitsPerMetre;
        var outer = Clean(input.Boundary.Select(p => new Pt(p.X / u, p.Y / u)).ToList());
        if (outer.Count < 3 || Math.Abs(Area(outer)) < 1e-6) return null;
        if (Area(outer) < 0) outer.Reverse();
        var returns = FindReturns(outer);
        if (returns.Count == 0) return null;
        var holes = input.Holes.Select(h => Clean(h.Select(p => new Pt(p.X / u, p.Y / u)).ToList()))
            .Where(h => h.Count >= 3 && Math.Abs(Area(h)) > 1e-6 && Inside(outer, Centroid(h))).ToList();
        var spacing = JointLayout.SpacingLimit(new JointInputs { ThicknessMm = input.ThicknessMm, Kind = input.Kind, MaxSpacingM = input.MaxSpacingM, Rules = input.Rules ?? new JointRuleSet() });
        if (!(spacing >= 0.5)) throw new ArgumentException("The joint spacing must be at least 0.5 m.");

        List<Pt> ring = outer!;
        bool OnReturn(Pt p) => returns.Any(r =>
        {
            if (Distance(p, r.S) < 0.05 || Distance(p, r.E) < 0.05) return false;
            for (int i = r.First; i != r.Last; i = (i + 1) % ring.Count)
                if (DistanceToSegment(p, ring[i], ring[(i + 1) % ring.Count]) < 0.02) return true;
            return false;
        });

        double EdgeAngle(Pt q, Pt dir)
        {
            foreach (var r in returns)
            {
                if (Distance(q, r.S) < 0.05)
                {
                    var b = ring[(r.First - 1 + ring.Count) % ring.Count];
                    return LineAngleDeg(dir, Unit(new Pt(r.S.X - b.X, r.S.Y - b.Y)));
                }
                if (Distance(q, r.E) < 0.05)
                {
                    var a = ring[(r.Last + 1) % ring.Count];
                    return LineAngleDeg(dir, Unit(new Pt(a.X - r.E.X, a.Y - r.E.Y)));
                }
            }
            return LineAngleDeg(dir, SmoothTangent(ring, q));
        }

        var chords = new List<(Pt A, Pt B)>();
        void TryChord(Pt from, Pt edgeDir)
        {
            var dir = new Pt(-edgeDir.Y, edgeDir.X);
            if (Cast(outer, from, dir) is not { } hit) return;
            if (EdgeAngle(hit.Q, dir) < SquareDeg || OnReturn(hit.Q)) return;
            if (chords.Any(c => (Distance(c.A, from) < 0.05 && Distance(c.B, hit.Q) < 0.05) || (Distance(c.B, from) < 0.05 && Distance(c.A, hit.Q) < 0.05))) return;
            if (chords.Any(c => Crosses(c, (from, hit.Q)))) return;
            chords.Add((from, hit.Q));
        }
        foreach (var r in returns)
        {
            var before = outer[(r.First - 1 + outer.Count) % outer.Count];
            var after = outer[(r.Last + 1) % outer.Count];
            TryChord(r.S, Unit(new Pt(r.S.X - before.X, r.S.Y - before.Y)));
            TryChord(r.E, Unit(new Pt(after.X - r.E.X, after.Y - r.E.Y)));
        }
        if (chords.Count == 0) return null;

        var joints = new List<(Pt A, Pt B)>();
        var warnings = new List<string>();
        var rayEnds = new List<(Return R, Pt End, int Partner)>();
        for (int i = 0; i < returns.Count; i++)
        {
            var r = returns[i];
            double stop = Cast(outer, r.M, r.N) is { } b ? Distance(r.M, b.Q) : double.MaxValue;
            int partner = -1;
            for (int j = 0; j < returns.Count; j++)
            {
                if (j == i || RayMeet(r.M, r.N, returns[j].M, returns[j].N) is not { } m) continue;
                if (m.Ti > 1e-6 && m.Tj > 1e-6 && m.Ti < stop - 1e-9) { stop = m.Ti; partner = j; }
            }
            if (stop == double.MaxValue) continue;
            var end = new Pt(r.M.X + r.N.X * stop, r.M.Y + r.N.Y * stop);
            if (partner < 0)
            {
                if (LineAngleDeg(r.N, SmoothTangent(outer, end)) < SquareDeg) continue;
            }
            else if (FullAngle(new Pt(-r.N.X, -r.N.Y), new Pt(-returns[partner].N.X, -returns[partner].N.Y)) < SquareDeg) continue;
            if (chords.Any(c => Crosses(c, (r.M, end)))) continue;
            rayEnds.Add((r, end, partner));
        }
        foreach (var (r, end, _) in rayEnds) joints.Add((r.M, end));

        int extras = 0;
        foreach (var group in rayEnds.Where(x => x.Partner >= 0).GroupBy(x => (Math.Round(x.End.X, 3), Math.Round(x.End.Y, 3))))
        {
            var rays = group.ToList();
            if (rays.Count != 2) continue;
            var apex = rays[0].End;
            var d1 = rays[0].R.N;
            var d2 = rays[1].R.N;
            var bis = Unit(new Pt(d1.X + d2.X, d1.Y + d2.Y));
            if (Cast(outer, apex, bis) is not { } first) continue;
            var tf = SmoothTangent(outer, first.Q);
            var f = new Pt(-tf.Y, tf.X);
            if (f.X * bis.X + f.Y * bis.Y < 0) f = new Pt(-f.X, -f.Y);
            if (Cast(outer, apex, f) is not { } flat) continue;
            if (LineAngleDeg(f, SmoothTangent(outer, flat.Q)) < SquareDeg) continue;
            if (FullAngle(f, new Pt(-d1.X, -d1.Y)) < SquareDeg || FullAngle(f, new Pt(-d2.X, -d2.Y)) < SquareDeg) continue;
            if (Distance(apex, flat.Q) > 2 * spacing || chords.Any(c => Crosses(c, (apex, flat.Q)))) continue;
            joints.Add((apex, flat.Q));

            var flatT = SmoothTangent(outer, flat.Q);
            foreach (var (ray, _, _) in rays)
            {
                double side = f.X * (ray.M.Y - apex.Y) - f.Y * (ray.M.X - apex.X);
                var ends = chords.SelectMany(c => new[] { c.A, c.B })
                    .Where(p => DistanceToSegment(p, new Pt(flat.Q.X - flatT.X * 1000, flat.Q.Y - flatT.Y * 1000), new Pt(flat.Q.X + flatT.X * 1000, flat.Q.Y + flatT.Y * 1000)) < 0.02)
                    .Where(p => Math.Sign(f.X * (p.Y - apex.Y) - f.Y * (p.X - apex.X)) == Math.Sign(side))
                    .OrderBy(p => Distance(p, flat.Q)).ToList();
                if (ends.Count == 0) continue;
                double run = Distance(ends[0], flat.Q);
                if (run <= 1.75 * spacing) continue;
                int k = (int)Math.Ceiling(run / spacing - 1e-9);
                var along = Unit(new Pt(ends[0].X - flat.Q.X, ends[0].Y - flat.Q.Y));
                var perp = new Pt(-ray.N.Y, ray.N.X);
                for (int j = 1; j < k; j++)
                {
                    var q = new Pt(flat.Q.X + along.X * run * j / k, flat.Q.Y + along.Y * run * j / k);
                    var foot = new Pt(apex.X + (ray.M.X - apex.X) * j / k, apex.Y + (ray.M.Y - apex.Y) * j / k);
                    var w = perp.X * (q.X - foot.X) + perp.Y * (q.Y - foot.Y) >= 0 ? perp : new Pt(-perp.X, -perp.Y);
                    if (RayMeet(q, new Pt(-f.X, -f.Y), foot, w) is not { } m || m.Ti < 0.05 || m.Tj < 0.05) continue;
                    var kink = new Pt(q.X - f.X * m.Ti, q.Y - f.Y * m.Ti);
                    if (!Inside(outer, kink) || FullAngle(f, new Pt(-w.X, -w.Y)) < SquareDeg) continue;
                    joints.Add((q, kink));
                    joints.Add((kink, foot));
                    extras++;
                }
            }
        }

        var regions = new List<List<Pt>> { outer };
        foreach (var c in chords)
        {
            var mid = new Pt((c.A.X + c.B.X) / 2, (c.A.Y + c.B.Y) / 2);
            int at = regions.FindIndex(r => Inside(r, mid));
            if (at < 0) continue;
            var (a, b) = SplitLoop(regions[at], c.A, c.B);
            if (a.Count < 3 || b.Count < 3) continue;
            regions[at] = a;
            regions.Add(b);
        }

        var allJoints = new List<JointSegment>();
        var longJoints = new List<IReadOnlyList<Pt>>();
        var check = new List<Pt>();
        int panels = 0, strips = 1, cornersJointed = 0;
        double largest = 0, worst = 1, sawcut = 0;
        IReadOnlyList<Pt>? guide = null;
        Pt W(Pt m) => new(m.X * u, m.Y * u);
        foreach (var region in regions)
        {
            bool junction = returns.Any(r => DistanceToPolyline(r.M, region.Append(region[0]).ToList()) < 0.02);
            if (junction) { panels += 1; continue; }
            var armHoles = input.Holes.Where(h => h.Count >= 3 && Inside(region, new Pt(h.Average(p => p.X) / u, h.Average(p => p.Y) / u))).ToList();
            var arm = Layout(input with { Boundary = region.Select(W).ToList(), Holes = armHoles, GuidePoint = null, NoJunctions = true });
            allJoints.AddRange(arm.Joints);
            longJoints.AddRange(arm.LongJoints ?? Array.Empty<IReadOnlyList<Pt>>());
            check.AddRange(arm.CheckPoints);
            warnings.AddRange(arm.Warnings);
            panels += arm.Panels;
            strips = Math.Max(strips, arm.Strips);
            largest = Math.Max(largest, arm.LargestPanelM);
            worst = Math.Max(worst, arm.WorstAspect);
            sawcut += arm.SawcutLengthM;
            cornersJointed += arm.CornersJointed;
            guide ??= arm.Guide;
        }
        foreach (var c in chords)
            foreach (var piece in AroundHoles(c.A, c.B, holes)) { allJoints.Add(new JointSegment(W(piece.A), W(piece.B))); sawcut += Distance(piece.A, piece.B); }
        foreach (var j in joints)
            foreach (var piece in AroundHoles(j.A, j.B, holes)) { allJoints.Add(new JointSegment(W(piece.A), W(piece.B))); sawcut += Distance(piece.A, piece.B); }
        panels += joints.Count;

        warnings.Insert(0, $"Junction: {returns.Count} curved return(s); slab split into arms at the ends of the curves, with joints off each curve meeting in a triangle"
                           + (extras > 0 ? $" ({extras} extra joint(s) in the bigger junction panels)." : "."));
        int single = returns.Count - rayEnds.Count;
        if (single > 0)
            warnings.Add($"{single} curve(s) have no joint off them (a joint there would meet the edge or another joint under 90°): check the panel at that curve.");

        double perimeter = Perimeter(outer) + holes.Sum(Perimeter);
        double area = Math.Abs(Area(outer)) - holes.Sum(h => Math.Abs(Area(h)));
        int depth = (int)(Math.Ceiling(input.ThicknessMm / 4 / 5 - 1e-9) * 5);
        return new SlabJointResult(allJoints, check, warnings.Distinct().ToList(), spacing, 0, panels, sawcut, perimeter, area,
            Math.Max(largest, spacing), worst, depth, cornersJointed, null, guide, longJoints, strips);
    }
}
