using ETWalls.Core.Design;
using ETWalls.Core.Drawing;
using ETWalls.Core.Standard;
using ETWalls.Export;

namespace ETWalls.Core.Tests;

public class DxfTests
{
    static string StandardPath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "standards", "TTW.json"));

    [Fact]
    public void ReadsTheCompanyStandard()
    {
        var std = CadStandard.Load(StandardPath);
        Assert.Equal("TTW", std.Name);
        Assert.Equal("Pen-25-c", std.Layers[Layer.Outline].Name);
        Assert.Equal("pen-35-c", std.Layers[Layer.Reinforcement].Name);
    }

    [Theory]
    [InlineData(WallType.Cantilever)]
    [InlineData(WallType.Block)]
    [InlineData(WallType.Post)]
    public void WritesADxfOnTheStandardLayersInMetres(WallType type)
    {
        var inp = new WallInput { Type = type, WallName = "W1", H = 1.8 };
        inp = Optimiser.Optimise(inp).Sizes.Apply(inp);
        var d = SectionBuilder.Build(inp, WallDesign.RunAll(inp));
        using var ms = new MemoryStream();
        DxfExport.Write(d, CadStandard.Load(StandardPath), ms);
        ms.Position = 0;
        var doc = netDxf.DxfDocument.Load(ms);
        Assert.NotNull(doc);
        Assert.True(doc.Layers.Contains("Pen-25-c"));
        Assert.True(doc.Layers.Contains("TTW_stdGlobal_Text_25"));
        Assert.Contains(doc.Entities.Hatches, h => h.Pattern.Name == "EARTH");
        Assert.NotEmpty(doc.Entities.MTexts);
        // True size: the wall is about as tall as designed (metres), not paper millimetres.
        var ys = doc.Entities.Polylines2D.SelectMany(p => p.Vertexes).Select(v => v.Position.Y).ToList();
        Assert.InRange(ys.Max() - ys.Min(), inp.H, inp.H + 8);
    }
}

public class DxfSampleWriter
{
    [Fact]
    public void WritesSamplesWhenAsked()
    {
        var dir = Environment.GetEnvironmentVariable("ETWALLS_DRAWINGS");
        if (dir is null) return;
        var inp = new WallInput { WallName = "W1 - Carpark east" };
        inp = Optimiser.Optimise(inp).Sizes.Apply(inp);
        var d = SectionBuilder.Build(inp, WallDesign.RunAll(inp));
        using var fs = File.Create(Path.Combine(dir, "cantilever.dxf"));
        DxfExport.Write(d, CadStandard.Default, fs);
    }
}
