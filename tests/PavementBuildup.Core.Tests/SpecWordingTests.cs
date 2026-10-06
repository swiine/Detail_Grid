using PavementBuildup.Core;

namespace PavementBuildup.Core.Tests;

public class SpecWordingTests
{
    private const string Spec =
        "60mm THICK PAVERS (REFER TO LANDSCAPE SPECIFICATIONS), 30mm THICK MORTAR, " +
        "2 x 150mm THICK LAYERS OF DGB20 ROAD BASE, SUBGRADE COMPACTED TO 98% STANDARD MDD.";

    [Fact]
    public void Reads_the_spec_note_as_written()
    {
        var errors = new List<string>();
        var r = BuildupParser.ParseBuildup(Spec, errors);

        Assert.Empty(errors);
        Assert.Equal(3, r.Layers.Count);
        Assert.Equal((60.0, 1, "THICK PAVERS (REFER TO LANDSCAPE SPECIFICATIONS)"), (r.Layers[0].ThicknessMm, r.Layers[0].Lifts, r.Layers[0].Description));
        Assert.Equal((30.0, 1, "THICK MORTAR"), (r.Layers[1].ThicknessMm, r.Layers[1].Lifts, r.Layers[1].Description));
        Assert.Equal((150.0, 2, "THICK LAYERS OF DGB20 ROAD BASE"), (r.Layers[2].ThicknessMm, r.Layers[2].Lifts, r.Layers[2].Description));
        Assert.Equal(300, r.Layers[2].TotalMm);
        Assert.Equal("SUBGRADE COMPACTED TO 98% STANDARD MDD", r.SubgradeText);
    }

    [Fact]
    public void Draws_it_with_the_same_wording_layers_and_hatches()
    {
        var r = BuildupParser.ParseBuildup(Spec, new List<string>());
        var b = new Buildup { Name = "PAVEMENT TYPE A", Layers = r.Layers, SubgradeText = r.SubgradeText! };
        var g = DetailLayout.Build(b, new DetailSettings(), CadStandard.CreateDefault(), 0.001); // metres

        Assert.Equal(new[]
        {
            "60mm THICK PAVERS (REFER TO LANDSCAPE SPECIFICATIONS)",
            "30mm THICK MORTAR",
            "2 x 150mm THICK LAYERS OF DGB20 ROAD BASE",
            "SUBGRADE COMPACTED TO 98% STANDARD MDD",
        }, g.Labels.Select(l => l.Text));

        Assert.Equal(new[] { "ANSI37", "AR-SAND", "GRAVEL" }, g.Bands.Select(x => x.Hatch!.Pattern));
        Assert.Equal(-0.39, g.InterfaceLevels[^1], 9);      // 60 + 30 + 300 mm = 0.39 m
        Assert.Equal(new[] { -0.24 }, g.LiftLevels.Select(v => Math.Round(v, 9)));
        Assert.Equal(new[] { "60mm", "30mm", "150mm", "150mm", "390mm" }, g.Dimensions.Select(d => d.Text));
        Assert.Contains(g.Titles, t => t.Text == "TOTAL CONSTRUCTION DEPTH = 390mm");
    }

    [Fact]
    public void Heading_numbering_and_wrapped_lines_from_a_drawing_note()
    {
        const string note = "PAVEMENT TYPE B:\n1. 60mm THICK PAVERS (REFER TO\nLANDSCAPE SPECIFICATIONS), 2. 30mm THICK MORTAR,\n3. 2 x 150mm THICK LAYERS OF DGB20 ROAD BASE, SUBGRADE COMPACTED TO 98% STANDARD MDD.";
        var errors = new List<string>();
        var r = BuildupParser.ParseBuildup(note, errors);

        Assert.Empty(errors);
        Assert.Equal("PAVEMENT TYPE B", r.Name);
        Assert.Equal(3, r.Layers.Count);
        Assert.Equal("THICK PAVERS (REFER TO LANDSCAPE SPECIFICATIONS)", r.Layers[0].Description);
        Assert.Equal(2, r.Layers[2].Lifts);

        var inline = BuildupParser.ParseBuildup("PAVEMENT TYPE C: 40mm SMA, 150mm DGB20", errors);
        Assert.Equal("PAVEMENT TYPE C", inline.Name);
        Assert.Equal(2, inline.Layers.Count);
    }

    [Fact]
    public void One_course_per_line_still_works_without_commas()
    {
        var r = BuildupParser.ParseBuildup("40 SMA\n60 binder\n150 Type 1", new List<string>());
        Assert.Equal(3, r.Layers.Count);
    }

    [Fact]
    public void Round_trips_layers_lifts_and_subgrade_through_quick_entry_text()
    {
        var r = BuildupParser.ParseBuildup(Spec, new List<string>());
        var b = new Buildup { Layers = r.Layers, SubgradeText = r.SubgradeText! };
        var text = BuildupParser.Format(b);
        Assert.Equal("60 THICK PAVERS (REFER TO LANDSCAPE SPECIFICATIONS); 30 THICK MORTAR; 2 x 150 THICK LAYERS OF DGB20 ROAD BASE; SUBGRADE COMPACTED TO 98% STANDARD MDD", text);

        var again = BuildupParser.ParseBuildup(text, new List<string>());
        Assert.Equal(BuildupParser.Format(r.Layers), BuildupParser.Format(again.Layers));
        Assert.Equal(r.SubgradeText, again.SubgradeText);
    }

    [Fact]
    public void Line_without_thickness_after_a_course_becomes_a_second_label_line()
    {
        var errors = new List<string>();
        var layers = BuildupParser.Parse("40mm SMA, SOMETHING ODD", errors);
        Assert.Empty(errors);
        Assert.Equal("SMA\nSOMETHING ODD", Assert.Single(layers).Description);
    }
}

public class MultiLayerLeaderTests
{
    [Fact]
    public void Leader_points_into_the_top_layer_not_onto_the_line_between_layers()
    {
        var b = new Buildup { Layers = BuildupParser.Parse("2 x 150mm DGB20 ROAD BASE") };
        var g = DetailLayout.Build(b, new DetailSettings(), CadStandard.CreateDefault(), 1);
        Assert.Equal(-75, g.Labels[0].Anchor.Y, 9);
        Assert.Equal(new[] { -150.0 }, g.LiftLevels);
    }
}
