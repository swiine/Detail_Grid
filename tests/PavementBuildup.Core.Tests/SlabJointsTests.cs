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

    [Fact]
    public void A_boundary_with_no_area_is_refused() =>
        Assert.Throws<ArgumentException>(() => Run(new[] { P(0, 0), P(10, 0), P(20, 0) }));

    private static bool Inside(JointSegment j, double x0, double x1, double y0, double y1) =>
        new[] { j.A, j.B }.All(p => p.X >= x0 - 1e-6 && p.X <= x1 + 1e-6 && p.Y >= y0 - 1e-6 && p.Y <= y1 + 1e-6);
}
