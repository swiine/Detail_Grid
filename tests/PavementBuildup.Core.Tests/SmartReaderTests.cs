using PavementBuildup.Core;

namespace PavementBuildup.Core.Tests;

/// <summary>Build-ups written the many ways people actually write them.</summary>
public class SmartReaderTests
{
    private static SmartReadResult Read(string text)
    {
        var errors = new List<string>();
        var r = SmartReader.Read(text, errors);
        Assert.True(errors.Count == 0, string.Join(" | ", errors) + "  <=  " + r.Text);
        return r;
    }

    private static string Summary(SmartReadResult r) =>
        string.Join(" / ", r.Parsed.Layers.Select(l => (l.Lifts > 1 ? $"{l.Lifts}x" : "") + $"{l.ThicknessMm:0.##}:{BuildupParser.Core(l.Description).ToUpperInvariant()}"))
        + (r.Parsed.SubgradeText is { } s ? " | " + BuildupParser.Core(s).ToUpperInvariant() : "");

    [Theory]
    // Already tidy: unchanged.
    [InlineData("60mm THICK PAVERS, 30mm THICK MORTAR, 2 x 150mm THICK LAYERS OF DGB20 ROAD BASE, SUBGRADE COMPACTED TO 98% STANDARD MDD",
                "60:THICK PAVERS / 30:THICK MORTAR / 2x150:THICK LAYERS OF DGB20 ROAD BASE | SUBGRADE COMPACTED TO 98% STANDARD MDD")]
    // Thickness after the material.
    [InlineData("Pavers 60mm thick, mortar bed 30mm, DGB20 road base 150mm thick, subgrade",
                "60:THICK PAVERS / 30:MORTAR BED / 150:THICK DGB20 ROAD BASE | SUBGRADE")]
    // Metres and centimetres.
    [InlineData("0.04m SMA surface; 0.06 m AC20 binder; 15cm crushed rock base; formation",
                "40:SMA SURFACE / 60:AC20 BINDER / 150:CRUSHED ROCK BASE | FORMATION")]
    // Layer counts written out.
    [InlineData("40mm asphalt, two layers of 150mm DGB20, subgrade", "40:ASPHALT / 2x150:DGB20 | SUBGRADE")]
    [InlineData("40mm asphalt, 150mm x 2 layers DGB20, subgrade", "40:ASPHALT / 2x150:DGB20 | SUBGRADE")]
    [InlineData("40mm asphalt, 300mm DGB20 compacted in 2 layers, subgrade", "40:ASPHALT / 2x150:DGB20 | SUBGRADE")]
    [InlineData("40mm asphalt, 2 No. layers @ 150mm crushed rock, subgrade", "40:ASPHALT / 2x150:CRUSHED ROCK | SUBGRADE")]
    // Sentences and sequence words.
    [InlineData("Lay 25mm AC10 wearing course. Then 50mm AC14 base. Followed by 200mm crushed rock. Subgrade proof rolled.",
                "25:AC10 WEARING COURSE / 50:AC14 BASE / 200:CRUSHED ROCK | SUBGRADE PROOF ROLLED")]
    // Intro wording and a heading.
    [InlineData("Car park pavement comprising 30mm AC10 over 150mm FCR over subgrade",
                "30:AC10 / 150:FCR | SUBGRADE")]
    // Written bottom-up.
    [InlineData("Subgrade compacted to 98%, 200mm DGB20 sub-base, 40mm AC14 surface",
                "40:AC14 SURFACE / 200:DGB20 SUB-BASE | SUBGRADE COMPACTED TO 98%")]
    // Membrane with no thickness, min. qualifier, a total line.
    [InlineData("60mm pavers; 30mm bedding sand; geotextile fabric; min 150mm DGB20; total 240mm; existing ground",
                "60:PAVERS / 30:BEDDING SAND / 0:GEOTEXTILE FABRIC / 150:DGB20 | EXISTING GROUND")]
    // Range.
    [InlineData("40mm SMA, 100-150mm DGB20 (varies), subgrade", "40:SMA / 150:DGB20 | SUBGRADE")]
    // Bare numbers after separators.
    [InlineData("Wearing course - 40; Basecourse: 150; Subgrade", "40:WEARING COURSE / 150:BASECOURSE | SUBGRADE")]
    // Conversational, with filler, "of", "on top of", "laid on a ..." and "over the ...".
    [InlineData("the pavement will be 40mm of SMA on top of 60mm AC20 binder on top of 250mm type 1 sub-base laid on a geotextile membrane over the subgrade",
                "40:SMA / 60:AC20 BINDER / 250:TYPE 1 SUB-BASE / 0:GEOTEXTILE MEMBRANE | SUBGRADE")]
    // Numbered note copied from a drawing.
    [InlineData("PAVEMENT TYPE B\n1. 60mm THICK PAVERS\n2. 30mm VARIABLE SCREED, REFER TO DRAWING CV-TTW-1030\n3. ON EXISTING CONCRETE SLAB (LEVELS TBC ON SITE)",
                "60:THICK PAVERS / 30:VARIABLE SCREED | EXISTING CONCRETE SLAB")]
    // Lower case, inches-free imperial-ish, tack coat as a membrane.
    [InlineData("50mm hot mix asphalt; tack coat; 75mm asphalt base; 300mm crushed aggregate base (compacted to 95%); natural ground",
                "50:HOT MIX ASPHALT / 0:TACK COAT / 75:ASPHALT BASE / 300:CRUSHED AGGREGATE BASE | NATURAL GROUND")]
    public void Reads_messy_build_ups(string text, string expected) =>
        Assert.Equal(expected, Summary(Read(text)));

