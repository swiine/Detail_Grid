using PavementBuildup.Core.Design;

namespace PavementBuildup.Core.Tests;

public class SlabJointsTests
{
    private static Pt P(double x, double y) => new(x, y);

    private static SlabJointResult Run(Pt[] boundary, double thickness = 200, JointRuleSet? rules = null, double units = 1, params Pt[][] holes) =>
        SlabJoints.Layout(new SlabJointInputs
        {
            Boundary = boundary, Holes = holes, ThicknessMm = thickness, Rules = rules, UnitsPerMetre = units, AngleRad = 0,
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
        var r = SlabJoints.Layout(new SlabJointInputs { Boundary = new[] { R(0, 0), R(30, 0), R(30, 20), R(0, 20) } });
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

    [Fact]
    public void A_boundary_with_no_area_is_refused() =>
        Assert.Throws<ArgumentException>(() => Run(new[] { P(0, 0), P(10, 0), P(20, 0) }));

    private static bool Inside(JointSegment j, double x0, double x1, double y0, double y1) =>
        new[] { j.A, j.B }.All(p => p.X >= x0 - 1e-6 && p.X <= x1 + 1e-6 && p.Y >= y0 - 1e-6 && p.Y <= y1 + 1e-6);
}
