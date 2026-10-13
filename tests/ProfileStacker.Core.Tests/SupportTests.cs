using ProfileStacker.Core;

namespace ProfileStacker.Core.Tests;

public class ProfileSamplesTests
{
    [Fact]
    public void Range_interpolates_at_the_ends_and_includes_points_inside()
    {
        var samples = new ProfileSamples();
        samples.Add(new[] { (0.0, 10.0), (100.0, 20.0), (200.0, 5.0) });
        var range = samples.Range(50, 150)!.Value;
        Assert.Equal(12.5, range.Low, 6); // 150 is half way from 20 down to 5
        Assert.Equal(20, range.High, 6);
    }

    [Fact]
    public void Range_covers_every_profile_and_ignores_ones_outside()
    {
        var samples = new ProfileSamples();
        samples.Add(new[] { (0.0, 10.0), (100.0, 10.0) });
        samples.Add(new[] { (50.0, 3.0), (80.0, 4.0) });
        samples.Add(new[] { (500.0, -40.0), (600.0, -40.0) });
        Assert.Equal((3.0, 10.0), samples.Range(0, 100));
        Assert.Null(samples.Range(200, 300));
    }

    [Fact]
    public void Unsorted_points_are_sorted()
    {
        var samples = new ProfileSamples();
        samples.Add(new[] { (100.0, 20.0), (0.0, 10.0) });
        Assert.Equal(15, samples.Range(50, 50)!.Value.Low, 6);
    }
}

public class NamingTests
{
    [Theory]
    [InlineData(0, 3, 0, "0+000")]
    [InlineData(1250, 3, 0, "1+250")]
    [InlineData(1250.456, 3, 2, "1+250.46")]
    [InlineData(1250, 2, 0, "12+50")]
    [InlineData(999.6, 3, 0, "1+000")]
    [InlineData(-50, 3, 0, "-0+050")]
    public void Station_text(double station, int digits, int decimals, string expected) =>
        Assert.Equal(expected, Naming.Station(station, digits, decimals));

    [Theory]
    [InlineData("1250", 1250)]
    [InlineData("1+250", 1250)]
    [InlineData("1+250.5", 1250.5)]
    [InlineData("12+50", 1250)]
    [InlineData("-0+050", -50)]
    [InlineData(" 0+000 ", 0)]
    public void Stations_are_read_either_way(string text, double expected)
    {
        Assert.True(Naming.TryParseStation(text, out double station));
        Assert.Equal(expected, station, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1+")]
    [InlineData("1+2+3")]
    public void Bad_stations_are_rejected(string text) => Assert.False(Naming.TryParseStation(text, out _));

    [Fact]
    public void Layout_name_fills_tokens_and_drops_invalid_characters()
    {
        Assert.Equal("PROFILE 03 (0+740 to 1+000)", Naming.Layout("PROFILE <N> (<START> to <END>)", 3, 12, "0+740", "1+000"));
        Assert.Equal("A B", Naming.Layout("A:<n>B", 1, 1, "", "").Replace("01", ""));
        Assert.Equal("Sheet 007", Naming.Layout("Sheet <N>", 7, 120, "", ""));
    }

    [Fact]
    public void View_name_fills_tokens()
    {
        Assert.Equal("PV - Main St - 02 (sheet 01)", Naming.View("<VIEW> - <N> (sheet <S>)", "PV - Main St", 2, 5, 1, 3, "", ""));
    }

    [Fact]
    public void Unique_adds_a_counter()
    {
        var taken = new HashSet<string> { "A", "A (2)" };
        Assert.Equal("A (3)", Naming.Unique("A", taken.Contains));
        Assert.Equal("B", Naming.Unique("B", taken.Contains));
    }
}

public class ModelPlacementTests
{
    [Fact]
    public void Sheets_are_columns_with_views_stacked_in_viewport_order()
    {
        var frame = new FrameGeometry(20, 5, 5, 30, 1, 10);
        var slots = StackPlanner.OrderSlots(new[]
        {
            new ViewportSlot(420, 450, 800, 250, 2),
            new ViewportSlot(420, 150, 800, 250, 2),
        });
        var samples = new ProfileSamples();
        samples.Add(new[] { (0.0, 50.0), (2000.0, 50.0) });
        var s = new StackerSettings { StartStation = 0, EndStation = 1000, PaperClearance = 0, ClearanceBelow = 0, ClearanceAbove = 0 };
        var plan = StackPlanner.Plan(slots, frame, s, samples);

        var placed = ModelPlacement.Place(plan, frame, left: 0, top: 0, gap: 10, paperClearance: 0);

        // View 1: graph 9 m x10 = 90 high under a 5 m title; frame 125 m high exactly fills the viewport.
        Assert.Equal(20, placed[0].GraphX, 6);
        Assert.Equal(-95, placed[0].GraphY, 6);
        Assert.Equal(125, placed[0].Frame.Height, 6);
        // View 2 sits under view 1 with the gap; view 3 starts the next column (400 m viewport + gap).
        Assert.Equal(-135, placed[1].Frame.MaxY, 6);
        Assert.Equal(410, placed[2].Frame.MinX, 6);
        Assert.Equal(0, placed[2].Frame.MaxY, 6);
        // Viewport looks at a full 400 m width starting at the frame's left edge.
        Assert.Equal(200, placed[0].ViewCenterX, 6);
        Assert.Equal(placed[0].Frame.CenterY, placed[0].ViewCenterY, 6);
    }
}

public class SettingsTests
{
    [Fact]
    public void Round_trips_through_json_without_the_stations()
    {
        var s = new StackerSettings
        {
            TemplatePath = @"C:\Templates\Profile A1.dwt",
            DatumMode = DatumMode.Centred,
            LengthMode = LengthMode.Even,
            Overlap = 5,
            StartStation = 123,
        };
        var back = StackerSettings.FromJson(s.ToJson());
        Assert.Equal(s.TemplatePath, back.TemplatePath);
        Assert.Equal(DatumMode.Centred, back.DatumMode);
        Assert.Equal(LengthMode.Even, back.LengthMode);
        Assert.Equal(5, back.Overlap);
        Assert.Equal(0, back.StartStation);
        Assert.Contains("\"Centred\"", s.ToJson());
    }

    [Fact]
    public void Broken_json_gives_defaults() =>
        Assert.Equal(10, StackerSettings.FromJson("{ not json").StationRounding);
}