    [Fact]
    public void Reads_a_table_pasted_from_excel_with_a_metre_header()
    {
        var r = Read("Layer\tThickness (m)\nAsphalt AC14\t0.05\nDGB20 base\t0.15\nDGB20 subbase\t0.2\nSubgrade CBR 5\t-");
        Assert.Equal("50:ASPHALT AC14 / 150:DGB20 BASE / 200:DGB20 SUBBASE | SUBGRADE CBR 5", Summary(r));
        Assert.Contains(r.Notes, n => n.Contains("table"));
    }

    [Fact]
    public void Reads_a_pipe_table_with_thickness_first()
    {
        var r = Read("60 | Clay pavers\n30 | Sand bedding\n150 | Crushed rock\nSubgrade | compacted 95%");
        Assert.Equal("60:CLAY PAVERS / 30:SAND BEDDING / 150:CRUSHED ROCK | SUBGRADE COMPACTED 95%", Summary(r));
    }

    [Fact]
    public void Says_what_it_interpreted()
    {
        var r = Read("Subgrade, 0.15m crushed rock, 40mm AC14");
        Assert.Contains(r.Notes, n => n.Contains("0.15m") && n.Contains("150mm"));
        Assert.Contains(r.Notes, n => n.Contains("bottom-up"));
        Assert.Equal("40:AC14 / 150:CRUSHED ROCK | SUBGRADE", Summary(r));
    }

    [Fact]
    public void Leaves_drawing_references_paver_sizes_and_brackets_alone()
    {
        var r = Read("60mm 400 x 400 concrete pavers (0.06m, see spec), REFER TO DRAWING CV-TTW-1030, 150mm DGB20, subgrade");
        Assert.Equal("60:400 X 400 CONCRETE PAVERS / 150:DGB20 | SUBGRADE", Summary(r));
        Assert.Equal(1, r.Parsed.Layers[0].Lifts);
        Assert.Contains("(0.06m, see spec)", r.Parsed.Layers[0].Description);           // bracket note untouched
        Assert.Contains("REFER TO DRAWING CV-TTW-1030", r.Parsed.Layers[0].Description); // remark line, not 1030mm
    }

    [Fact]
    public void Keeps_reinforcement_and_square_bracket_titles()
    {
        var r = Read("[DRIVEWAY TYPE D] 150mm concrete [H12@200 c50] on 100mm crushed rock on subgrade");
        Assert.Equal("DRIVEWAY TYPE D", r.Parsed.Name);
        Assert.Single(r.Parsed.Layers[0].Reinforcement);
        Assert.Equal("150:CONCRETE / 100:CRUSHED ROCK | SUBGRADE", Summary(r));
    }

