using PavementBuildup.Core;

namespace PavementBuildup.Core.Tests;

public class DetailLayoutTests
{
    private static Buildup Sample() => new()
    {
        Name = "Type A",
        Layers = BuildupParser.Parse("40 SMA surface; 60 AC 20 binder; 150 AC 32 base; 0 Geotextile; 225 Type 1 sub-base"),
    };

    [Fact]
    public void Stacks_bands_from_the_surface_down_in_millimetres()
    {
        var g = DetailLayout.Build(Sample(), new DetailSettings(), unitsPerMm: 1);

        Assert.Equal(4, g.Bands.Count); // geotextile is a line, not a band
        Assert.Equal((0.0, -40.0), (g.Bands[0].Top, g.Bands[0].Bottom));
        Assert.Equal((-250.0, -475.0), (g.Bands[3].Top, g.Bands[3].Bottom));
        Assert.Equal(new[] { -250.0 }, g.MembraneLevels);
        Assert.Equal(new[] { 0.0, -40, -100, -250, -475 }, g.InterfaceLevels);
        Assert.Equal(-625, g.Subgrade!.Bottom); // 150mm default strip
    }

    [Fact]
    public void Scales_everything_to_metre_drawings()
    {
        var g = DetailLayout.Build(Sample(), new DetailSettings { WidthMm = 1000 }, unitsPerMm: 0.001);

        Assert.Equal(1.0, g.Width, 9);
        Assert.Equal(-0.475, g.InterfaceLevels[^1], 9);
        Assert.Equal(0.025, g.TextHeight, 9); // 2.5mm on paper at 1:10 = 25mm = 0.025m
    }

    [Fact]
    public void Labels_never_overlap_even_for_thin_layers()
    {
        var b = new Buildup { Layers = BuildupParser.Parse("10 Thin surfacing; 0 Tack coat; 10 Regulating; 20 Binder; 200 Type 1") };
        var g = DetailLayout.Build(b, new DetailSettings(), 1);

        for (int i = 1; i < g.Labels.Count; i++)
            Assert.True(g.Labels[i - 1].TextAt.Y - g.Labels[i].TextAt.Y >= g.TextHeight * 1.8 - 1e-9);

        // Labels only move down, never above their own course.
        Assert.All(g.Labels, l => Assert.True(l.TextAt.Y <= l.Anchor.Y + 1e-9));
    }

    [Fact]
    public void Dimensions_per_course_plus_total()
    {
        var g = DetailLayout.Build(Sample(), new DetailSettings(), 1);

        Assert.Equal(new[] { "40mm", "60mm", "150mm", "225mm", "475mm" }, g.Dimensions.Select(d => d.Text));
        Assert.Contains(g.Titles, t => t.Text == "TOTAL CONSTRUCTION DEPTH = 475mm");
        Assert.Contains(g.Titles, t => t.Text == "SCALE 1:10");
    }

    [Fact]
    public void Labels_include_thickness_and_respect_case_option()
    {
        var upper = DetailLayout.Build(Sample(), new DetailSettings(), 1);
        var asTyped = DetailLayout.Build(Sample(), new DetailSettings { UpperCaseLabels = false }, 1);

        Assert.Equal("40mm SMA SURFACE", upper.Labels[0].Text);
        Assert.Equal("40mm SMA surface", asTyped.Labels[0].Text);
        Assert.Equal("GEOTEXTILE", upper.Labels[3].Text);
        Assert.Equal("SUBGRADE", upper.Labels[^1].Text);
    }

    [Fact]
    public void Options_can_switch_parts_off()
    {
        var b = Sample();
        b.ShowSubgrade = false;
        var g = DetailLayout.Build(b, new DetailSettings { ShowDimensions = false, ShowTitle = false, ShowBreakLines = false }, 1);

        Assert.Null(g.Subgrade);
        Assert.Empty(g.Dimensions);
        Assert.Empty(g.Titles);
        Assert.All(g.Edges, e => Assert.Equal(2, e.Count));
    }

    [Fact]
    public void Rejects_empty_or_negative_buildups()
    {
        Assert.Throws<ArgumentException>(() => DetailLayout.Build(new Buildup(), new DetailSettings(), 1));
        var bad = new Buildup { Layers = { new PavementLayer { Description = "x", ThicknessMm = -5 } } };
        Assert.Throws<ArgumentException>(() => DetailLayout.Build(bad, new DetailSettings(), 1));
    }
}
