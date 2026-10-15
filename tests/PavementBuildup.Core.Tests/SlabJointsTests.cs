using PavementBuildup.Core.Design;

namespace PavementBuildup.Core.Tests;

public class SlabJointsTests
{
    private static Pt P(double x, double y) => new(x, y);

    private static SlabJointResult Run(Pt[] boundary, double thickness = 200, JointRuleSet? rules = null, double units = 1, params Pt[][] holes) =>
        SlabJoints.Layout(new SlabJointInputs
        {
            Boundary = boundary, Holes = holes, ThicknessMm = thickness, Rules = rules, UnitsPerMetre = units, AngleRad = 0, SetOut = JointSetOut.LongestEdge,
        });

    private static bool OnLineX(SlabJointResult r, double x) => r.Joints.Any(j => Math.Abs(j.A.X - x) < 1e-6 && Math.Abs(j.B.X - x) < 1e-6);
    private static bool OnLineY(SlabJointResult r, double y) => r.Joints.Any(j => Math.Abs(j.A.Y - y) < 1e-6 && Math.Abs(j.B.Y - y) < 1e-6);

    [Fact]
    public void A_rectangle_matches_the_rectangle_helper()
    {
        var r = Run(new[] { P(0, 0), P(30, 0), P(30, 20), P(0, 20) });
        var plan = JointLayout.Plan(new JointInputs { LengthM = 30, WidthM = 20, ThicknessMm = 200 });
        Assert.Equal(plan.Panels, r.Panels);
        Assert.Equal(plan.SawcutLengthM, r.SawcutLengthM, 6);
        Assert.Equal(100, r.PerimeterM, 6);
        Assert.Empty(r.CheckPoints);
        Assert.Equal(50, r.SawcutDepthMm);
    }

    [Fact]
    public void An_L_shape_gets_joints_straight_out_of_its_inside_corner()
    {
        var r = Run(new[] { P(0, 0), P(30, 0), P(30, 10), P(17, 10), P(17, 20), P(0, 20) });
        Assert.True(OnLineX(r, 17));
        Assert.True(OnLineY(r, 10));
        Assert.Equal(1, r.CornersJointed);
        Assert.Empty(r.CheckPoints);
        Assert.True(r.LargestPanelM <= r.SpacingM + 1e-9);
        Assert.All(r.Joints, j => Assert.True(Inside(j, 0, 30, 0, 20)));
        Assert.DoesNotContain(r.Joints, j => j.A.X > 17.001 && j.A.Y > 10.001 && j.B.Y > 10.001);
    }

    [Fact]
    public void Pits_get_joints_off_their_corners_and_no_joint_crosses_them()
    {
        var pit = new[] { P(12, 8), P(13, 8), P(13, 9), P(12, 9) };
        var r = Run(new[] { P(0, 0), P(30, 0), P(30, 20), P(0, 20) }, holes: pit);
        Assert.True(OnLineX(r, 12) || OnLineX(r, 13));
        Assert.True(OnLineY(r, 8) || OnLineY(r, 9));
        foreach (var j in r.Joints)
        {
            double mx = (j.A.X + j.B.X) / 2, my = (j.A.Y + j.B.Y) / 2;
            bool vertical = Math.Abs(j.A.X - j.B.X) < 1e-9;
            if (vertical && j.A.X > 12 && j.A.X < 13) Assert.False(Math.Min(j.A.Y, j.B.Y) < 8.5 && Math.Max(j.A.Y, j.B.Y) > 8.5);
            if (!vertical && j.A.Y > 8 && j.A.Y < 9) Assert.False(Math.Min(j.A.X, j.B.X) < 12.5 && Math.Max(j.A.X, j.B.X) > 12.5);
        }
        Assert.Equal(600 - 1, r.AreaM2, 6);
        Assert.Equal(100 + 4, r.PerimeterM, 6);
    }

