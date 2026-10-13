using ProfileStacker.Core;

namespace ProfileStacker.Core.Tests;

public class StackPlannerTests
{
    // A metre drawing on a millimetre sheet at 1:500 (2xp): an 800 x 250 mm viewport shows 400 x 125 m.
    private static ViewportSlot Slot(double cy, double w = 800, double h = 250, double cx = 420, double scale = 2) =>
        new(cx, cy, w, h, scale);

    // Axes 20 m left, 5 m right, title 5 m above, bands 30 m below; vertical exaggeration 10.
    private static readonly FrameGeometry Frame = new(20, 5, 5, 30, 1, 10);

    private static StackerSettings Settings(double start = 0, double end = 1000) => new()
    {
        StartStation = start,
        EndStation = end,
        PaperClearance = 0,
        StationRounding = 10,
        DatumRounding = 1,
        ClearanceBelow = 0,
        ClearanceAbove = 0,
    };

    private static ProfileSamples Flat(double elevation, double from = -100, double to = 5000)
    {
        var samples = new ProfileSamples();
        samples.Add(new[] { (from, elevation), (to, elevation) });
        return samples;
    }

    private static IReadOnlyList<ViewportSlot> TwoStacked() => StackPlanner.OrderSlots(new[] { Slot(150), Slot(450) });

    [Fact]
    public void Capacity_takes_off_axes_bands_and_clearance_then_undoes_exaggeration()
    {
        var cap = StackPlanner.Capacity(Slot(0), Frame, paperClearance: 0);
        Assert.Equal(375, cap.StationLength, 6);
        Assert.Equal(9, cap.ElevationRange, 6);

        // 5 mm of paper clearance each side is 2.5 m each side at 2xp.
        var tight = StackPlanner.Capacity(Slot(0), Frame, paperClearance: 5);
        Assert.Equal(370, tight.StationLength, 6);
        Assert.Equal(8.5, tight.ElevationRange, 6);
    }

    [Fact]
    public void Slots_are_ordered_top_to_bottom_then_left_to_right()
    {
        var ordered = StackPlanner.OrderSlots(new[]
        {
            Slot(cy: 100, cx: 600, w: 300),
            Slot(cy: 500),
            Slot(cy: 110, cx: 200, w: 300),
        });
        Assert.Equal(new[] { 500.0, 110, 100 }, ordered.Select(s => s.CenterY));
        Assert.Equal(new[] { 0, 1, 2 }, ordered.Select(s => s.Index));
    }

