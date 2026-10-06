using PavementBuildup.Core;

namespace PavementBuildup.Core.Tests;

public class ExistingWorkTests
{
    [Fact]
    public void Default_standard_has_PEN_GREY_layers_for_existing_work()
    {
        var s = CadStandard.CreateDefault();
        Assert.Equal("PEN-GREY-H", s.Layer(DetailElement.ExistingHatch).Name);
        Assert.Equal("PEN-GREY-C", s.Layer(DetailElement.ExistingOutline).Name);
        Assert.Contains("PEN-GREY-H", s.AllLayerNames());
        Assert.Contains("PEN-GREY-C", s.AllLayerNames());
    }

    [Fact]
    public void Existing_base_is_flagged_and_new_pavement_edges_stop_on_top_of_it()
    {
        var b = BuildupParser.ParseBuildup("60mm THICK PAVERS, ON 30mm SCREED, ON EXISTING CONCRETE SLAB (LEVELS TBC ON SITE)", new List<string>()).ToBuildup();
        var g = DetailLayout.Build(b, new DetailSettings(), CadStandard.CreateDefault(), 1);

        Assert.True(g.Subgrade!.Existing);
        Assert.All(g.Bands, x => Assert.False(x.Existing));
        Assert.All(g.Edges, e => Assert.Equal(-90, e[^1].Y, 9)); // formation, not the bottom of the slab strip
    }

    [Fact]
    public void Subgrade_is_not_existing_and_existing_courses_are()
    {
        var b = BuildupParser.ParseBuildup("40mm AC OVERLAY, 200mm EXISTING ASPHALT (MILLED), SUBGRADE", new List<string>()).ToBuildup();
        var g = DetailLayout.Build(b, new DetailSettings(), CadStandard.CreateDefault(), 1);

        Assert.Equal(new[] { false, true }, g.Bands.Select(x => x.Existing));
        Assert.False(g.Subgrade!.Existing);
        Assert.All(g.Edges, e => Assert.Equal(-240 - 150, e[^1].Y, 9)); // edges run through the subgrade as before
    }
}
