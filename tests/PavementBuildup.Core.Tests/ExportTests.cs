using PavementBuildup.Core;
using PavementBuildup.Export;

namespace PavementBuildup.Core.Tests;

/// <summary>The standalone tools' outputs: a DXF AutoCAD can open, and the SVG picture.</summary>
public class ExportTests
{
    private static Buildup Sample() =>
        SmartReader.Read("[CARPARK]\n40mm AC10 wearing course\n2 x 150mm DGB20 road base\nGeotextile\nSubgrade", new List<string>())
            .Parsed.ToBuildup("CARPARK");

    [Fact]
    public void Dxf_has_the_detail_on_the_standard_layers_and_reads_back()
    {
        var standard = CadStandard.CreateDefault();
        using var ms = new MemoryStream();
        DxfExport.Write(Sample(), new DetailSettings(), standard, ms);

        ms.Position = 0;
        var doc = netDxf.DxfDocument.Load(ms);
        Assert.NotNull(doc);
        Assert.True(doc.Entities.Hatches.Count() >= 3);           // 2 courses + subgrade
        Assert.True(doc.Entities.Dimensions.Count() >= 2);
        Assert.Contains(doc.Entities.MTexts, t => t.Value.Contains("CARPARK"));
        Assert.True(doc.Layers.Contains(standard.Layer(DetailElement.Hatch).Name));
        Assert.True(doc.Layers.Contains(standard.Layer(DetailElement.Membrane).Name));
        Assert.Equal(netDxf.Units.DrawingUnits.Meters, doc.DrawingVariables.InsUnits);
        // Metres: a 1000mm wide strip is 1.0 drawing units wide.
        Assert.Contains(doc.Entities.Lines, l => Math.Abs(Math.Abs(l.EndPoint.X - l.StartPoint.X) - 1.0) < 1e-6);
    }

    [Fact]
    public void Svg_picture_draws_the_layers_and_labels()
    {
        var svg = SvgPreview.Render(Sample(), new DetailSettings(), CadStandard.CreateDefault(), out var error);
        Assert.Null(error);
        Assert.NotNull(svg);
        Assert.StartsWith("<svg", svg);
        Assert.Contains("ROAD BASE", svg);
        Assert.Contains("url(#p-", svg);
    }

    [Fact]
    public void Svg_says_why_when_there_is_nothing_to_draw()
    {
        var svg = SvgPreview.Render(new Buildup { Layers = new() }, new DetailSettings(), CadStandard.CreateDefault(), out var error);
        Assert.Null(svg);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
