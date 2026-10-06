using PavementBuildup.Core;

namespace PavementBuildup.Core.Tests;

/// <summary>The note that failed in Civil 3D, and the bracket / "2 x" leader rules.</summary>
public class NoteWordingTests
{
    private const string Note =
        "60mm THICK PAVERS, REFER TO LANDSCAPE SPECIFICATIONS, ON 30mm VARIABLE SCREED, " +
        "REFER TO DRAWING CV-TTW-1030, ON EXISTING CONCRETE SLAB (LEVELS TBC ON SITE)";

    [Theory]
    [InlineData(Note)]
    [InlineData("60mm THICK PAVERS\nREFER TO LANDSCAPE SPECIFICATIONS\nON 30mm VARIABLE SCREED\nREFER TO DRAWING CV-TTW-1030\nON EXISTING CONCRETE SLAB (LEVELS TBC ON SITE)")]
    [InlineData("60mm THICK PAVERS, REFER TO LANDSCAPE SPECIFICATIONS ON 30mm VARIABLE SCREED, REFER TO DRAWING CV-TTW-1030 ON EXISTING CONCRETE SLAB (LEVELS TBC ON SITE)")]
    public void Reads_remarks_ON_courses_and_an_existing_base(string note)
    {
        var errors = new List<string>();
        var r = BuildupParser.ParseBuildup(note, errors);

        Assert.Empty(errors);
        Assert.Equal(2, r.Layers.Count);
        Assert.Equal((60.0, "THICK PAVERS\nREFER TO LANDSCAPE SPECIFICATIONS"), (r.Layers[0].ThicknessMm, r.Layers[0].Description));
        Assert.Equal((30.0, "VARIABLE SCREED\nREFER TO DRAWING CV-TTW-1030"), (r.Layers[1].ThicknessMm, r.Layers[1].Description));
        Assert.Equal("EXISTING CONCRETE SLAB (LEVELS TBC ON SITE)", r.SubgradeText);
    }

    [Fact]
    public void Draws_remarks_as_second_label_lines_and_hatches_the_existing_slab_as_concrete()
    {
        var b = BuildupParser.ParseBuildup(Note, new List<string>()).ToBuildup("TYPE P");
        var g = DetailLayout.Build(b, new DetailSettings(), CadStandard.CreateDefault(), 0.001);

        Assert.Equal(new[]
        {
            "60mm THICK PAVERS\nREFER TO LANDSCAPE SPECIFICATIONS",
            "30mm VARIABLE SCREED\nREFER TO DRAWING CV-TTW-1030",
            "EXISTING CONCRETE SLAB (LEVELS TBC ON SITE)",
        }, g.Labels.Select(l => l.Text));
        Assert.Equal(new[] { "ANSI37", "AR-SAND" }, g.Bands.Select(x => x.Hatch!.Pattern));
        Assert.Equal("AR-CONC", g.Subgrade!.Hatch!.Pattern);

        // Two-line labels get more room than one-liners.
        double gap = g.Labels[0].TextAt.Y - g.Labels[1].TextAt.Y;
        Assert.True(gap >= 2 * 1.6 * g.TextHeight);
    }

    [Fact]
    public void Brackets_are_ignored_for_reading_and_kept_on_the_label()
    {
        var errors = new List<string>();
        var r = BuildupParser.ParseBuildup(
            "60mm THICK PAVERS (REFER TO LANDSCAPE SPEC, 80mm IN DRIVEWAYS), 2x 150mm ROAD BASE (DGB20, ON 5mm GEOFABRIC), SUBGRADE (CBR 5)", errors);

        Assert.Empty(errors);
        Assert.Equal(2, r.Layers.Count);                         // nothing in brackets split or added a course
        Assert.Equal(60, r.Layers[0].ThicknessMm);               // not 80
        Assert.Equal("THICK PAVERS (REFER TO LANDSCAPE SPEC, 80mm IN DRIVEWAYS)", r.Layers[0].Description);
        Assert.Equal((150.0, 2, "ROAD BASE (DGB20, ON 5mm GEOFABRIC)"), (r.Layers[1].ThicknessMm, r.Layers[1].Lifts, r.Layers[1].Description));
        Assert.Equal("SUBGRADE (CBR 5)", r.SubgradeText);

        // "(... GEOFABRIC)" would match the membrane rule; brackets don't pick the hatch.
        Assert.Equal("GRAVEL", MaterialLibrary.Resolve(r.Layers[1], CadStandard.CreateDefault())!.Pattern);
    }

    [Fact]
    public void Two_x_course_has_a_leader_branch_to_each_layer()
    {
        var b = new Buildup { Layers = BuildupParser.Parse("40mm SMA, 2x 150mm ROAD BASE") };
        var g = DetailLayout.Build(b, new DetailSettings(), CadStandard.CreateDefault(), 1);

        var roadBase = g.Labels[1];
        Assert.Equal("2 x 150mm ROAD BASE", roadBase.Text);
        Assert.Equal(-40 - 75, roadBase.Anchor.Y, 9);                         // middle of the first layer
        Assert.Equal(new[] { -40 - 225.0 }, roadBase.ExtraAnchors.Select(a => a.Y)); // middle of the second
        Assert.Empty(g.Labels[0].ExtraAnchors);
    }

    [Fact]
    public void Remarks_round_trip_through_quick_entry_text()
    {
        var r = BuildupParser.ParseBuildup(Note, new List<string>());
        var text = BuildupParser.Format(r.ToBuildup());
        var again = BuildupParser.ParseBuildup(text, new List<string>());

        Assert.Equal(r.Layers.Select(l => l.Description), again.Layers.Select(l => l.Description));
        Assert.Equal(r.SubgradeText, again.SubgradeText);
    }
}
