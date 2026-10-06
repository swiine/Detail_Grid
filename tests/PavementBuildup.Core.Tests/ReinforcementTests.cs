using PavementBuildup.Core;

namespace PavementBuildup.Core.Tests;

public class ReinforcementTests
{
    private static readonly CadStandard Std = CadStandard.CreateDefault();

    [Fact]
    public void Parses_single_bottom_mat_by_default()
    {
        var r = Assert.Single(ReinforcementParser.Parse("H16@150 c50"));
        Assert.Equal(BarFace.Bottom, r.Face);
        Assert.Equal((16.0, 150.0, 50.0, "H"), (r.DiameterMm, r.SpacingMm, r.CoverMm, r.Prefix));
        Assert.False(r.HasTransverse);
    }

    [Theory]
    [InlineData("top T12@200 cover 40 + 10@300")]
    [InlineData("TOP: t12 @ 200mm c/c, cover=40mm & 10@300 c/c")]
    [InlineData("top 12@200 crs c40 and 10 @ 300")]
    public void Accepts_common_ways_of_writing_it(string text)
    {
        var r = Assert.Single(ReinforcementParser.Parse(text));
        Assert.Equal(BarFace.Top, r.Face);
        Assert.Equal((12.0, 200.0, 40.0), (r.DiameterMm, r.SpacingMm, r.CoverMm));
        Assert.Equal((10.0, 300.0), (r.TransverseDiameterMm, r.TransverseSpacingMm));
    }

    [Fact]
    public void Two_mats_separated_by_comma_and_formatting_round_trips()
    {
        const string text = "top H12@200 c40, H16@150 c50 + H10@300";
        var mats = ReinforcementParser.Parse(text);
        Assert.Equal(new[] { BarFace.Top, BarFace.Bottom }, mats.Select(m => m.Face));
        Assert.Equal(text, ReinforcementParser.Format(mats));
    }

    [Theory]
    [InlineData("H16")]
    [InlineData("H16@150")]          // no cover
    [InlineData("H0@150 c50")]
    [InlineData("H16@150 c50, bottom H12@200 c40")] // two bottom mats
    public void Rejects_incomplete_or_ambiguous_input(string text) =>
        Assert.Throws<FormatException>(() => ReinforcementParser.Parse(text));

    [Fact]
    public void Quick_entry_takes_reinforcement_in_brackets()
    {
        var layers = BuildupParser.Parse("250 PQC C32/40 [top H12@200 c60, H16@150 c50]; 150 Type 1");
        Assert.Equal("PQC C32/40", layers[0].Description);
        Assert.Equal(2, layers[0].Reinforcement.Count);
        Assert.Empty(layers[1].Reinforcement);
        Assert.Equal("250 PQC C32/40 [top H12@200 c60, H16@150 c50]; 150 Type 1", BuildupParser.Format(layers));

        var errors = new List<string>();
        BuildupParser.Parse("250 PQC [H16@150 lots]", errors); // bar notation that is not valid
        Assert.Single(errors);
    }

    [Fact]
    public void Table_text_property_parses_and_formats()
    {
        var l = new PavementLayer { Description = "PQC", ThicknessMm = 250, ReinforcementText = "H16@150 c50" };
        Assert.Single(l.Reinforcement);
        Assert.Equal("H16@150 c50", l.ReinforcementText);
        l.ReinforcementText = "";
        Assert.Empty(l.Reinforcement);
    }

    [Fact]
    public void Bars_are_placed_at_cover_from_the_right_face_and_centred_across_the_width()
    {
        // 250 PQC from y=0 to -250. Bottom mat: 50 cover + H16 => centre 50+8 = 58 above -250.
        var b = new Buildup { Layers = BuildupParser.Parse("250 PQC [top H12@200 c40, H16@150 c50 + H10@300]") };
        var g = DetailLayout.Build(b, new DetailSettings { WidthMm = 1000 }, Std, 1);

        var bottom = g.Bars.Where(x => x.Diameter == 16).ToList();
        Assert.Equal(6, bottom.Count);                           // floor(1000/150)
        Assert.All(bottom, x => Assert.Equal(-192, x.Center.Y, 9));
        Assert.Equal(1000 - bottom[^1].Center.X, bottom[0].Center.X, 9); // symmetric
        Assert.Equal(150, bottom[1].Center.X - bottom[0].Center.X, 9);

        var top = g.Bars.Where(x => x.Diameter == 12).ToList();
        Assert.All(top, x => Assert.Equal(-46, x.Center.Y, 9));    // 40 + 6 below the surface

        var line = Assert.Single(g.BarLines);
        Assert.Equal(-250 + 50 + 16 + 5, line.Y, 9);               // inside the main bars
    }

    [Fact]
    public void Labels_and_cover_dimensions_follow_the_standard()
    {
        var b = new Buildup { Layers = BuildupParser.Parse("250 PQC [top 12@200 c40, H16@150 c50 + H10@300]") };
        var g = DetailLayout.Build(b, new DetailSettings(), Std, 1);
        var texts = g.Labels.Select(l => l.Text).ToList();

        // Top mat, course, bottom mat, subgrade: in level order.
        Assert.Equal("H12 @ 200 c/c TOP - 40mm COVER", texts[0]);
        Assert.Equal("250mm PQC", texts[1]);
        Assert.Equal("H16 @ 150 c/c BTM + H10 @ 300 c/c - 50mm COVER", texts[2]);

        var covers = g.Dimensions.Where(d => d.RefX != 0).ToList();
        Assert.Equal(new[] { "40", "50" }, covers.Select(c => c.Text));
        Assert.Equal((-200.0, -250.0), (covers[1].Top, covers[1].Bottom)); // 50mm cover above the bottom face

        var s = CadStandard.CreateDefault();
        s.BarPrefix = "N";
        s.ReinforcementLabelFormat = "{bars} ({cover} COVER)";
        s.ShowCoverDimension = false;
        var g2 = DetailLayout.Build(b, new DetailSettings(), s, 1);
        Assert.Equal("N12 @ 200 c/c (40 COVER)", g2.Labels[0].Text);
        Assert.DoesNotContain(g2.Dimensions, d => d.RefX != 0);
    }

    [Fact]
    public void Bars_that_do_not_fit_are_rejected_with_a_clear_message()
    {
        var tooDeep = new Buildup { Layers = BuildupParser.Parse("100 Concrete [H16@150 c90]") };
        var ex = Assert.Throws<ArgumentException>(() => DetailLayout.Build(tooDeep, new DetailSettings(), Std, 1));
        Assert.Contains("more than the 100mm course", ex.Message);

        var clash = new Buildup { Layers = BuildupParser.Parse("150 Concrete [top H16@150 c60, H16@150 c60]") };
        Assert.Throws<ArgumentException>(() => DetailLayout.Build(clash, new DetailSettings(), Std, 1));
    }
}