    [Fact]
    public void Long_view_is_cut_into_rounded_viewport_lengths_two_per_sheet()
    {
        var plan = StackPlanner.Plan(TwoStacked(), Frame, Settings(), Flat(50));

        Assert.Equal(new[] { (0.0, 370.0), (370.0, 740.0), (740.0, 1000.0) }, plan.Views.Select(v => (v.StationStart, v.StationEnd)));
        Assert.Equal(new[] { 0, 0, 1 }, plan.Views.Select(v => v.Sheet));
        Assert.Equal(new[] { 0, 1, 0 }, plan.Views.Select(v => v.Slot.Index));
        Assert.Equal(2, plan.SheetCount);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Start_is_rounded_down_but_not_before_the_alignment()
    {
        var s = Settings(start: 123.4, end: 600);
        var plan = StackPlanner.Plan(TwoStacked(), Frame, s, Flat(50));
        Assert.Equal(120, plan.Views[0].StationStart);

        s.MinStation = 121;
        plan = StackPlanner.Plan(TwoStacked(), Frame, s, Flat(50));
        Assert.Equal(121, plan.Views[0].StationStart);
    }

    [Fact]
    public void Overlap_repeats_the_end_of_each_view()
    {
        var s = Settings();
        s.Overlap = 20;
        var plan = StackPlanner.Plan(TwoStacked(), Frame, s, Flat(50));
        Assert.Equal(new[] { 0.0, 350, 700 }, plan.Views.Select(v => v.StationStart));
        Assert.All(plan.Views.Take(2), v => Assert.Equal(370, v.Length, 6));
    }

    [Fact]
    public void Untrimmed_last_view_keeps_its_full_length_up_to_the_alignment_end()
    {
        var s = Settings();
        s.TrimLastView = false;
        Assert.Equal(1110, StackPlanner.Plan(TwoStacked(), Frame, s, Flat(50)).Views[^1].StationEnd);

        s.MaxStation = 1050;
        Assert.Equal(1050, StackPlanner.Plan(TwoStacked(), Frame, s, Flat(50)).Views[^1].StationEnd);
    }

    [Fact]
    public void Even_mode_shares_the_length_between_the_same_number_of_views()
    {
        var s = Settings();
        s.LengthMode = LengthMode.Even;
        var plan = StackPlanner.Plan(TwoStacked(), Frame, s, Flat(50));
        // 1000 over 3 views is 333.3, rounded up to 340 so three views always reach the end.
        Assert.Equal(new[] { (0.0, 340.0), (340.0, 680.0), (680.0, 1000.0) }, plan.Views.Select(v => (v.StationStart, v.StationEnd)));
    }

    [Fact]
    public void Viewports_of_different_sizes_get_different_lengths()
    {
        var slots = StackPlanner.OrderSlots(new[] { Slot(450), Slot(150, w: 400) });
        var plan = StackPlanner.Plan(slots, Frame, Settings(), Flat(50));
        // Top: 375 → 370. Bottom: 200 m - 25 m of axes = 175 → 170.
        Assert.Equal(new[] { 370.0, 170, 370, 90 }, plan.Views.Select(v => Math.Round(v.Length, 6)));
    }

    [Fact]
    public void Scale_override_replaces_the_template_scale()
    {
        var s = Settings();
        s.ScaleOverride = 1; // 1:1000: 800 mm shows 800 m
        var plan = StackPlanner.Plan(TwoStacked(), Frame, s, Flat(50));
        Assert.Equal(new[] { (0.0, 770.0), (770.0, 1000.0) }, plan.Views.Select(v => (v.StationStart, v.StationEnd)));
    }

    [Fact]
    public void Datum_follows_the_profile_in_each_view()
    {
        var samples = new ProfileSamples();
        samples.Add(new[] { (0.0, 10.2), (370.0, 12.0), (740.0, 30.5), (1000.0, 31.0) });
        var plan = StackPlanner.Plan(TwoStacked(), Frame, Settings(), samples);

        Assert.Equal(10, plan.Views[0].ElevationMin);
        Assert.Equal(19, plan.Views[0].ElevationMax); // the whole 9 m the viewport holds
        Assert.False(plan.Views[1].FitsHeight); // 12 → 30.5 is more than 9 m
        Assert.Equal(12, plan.Views[1].ElevationMin);
        Assert.Equal(31, plan.Views[1].ElevationMax);
        Assert.Equal(30, plan.Views[2].ElevationMin);
        Assert.Contains(plan.Warnings, w => w.StartsWith("View 2:"));
    }

    [Fact]
    public void Views_without_profile_data_carry_the_datum_over()
    {
        var samples = new ProfileSamples();
        samples.Add(new[] { (0.0, 40.0), (300.0, 41.0) });
        var plan = StackPlanner.Plan(TwoStacked(), Frame, Settings(), samples);
        Assert.True(plan.Views[0].HasProfileData);
        Assert.False(plan.Views[2].HasProfileData);
        Assert.Equal(plan.Views[0].ElevationMin, plan.Views[2].ElevationMin);
    }

    [Fact]
    public void Viewport_too_small_for_the_bands_is_reported()
    {
        var tiny = StackPlanner.OrderSlots(new[] { Slot(100, w: 40) });
        var ex = Assert.Throws<PlanException>(() => StackPlanner.Plan(tiny, Frame, Settings(), Flat(50)));
        Assert.Contains("too small", ex.Message);
    }

    [Fact]
    public void No_viewports_is_reported()
    {
        Assert.Throws<PlanException>(() => StackPlanner.Plan(Array.Empty<ViewportSlot>(), Frame, Settings(), Flat(50)));
    }

    [Theory]
    [InlineData(10.2, 12.7, DatumMode.LowPoint, 10, 19)]
    [InlineData(10.2, 12.7, DatumMode.Centred, 7, 16)]
    [InlineData(10.0, 19.0, DatumMode.LowPoint, 10, 19)]
    [InlineData(10.5, 19.2, DatumMode.LowPoint, 10.5, 19.5)] // no round datum fits: uses the low point
    public void FitDatum_keeps_round_values_and_the_whole_height(double low, double high, DatumMode mode, double min, double max)
    {
        var s = Settings();
        s.DatumMode = mode;
        var (fmin, fmax, fits) = StackPlanner.FitDatum(low, high, 9, s);
        Assert.True(fits);
        Assert.Equal(min, fmin, 6);
        Assert.Equal(max, fmax, 6);
    }

    [Fact]
    public void FitDatum_adds_the_clearances()
    {
        var s = Settings();
        s.ClearanceBelow = 2;
        var (min, _, _) = StackPlanner.FitDatum(10.2, 12, 9, s);
        Assert.Equal(8, min);
    }
}