    [Fact]
    public void A_corner_too_close_to_another_joint_is_circled_for_trimmer_bars()
    {
        var one = Run(new[] { P(0, 0), P(30, 0), P(30, 10), P(29.8, 10), P(29.8, 20), P(0, 20) });
        Assert.True(OnLineY(one, 10));
        Assert.Empty(one.CheckPoints);
        var r = Run(new[] { P(0, 0), P(30, 0), P(30, 19.8), P(29.8, 19.8), P(29.8, 20), P(0, 20) });
        Assert.Single(r.CheckPoints);
        Assert.Contains(r.Warnings, w => w.Contains("trimmer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_skewed_edge_flags_acute_joint_ends()
    {
        var r = Run(new[] { P(0, 0), P(30, 0), P(40, 20), P(0, 20) });
        Assert.NotEmpty(r.CheckPoints);
        Assert.All(r.CheckPoints, c => Assert.True(Math.Abs((c.X - 30) * 20 - (c.Y - 0) * 10) < 1e-3));
        Assert.Contains(r.Warnings, w => w.Contains("60"));
    }

    [Fact]
    public void Drawing_units_are_respected()
    {
        var m = Run(new[] { P(0, 0), P(30, 0), P(30, 20), P(0, 20) });
        var mm = Run(new[] { P(0, 0), P(30000, 0), P(30000, 20000), P(0, 20000) }, units: 1000);
        Assert.Equal(m.Panels, mm.Panels);
        Assert.Equal(m.SawcutLengthM, mm.SawcutLengthM, 6);
        Assert.Equal(m.Joints[0].A.X * 1000, mm.Joints[0].A.X, 3);
    }

    [Fact]
    public void Council_rules_change_the_spacing()
    {
        var council = JointRulesFile.CreateDefault().Find("Example council (edit me)");
        var r = Run(new[] { P(0, 0), P(30, 0), P(30, 20), P(0, 20) }, rules: council);
        Assert.Equal(4.5, r.SpacingM);
        Assert.True(r.LargestPanelM <= 4.5 + 1e-9);
        Assert.True(r.WorstAspect <= 1.25 + 1e-9);
    }

    [Fact]
    public void A_rotated_slab_is_laid_out_square_to_its_long_edge()
    {
        double a = 30 * Math.PI / 180;
        Pt R(double x, double y) => new(100 + x * Math.Cos(a) - y * Math.Sin(a), 50 + x * Math.Sin(a) + y * Math.Cos(a));
        var r = SlabJoints.Layout(new SlabJointInputs { Boundary = new[] { R(0, 0), R(30, 0), R(30, 20), R(0, 20) }, SetOut = JointSetOut.LongestEdge });
        Assert.Equal(a, r.AngleRad, 6);
        Assert.Equal(24, r.Panels);
        Assert.Empty(r.CheckPoints);
    }

    private static readonly Pt[] Skewed = { new(0, 0), new(30, 0), new(30, 15), new(8, 15) };

    private static SlabJointResult FromCorner(Pt[] boundary, Pt corner, bool exact = false) =>
        SlabJoints.Layout(new SlabJointInputs { Boundary = boundary, SetOut = JointSetOut.FromCorner, Corner = corner, ExactSteps = exact });

    private static bool Parallel(JointSegment j, double dx, double dy)
    {
        double jx = j.B.X - j.A.X, jy = j.B.Y - j.A.Y;
        return Math.Abs(jx * dy - jy * dx) / (Math.Sqrt(jx * jx + jy * jy) * Math.Sqrt(dx * dx + dy * dy)) < 1e-6;
    }

    [Fact]
    public void Setting_out_from_a_corner_runs_joints_parallel_to_its_two_edges()
    {
        var r = FromCorner(Skewed, new Pt(0.3, 0.2));
        Assert.Equal(new Pt(0, 0), r.SetOutCorner);
        Assert.All(r.Joints, j => Assert.True(Parallel(j, 1, 0) || Parallel(j, 8, 15)));
        Assert.Contains(r.Joints, j => Parallel(j, 8, 15));
        Assert.DoesNotContain(r.CheckPoints, c => Math.Abs(c.X * 15 - c.Y * 8) < 1e-3);
        var grid = Run(Skewed);
        Assert.Contains(grid.CheckPoints, c => Math.Abs(c.X * 15 - c.Y * 8) < 1e-3);
    }

    [Fact]
    public void Exact_circle_steps_are_the_spacing_from_the_corner()
    {
        var box = new[] { P(0, 0), P(23, 0), P(23, 10), P(0, 10) };
        var exact = FromCorner(box, P(0, 0), exact: true);
        foreach (var x in new[] { 5.0, 10, 15, 20 }) Assert.True(OnLineX(exact, x), $"no joint at {x}");
        var equal = FromCorner(box, P(0, 0));
        Assert.True(OnLineX(equal, 4.6));
        Assert.False(OnLineX(equal, 5));
    }

    [Fact]
    public void The_corner_is_found_automatically_and_an_inside_corner_still_gets_its_joint()
    {
        var l = new[] { P(0, 0), P(30, 0), P(30, 10), P(17, 10), P(17, 22), P(0, 22) };
        var r = SlabJoints.Layout(new SlabJointInputs { Boundary = l, SetOut = JointSetOut.FromCorner });
        Assert.NotNull(r.SetOutCorner);
        Assert.Equal(1, r.CornersJointed);
        Assert.Empty(r.CheckPoints);
        var skew = SlabJoints.Layout(new SlabJointInputs { Boundary = Skewed, SetOut = JointSetOut.FromCorner });
        Assert.True(skew.CheckPoints.Count <= FromCorner(Skewed, new Pt(30, 0)).CheckPoints.Count);
    }

    private static SlabJointResult Across(IReadOnlyList<Pt> boundary, double spacing, Pt? guide = null, bool exact = false) =>
        SlabJoints.Layout(new SlabJointInputs { Boundary = boundary, MaxSpacingM = spacing, GuidePoint = guide, ExactSteps = exact });

    private static double JointAngleTo(JointSegment j, double dx, double dy)
    {
        double jx = j.B.X - j.A.X, jy = j.B.Y - j.A.Y;
        double c = Math.Abs(jx * dx + jy * dy) / (Math.Sqrt(jx * jx + jy * jy) * Math.Sqrt(dx * dx + dy * dy));
        return Math.Acos(Math.Min(1, c)) * 180 / Math.PI;
    }

    private static List<Pt> Ring(double r1, double r2, double fromDeg, double toDeg)
    {
        var pts = new List<Pt>();
        for (int i = 0; i <= 40; i++) { double a = (fromDeg + (toDeg - fromDeg) * i / 40) * Math.PI / 180; pts.Add(new Pt(r2 * Math.Cos(a), r2 * Math.Sin(a))); }
        for (int i = 40; i >= 0; i--) { double a = (fromDeg + (toDeg - fromDeg) * i / 40) * Math.PI / 180; pts.Add(new Pt(r1 * Math.Cos(a), r1 * Math.Sin(a))); }
        return pts;
    }

    [Fact]
    public void Across_the_width_a_straight_path_gets_square_joints_only_one_way()
    {
        var r = Across(new[] { P(0, 0), P(20, 0), P(20, 1.5), P(0, 1.5) }, 1.5);
        Assert.Equal(13, r.Joints.Count);
        Assert.All(r.Joints, j => Assert.Equal(90, JointAngleTo(j, 1, 0), 6));
        Assert.All(r.Joints, j => Assert.Equal(1.5, Math.Sqrt(Math.Pow(j.B.X - j.A.X, 2) + Math.Pow(j.B.Y - j.A.Y, 2)), 6));
        Assert.Empty(r.CheckPoints);
        Assert.NotNull(r.Guide);
    }

    [Fact]
    public void Across_a_curved_path_the_joints_are_radial_so_never_under_90_degrees()
    {
        var r = Across(Ring(10, 12, 0, 90), 2);
        Assert.NotEmpty(r.Joints);
        foreach (var j in r.Joints)
        {
            var mid = new Pt((j.A.X + j.B.X) / 2, (j.A.Y + j.B.Y) / 2);
            Assert.True(JointAngleTo(j, mid.X, mid.Y) < 1.5, "joint is not radial");
            Assert.InRange(Math.Sqrt(Math.Pow(j.B.X - j.A.X, 2) + Math.Pow(j.B.Y - j.A.Y, 2)), 1.95, 2.05);
        }
        Assert.Empty(r.CheckPoints);
    }

    [Fact]
    public void Across_a_tapering_slab_each_joint_is_a_dog_leg_square_to_both_edges()
    {
        var r = Across(new[] { P(0, 0), P(20, 0), P(20, 3), P(0, 1.5) }, 2);
        Assert.Empty(r.CheckPoints);
        Assert.Contains(r.Warnings, w => w.Contains("dog-leg"));
        foreach (var j in r.Joints)
            foreach (var end in new[] { j.A, j.B })
            {
                if (Math.Abs(end.Y) < 1e-6) Assert.Equal(90, JointAngleTo(j, 1, 0), 3);
                else if (Math.Abs(end.Y - (1.5 + end.X * 1.5 / 20)) < 1e-6) Assert.Equal(90, JointAngleTo(j, 20, 1.5), 3);
            }
        Assert.True(r.Joints.Count >= 18 && r.Joints.Count % 2 == 0);
    }

    [Fact]
    public void Across_the_width_the_side_you_click_is_the_one_spaced_along_and_exact_steps_work()
    {
        var box = new[] { P(0, 0), P(23, 0), P(23, 4), P(0, 4) };
        var exact = Across(box, 5, guide: P(10, -0.1), exact: true);
        var xs = exact.Joints.Select(j => Math.Round(j.A.X, 6)).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 5.0, 10, 15, 20 }, xs);
        var shortSide = Across(box, 5, guide: P(23.1, 2));
        Assert.All(shortSide.Joints, j => Assert.Equal(0, JointAngleTo(j, 1, 0), 6));
    }

    [Fact]
    public void Across_the_width_an_inside_corner_off_a_joint_is_circled()
    {
        var bend = new[] { P(0, 0), P(20, 0), P(20, 20), P(18.5, 20), P(18.5, 1.5), P(0, 1.5) };
        var r = Across(bend, 1.5);
        Assert.Contains(r.Warnings, w => w.Contains("inside corner"));
    }

    private static SlabJointResult Lengthways(IReadOnlyList<Pt> boundary, double spacing, double lengthways = 0, params IReadOnlyList<Pt>[] holes) =>
        SlabJoints.Layout(new SlabJointInputs { Boundary = boundary, MaxSpacingM = spacing, Lengthways = true, LengthwaysSpacingM = lengthways, Holes = holes });

    [Fact]
    public void Lengthways_joints_use_the_across_spacing_when_none_is_given()
    {
        var r = Lengthways(new[] { P(0, 0), P(20, 0), P(20, 6), P(0, 6) }, 4);
        Assert.Equal(4, r.Joints.Count);
        Assert.Equal(2, r.Strips);
        var line = Assert.Single(r.LongJoints!);
        Assert.All(line, p => Assert.Equal(3, p.Y, 6));
        Assert.Equal(0, line.Min(p => p.X), 3);
        Assert.Equal(20, line.Max(p => p.X), 3);
        Assert.Equal(10, r.Panels);
    }

    [Fact]
    public void Lengthways_joints_take_their_own_spacing_and_split_into_equal_strips()
    {
        var r = Lengthways(new[] { P(0, 0), P(20, 0), P(20, 6), P(0, 6) }, 4, lengthways: 1.5);
        Assert.Equal(4, r.Strips);
        var ys = r.LongJoints!.Select(l => Math.Round(l[0].Y, 6)).OrderBy(y => y).ToList();
        Assert.Equal(new[] { 1.5, 3, 4.5 }, ys);
        var none = SlabJoints.Layout(new SlabJointInputs { Boundary = new[] { P(0, 0), P(20, 0), P(20, 6), P(0, 6) }, MaxSpacingM = 4 });
        Assert.Empty(none.LongJoints!);
    }

    [Fact]
    public void Lengthways_joints_follow_a_curve_as_concentric_arcs()
    {
        var r = Lengthways(Ring(10, 16, 0, 90), 2);
        Assert.Equal(3, r.Strips);
        Assert.Equal(2, r.LongJoints!.Count);
        foreach (var line in r.LongJoints!)
        {
            double radius = Math.Sqrt(line[line.Count / 2].X * line[line.Count / 2].X + line[line.Count / 2].Y * line[line.Count / 2].Y);
            Assert.True(Math.Abs(radius - 12) < 0.05 || Math.Abs(radius - 14) < 0.05, $"radius {radius}");
            Assert.All(line, p => Assert.InRange(Math.Sqrt(p.X * p.X + p.Y * p.Y), radius - 0.05, radius + 0.05));
        }
    }

    [Fact]
    public void A_pit_splits_a_lengthways_joint()
    {
        var pit = new[] { P(9, 2.5), P(11, 2.5), P(11, 3.5), P(9, 3.5) };
        var r = Lengthways(new[] { P(0, 0), P(20, 0), P(20, 6), P(0, 6) }, 4, 0, pit);
        Assert.Equal(2, r.LongJoints!.Count);
        Assert.All(r.LongJoints!, l => Assert.DoesNotContain(l, p => p.X > 9.001 && p.X < 10.999));
    }

    private static double MinAngleOf(SlabJointResult r, IReadOnlyList<Pt> boundary)
    {
        var segs = r.Joints.Select(j => (j.A, j.B)).ToList();
        foreach (var l in r.LongJoints ?? Array.Empty<IReadOnlyList<Pt>>())
            for (int i = 0; i + 1 < l.Count; i++) segs.Add((l[i], l[i + 1]));
        double Ang(Pt a1, Pt a2, Pt b1, Pt b2)
        {
            double ax = a2.X - a1.X, ay = a2.Y - a1.Y, bx = b2.X - b1.X, by = b2.Y - b1.Y;
            double c = Math.Abs(ax * bx + ay * by) / (Math.Sqrt(ax * ax + ay * ay) * Math.Sqrt(bx * bx + by * by));
            return Math.Acos(Math.Min(1, c)) * 180 / Math.PI;
        }
        double Dist(Pt p, Pt a, Pt b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len = dx * dx + dy * dy;
            double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len, 0, 1);
            return Math.Sqrt(Math.Pow(p.X - a.X - t * dx, 2) + Math.Pow(p.Y - a.Y - t * dy, 2));
        }
        bool Same(Pt a, Pt b) => Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;
        double worst = 180;
        foreach (var (a, b) in segs)
        {
            for (int i = 0; i < boundary.Count; i++)
            {
                var p = boundary[i];
                var q = boundary[(i + 1) % boundary.Count];
                foreach (var end in new[] { a, b })
                    if (Dist(end, p, q) < 1e-6 && !Same(end, p) && !Same(end, q)) worst = Math.Min(worst, Ang(a, b, p, q));
            }
            foreach (var (c, d) in segs)
            {
                if ((a, b) == (c, d) || Same(a, c) || Same(a, d) || Same(b, c) || Same(b, d)) continue;
                bool touches = Dist(a, c, d) < 1e-6 || Dist(b, c, d) < 1e-6 || Dist(c, a, b) < 1e-6 || Dist(d, a, b) < 1e-6;
                double d1 = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X), d2 = (b.X - a.X) * (d.Y - a.Y) - (b.Y - a.Y) * (d.X - a.X);
                double d3 = (d.X - c.X) * (a.Y - c.Y) - (d.Y - c.Y) * (a.X - c.X), d4 = (d.X - c.X) * (b.Y - c.Y) - (d.Y - c.Y) * (b.X - c.X);
                bool crosses = d1 * d2 < 0 && d3 * d4 < 0;
                if (touches || crosses) worst = Math.Min(worst, Ang(a, b, c, d));
            }
        }
        return worst;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Across_the_width_no_joint_meets_an_edge_or_a_joint_under_90_degrees(bool lengthways)
    {
        var shapes = new[]
        {
            new[] { P(0, 0), P(20, 0), P(20, 3), P(0, 1.5) },
            new[] { P(0, 0), P(30, 0), P(26, 8), P(4, 8) },
            new[] { P(0, 0), P(30, 0), P(30, 12), P(0, 8) },
            new[] { P(0, 0), P(20, 0), P(20, 6), P(0, 6) },
        };
        foreach (var shape in shapes)
        {
            var r = SlabJoints.Layout(new SlabJointInputs { Boundary = shape, MaxSpacingM = 3, Lengthways = lengthways });
            Assert.NotEmpty(r.Joints);
            Assert.True(MinAngleOf(r, shape) >= 89.5, $"angle {MinAngleOf(r, shape):0.0} on shape starting {shape[1]}");
        }
    }

    [Fact]
    public void A_boundary_with_no_area_is_refused() =>
        Assert.Throws<ArgumentException>(() => Run(new[] { P(0, 0), P(10, 0), P(20, 0) }));

    private static bool Inside(JointSegment j, double x0, double x1, double y0, double y1) =>
        new[] { j.A, j.B }.All(p => p.X >= x0 - 1e-6 && p.X <= x1 + 1e-6 && p.Y >= y0 - 1e-6 && p.Y <= y1 + 1e-6);
}
