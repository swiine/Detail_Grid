using PavementBuildup.Core;

namespace PavementBuildup.Core.Tests;

public class TitleAndSizesTests
{
    [Fact]
    public void Square_brackets_are_the_title_but_bar_notation_stays_reinforcement()
    {
        var errors = new List<string>();
        var r = BuildupParser.ParseBuildup("[PAVEMENT TYPE A - DRIVEWAY] 250mm CONCRETE [H12@200 c50], ON 100mm DGB20 (COMPACTED), SUBGRADE", errors);

        Assert.Empty(errors);
        Assert.Equal("PAVEMENT TYPE A - DRIVEWAY", r.Name);
        Assert.Equal("CONCRETE", r.Layers[0].Description);
        Assert.Single(r.Layers[0].Reinforcement);
        Assert.Equal(2, r.Layers.Count);
    }

    [Fact]
    public void Title_can_be_anywhere_and_round_trips()
    {
        var r = BuildupParser.ParseBuildup("60mm THICK PAVERS, 30mm MORTAR, SUBGRADE [TYPE B]", new List<string>());
        Assert.Equal("TYPE B", r.Name);

        var text = BuildupParser.Format(r.ToBuildup());
        Assert.StartsWith("[TYPE B] ", text);
        Assert.Equal("TYPE B", BuildupParser.ParseBuildup(text, new List<string>()).Name);
    }

    [Fact]
    public void Title_block_is_centred_under_the_detail_with_its_own_scale_height()
    {
        var s = CadStandard.CreateDefault();
        s.TitleHeightMm = 5;
        s.ScaleHeightMm = 3;
        var b = new Buildup { Name = "Type A", Layers = BuildupParser.Parse("40 SMA, 150 DGB20") };
        var g = DetailLayout.Build(b, new DetailSettings { WidthMm = 1000 }, s, 0.001);

        Assert.All(g.Titles, t => Assert.True(t.Centered));
        Assert.All(g.Titles, t => Assert.Equal(0.5, t.At.X, 9));            // middle of a 1.0 m wide strip
        Assert.Equal(new[] { 0.05, 0.03, 0.025 }, g.Titles.Select(t => Math.Round(t.Height, 9))); // title, scale, total
        Assert.True(g.Titles[0].At.Y > g.Titles[1].At.Y && g.Titles[1].At.Y > g.Titles[2].At.Y);
        Assert.True(g.Titles[0].At.Y < g.Subgrade!.Bottom);                   // below the build-up
    }

    [Fact]
    public void New_sizes_have_defaults_and_are_validated()
    {
        var s = CadStandard.CreateDefault();
        Assert.Equal((2.5, 2.5, 2.0), (s.ScaleHeightMm, s.DimensionTextHeightMm, s.DimensionArrowSizeMm));
        s.ScaleHeightMm = 0;
        Assert.Contains(s.Validate(), e => e.Contains("Text heights"));
    }
}

public class ShowDimensionsTests
{
    [Fact]
    public void Turning_dimensions_off_removes_thickness_and_cover_dimensions()
    {
        var b = new Buildup { Layers = BuildupParser.Parse("250 PQC [H16@150 c50], 150 DGB20") };
        var on = DetailLayout.Build(b, new DetailSettings(), CadStandard.CreateDefault(), 1);
        var off = DetailLayout.Build(b, new DetailSettings { ShowDimensions = false }, CadStandard.CreateDefault(), 1);

        Assert.Contains(on.Dimensions, d => d.Text == "50");   // cover
        Assert.Contains(on.Dimensions, d => d.Text == "250mm");
        Assert.Empty(off.Dimensions);
    }
}
