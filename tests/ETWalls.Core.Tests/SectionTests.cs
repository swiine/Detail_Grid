using ETWalls.Core.Design;
using ETWalls.Core.Drawing;

namespace ETWalls.Core.Tests;

public class SectionTests
{
    static WallInput Sized(WallType type, Footing footing = Footing.Heel, bool facesRight = false)
    {
        var inp = new WallInput { Type = type, Footing = footing, FacesRight = facesRight, WallName = "W1 - Carpark east", H = type == WallType.Cantilever ? 2.4 : 1.8 };
        return Optimiser.Optimise(inp).Sizes.Apply(inp);
    }

    [Theory]
    [InlineData(WallType.Cantilever, Footing.Heel, false)]
    [InlineData(WallType.Cantilever, Footing.Heel, true)]
    [InlineData(WallType.Cantilever, Footing.Toe, false)]
    [InlineData(WallType.Block, Footing.Heel, false)]
    [InlineData(WallType.Gravity, Footing.Heel, false)]
    [InlineData(WallType.Post, Footing.Heel, false)]
    public void DrawsEveryWallTypeAtAStandardScale(WallType type, Footing footing, bool facesRight)
    {
        var inp = Sized(type, footing, facesRight);
        var d = SectionBuilder.Build(inp, WallDesign.RunAll(inp));
        Assert.Contains(d.Scale, Cad.Scales);
        Assert.Contains(d.Items, i => i is Shape { Fill: Fill.Earth });
        Assert.Contains(d.Items, i => i is Label { Layer: Layer.Title } l && l.Text.StartsWith("TYPICAL WALL SECTION"));
        Assert.True(d.View.W > 50 && d.View.H > 50);
        var svg = SvgWriter.Write(d);
        Assert.StartsWith("<svg", svg);
        var dir = Environment.GetEnvironmentVariable("ETWALLS_DRAWINGS");
        if (dir is not null) File.WriteAllText(Path.Combine(dir, $"{type}-{footing}-{(facesRight ? "R" : "L")}.svg"), svg);
    }

    [Fact]
    public void MirroringKeepsTheTextReadable()
    {
        var inp = Sized(WallType.Cantilever, facesRight: true);
        var d = SectionBuilder.Build(inp, WallDesign.RunAll(inp));
        Assert.True(d.Mirrored);
        Assert.All(d.Items.OfType<Label>(), l => Assert.True(l.Rotation is 0 or 90));
    }

    [Fact]
    public void BarLabelsUseTheStandardFormat()
    {
        var inp = Sized(WallType.Cantilever);
        var d = SectionBuilder.Build(inp, WallDesign.RunAll(inp));
        Assert.Contains(d.Items.OfType<Label>(), l => System.Text.RegularExpressions.Regex.IsMatch(l.Text, @"^N\d+ @ \d+ c/c \(A\)"));
    }
}
