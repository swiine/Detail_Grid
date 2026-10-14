using PavementBuildup.Core;
using PavementBuildup.Export;

namespace PavementBuildup.Core.Tests;

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
        Assert.True(doc.Entities.Hatches.Count() >= 3);
        Assert.True(doc.Entities.Dimensions.Count() >= 2);
        Assert.Contains(doc.Entities.MTexts, t => t.Value.Contains("CARPARK"));
        Assert.True(doc.Layers.Contains(standard.Layer(DetailElement.Hatch).Name));
        Assert.True(doc.Layers.Contains(standard.Layer(DetailElement.Membrane).Name));
        Assert.Equal(netDxf.Units.DrawingUnits.Meters, doc.DrawingVariables.InsUnits);
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
        Assert.True(g.Titles[2].At.Y < g.Titles[1].At.Y);
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

public class LongWordingTests
{
    private const string Gravel = "5. Standard Domestic Gravel Driveway (Unbound Pavement)A low-cost, classic rural or suburban driveway layout. It relies purely on stone interlock rather than chemical binders like cement or tar.• Surface Layer: 40–50mm of loose, decorative angular gravel (10–20mm sizing so it doesn't get stuck in shoe or tire treads).• Base Course: 100–150mm of dense, graded crushed rock with fines (MOT Type 1 or equivalent) that compacts into a solid, unyielding crust.• Geotextile Membrane: Heavy-duty weed barrier fabric to keep the base stone from sinking into the soft earth.• Subgrade: Stripped and compacted natural subsoil.";

    [Fact]
    public void Commas_inside_a_list_line_stay_in_that_layer_and_a_membrane_label_is_kept()
    {
        var errors = new List<string>();
        var r = SmartReader.Read(Gravel, errors);
        Assert.Empty(errors);
        Assert.Equal("Standard Domestic Gravel Driveway (Unbound Pavement)", r.Parsed.Name);
        Assert.Equal(new[] { 50.0, 150, 0 }, r.Parsed.Layers.Select(l => l.ThicknessMm));
        Assert.StartsWith("loose, decorative angular gravel", r.Parsed.Layers[0].Description);
        Assert.StartsWith("dense, graded crushed rock", r.Parsed.Layers[1].Description);
        Assert.StartsWith("Geotextile Membrane", r.Parsed.Layers[2].Description);
    }

    [Fact]
    public void Long_labels_wrap_onto_more_lines()
    {
        var wrapped = DetailLayout.WrapLabel("150mm DENSE, GRADED CRUSHED ROCK WITH FINES (MOT TYPE 1 OR EQUIVALENT) THAT COMPACTS INTO A SOLID CRUST", 60);
        Assert.All(wrapped.Split('\n'), line => Assert.True(line.Length <= 60));
        Assert.True(wrapped.Split('\n').Length >= 2);
        Assert.Equal("30mm THICK MORTAR", DetailLayout.WrapLabel("30mm THICK MORTAR", 60));
    }
}
