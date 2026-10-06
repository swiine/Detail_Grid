using PavementBuildup.Core;

namespace PavementBuildup.Core.Tests;

public class UnitsTests
{
    [Fact]
    public void Default_standard_draws_in_metres_so_40mm_is_0_04()
    {
        var std = CadStandard.CreateDefault();
        Assert.Equal("M", std.DrawingUnits);

        double u = DetailLayout.ResolveUnitsPerMm(new DetailSettings().UnitsOverride, std.DrawingUnits, insunits: "MM");
        Assert.Equal(0.001, u, 12);

        var g = DetailLayout.Build(new Buildup { Layers = BuildupParser.Parse("40 SMA surface; 60 binder") }, new DetailSettings(), std, u);
        Assert.Equal(0.04, g.Bands[0].Top - g.Bands[0].Bottom, 12);
        Assert.Equal(-0.1, g.InterfaceLevels[^1], 12);
        Assert.Equal(1.0, g.Width, 12);           // 1000mm strip = 1.0 m
        Assert.Equal(0.025, g.TextHeight, 12);    // 2.5mm text at 1:10 = 25mm = 0.025 m
    }

    [Theory]
    [InlineData("STANDARD", "M", "MM", 0.001)]   // standard wins over INSUNITS
    [InlineData("STANDARD", "MM", "M", 1.0)]
    [InlineData("", "", null, 0.001)]            // nothing set: metres
    [InlineData("MM", "M", "M", 1.0)]            // dialog override wins
    [InlineData("STANDARD", "AUTO", "CM", 0.1)]  // AUTO reads INSUNITS
    [InlineData("AUTO", "MM", null, 0.001)]      // unitless drawing: metres
    public void Resolves_units(string settings, string standard, string? insunits, double expected) =>
        Assert.Equal(expected, DetailLayout.ResolveUnitsPerMm(settings, standard, insunits), 12);

    [Fact]
    public void Bars_and_cover_scale_to_metres_too()
    {
        var b = new Buildup { Layers = BuildupParser.Parse("250 PQC [H16@150 c50]") };
        var g = DetailLayout.Build(b, new DetailSettings(), CadStandard.CreateDefault(), 0.001);

        Assert.All(g.Bars, bar => Assert.Equal(0.016, bar.Diameter, 12));
        Assert.All(g.Bars, bar => Assert.Equal(-0.25 + 0.05 + 0.008, bar.Center.Y, 12));
        Assert.Equal(0.15, g.Bars[1].Center.X - g.Bars[0].Center.X, 12);
    }

    [Fact]
    public void Invalid_units_are_rejected()
    {
        var s = CadStandard.CreateDefault();
        s.DrawingUnits = "FURLONGS";
        Assert.Contains(s.Validate(), e => e.Contains("Drawing units"));
        Assert.Throws<ArgumentException>(() => DetailLayout.ResolveUnitsPerMm("FURLONGS", "M", null));
    }
}
