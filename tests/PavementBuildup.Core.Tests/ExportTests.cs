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

public class TitleWrapTests
{
    [Fact]
    public void A_title_wider_than_the_build_up_goes_onto_two_lines()
    {
        var b = new Buildup
        {
            Name = "CELLULAR GRASS PAVING (OVERFLOW PARKING / FIRE LANES)",
            Layers = BuildupParser.Parse("50mm grass cells, 30mm sand, 150mm crushed stone"),
        };
        var g = DetailLayout.Build(b, new DetailSettings(), CadStandard.CreateDefault(), 1.0);
        Assert.Equal("CELLULAR GRASS PAVING", g.Titles[0].Text);
        Assert.Equal("(OVERFLOW PARKING / FIRE LANES)", g.Titles[1].Text);
        Assert.True(g.Titles[1].At.Y < g.Titles[0].At.Y);
        Assert.True(g.Titles[2].At.Y < g.Titles[1].At.Y); // the scale line moves down
    }

    [Fact]
    public void A_short_title_stays_on_one_line() =>
        Assert.Single(DetailLayout.SplitTitle("CARPARK", 35, 1000));
}

public class PastedListTests
{
    [Fact]
    public void Inline_bullets_and_a_description_after_the_heading_read_cleanly()
    {
        const string text = "3. Cellular Grass Paving (Overflow Parking / Fire Lanes)An eco-friendly structural driving surface while maintaining the look and drainage of a lawn.• Surface Layer: 40–50mm Plastic interlocking hexagonal cells filled with a mix of topsoil and grass seed (or washed angular gravel).• Bedding Layer: 30mm Sharp sand mixed with organic compost.• Base Course: 100–150mm of open-graded crushed stone for stability and root water storage.• Subgrade: Compacted natural earth.";
        var errors = new List<string>();
        var r = SmartReader.Read(text, errors);
        Assert.Empty(errors);
        Assert.Equal("Cellular Grass Paving (Overflow Parking / Fire Lanes)", r.Parsed.Name);
        Assert.Equal(new[] { 50.0, 30, 150 }, r.Parsed.Layers.Select(l => l.ThicknessMm));
        Assert.Equal("open-graded crushed stone for stability and root water storage (100-150mm)", r.Parsed.Layers[2].Description);
        Assert.Equal("Subgrade Compacted natural earth", r.Parsed.SubgradeText);
    }
}