    [Theory]
    [InlineData("40 SMA 10 surface course; 60 AC 20 dense bin 40/60 binder course; 150 AC 32 dense base 40/60 base course; 225 Type 1 sub-base; 0 Geotextile separator")]
    [InlineData("250 PQC C32/40 [top H12@200 c40, H16@150 c50 + H10@300]; 0 Slip membrane; 150 Type 1 sub-base")]
    [InlineData("PAVEMENT TYPE A: 60mm THICK PAVERS (REFER TO LANDSCAPE SPECIFICATIONS), 30mm THICK MORTAR, SUBGRADE")]
    public void Tidy_text_reads_the_same_as_the_strict_reader(string text)
    {
        var strict = BuildupParser.ParseBuildup(text, new List<string>());
        var smart = Read(text);
        Assert.Equal(BuildupParser.Format(strict.Layers), BuildupParser.Format(smart.Parsed.Layers));
        Assert.Equal(strict.Name, smart.Parsed.Name);
        Assert.Equal(strict.SubgradeText, smart.Parsed.SubgradeText);
    }
}

public class SmartReaderHatchTests
{
    [Theory]
    [InlineData("50mm hot mix asphalt", "AR-SAND")]
    [InlineData("40mm DGA14", "AR-SAND")]
    [InlineData("60mm clay bricks", "ANSI37")]
    [InlineData("200mm lime stabilised subgrade layer", "AR-CONC")]
    [InlineData("150mm crushed rock", "GRAVEL")]
    [InlineData("25mm AC10 wearing course", "AR-SAND")]
    public void Common_material_words_get_a_sensible_hatch(string text, string pattern)
    {
        var layer = SmartReader.Read(text, new List<string>()).Parsed.Layers.Single();
        Assert.Equal(pattern, MaterialLibrary.Resolve(layer, CadStandard.CreateDefault())!.Pattern);
    }
}

public class RealWorldListTests
{
    private const string PortPavement = """
7. Heavy-Duty Industrial Pavement (Container Terminal / Port)

* Surface Layer: 100mm High-Strength Concrete Interlocking Pavers (80 MPa)
* Bedding Sand: 30mm Well-Graded Sharp Sand
* Base Course: 250mm Roller-Compacted Concrete (RCC) or Cement-Bound Granular Mixture (CBGM)
* Sub-base: 200mm Crushed Concrete / Recycled Sub-base aggregate
* Capping Layer: 200mm Stabilized Granular Soil
* Subgrade: Heavily proof-rolled natural subgrade
""";

    [Fact]
    public void Reads_a_bulleted_list_with_layer_names_before_colons()
    {
        var errors = new List<string>();
        var r = SmartReader.Read(PortPavement, errors);
        var std = CadStandard.CreateDefault();

        Assert.Empty(errors);
        Assert.Equal("Heavy-Duty Industrial Pavement (Container Terminal / Port)", r.Parsed.Name);
        Assert.Equal(new[]
        {
            "100:High-Strength Concrete Interlocking Pavers (80 MPa):ANSI37",
            "30:Well-Graded Sharp Sand:AR-SAND",
            "250:Roller-Compacted Concrete (RCC) or Cement-Bound Granular Mixture (CBGM):AR-CONC",
            "200:Crushed Concrete / Recycled Sub-base aggregate:GRAVEL",
            "200:Stabilized Granular Soil:AR-CONC",
        }, r.Parsed.Layers.Select(l => $"{l.ThicknessMm}:{l.Description}:{MaterialLibrary.Resolve(l, std)!.Pattern}"));
        Assert.Equal("Heavily proof-rolled natural subgrade", r.Parsed.SubgradeText);
        Assert.Equal(780, r.Parsed.Layers.Sum(l => l.TotalMm));
    }

    [Fact]
    public void Subgrade_label_is_kept_when_the_rest_does_not_say_subgrade()
    {
        var r = SmartReader.Read("Surface: 40mm AC14\nBase: 200mm DGB20\nSubgrade: CBR 5", new List<string>());
        Assert.Equal("Subgrade CBR 5", r.Parsed.SubgradeText);
        Assert.Equal(2, r.Parsed.Layers.Count);
    }

    [Fact]
    public void A_single_heading_with_a_colon_is_still_the_title()
    {
        var r = SmartReader.Read("PAVEMENT TYPE A: 60mm PAVERS, 30mm MORTAR, SUBGRADE", new List<string>());
        Assert.Equal("PAVEMENT TYPE A", r.Parsed.Name);
        Assert.Equal(2, r.Parsed.Layers.Count);
    }
}
