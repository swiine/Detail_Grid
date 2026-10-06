using PavementBuildup.Core;

namespace PavementBuildup.Core.Tests;

public class CadStandardTests
{
    private static CadStandard Company()
    {
        var s = CadStandard.CreateDefault();
        s.Name = "ACME";
        s.Layer(DetailElement.Outline).Name = "C-DETL-OTLN";
        s.Layer(DetailElement.Text).Name = "C-DETL-TEXT";
        s.HatchRules.Insert(0, new HatchRule
        {
            Name = "ACME asphalt", Keywords = "sma, binder", Pattern = "ACME-ASPH", Scale = 2, Angle = 15,
            Layer = "C-DETL-HTCH-ASPH", Color = "200,100,50", BackgroundColor = "254",
        });
        s.LabelFormat = "{description} ({thickness} THK)";
        s.TitleFormat = "DETAIL: {name}";
        s.ScaleFormat = "";
        return s;
    }

    [Fact]
    public void Default_standard_is_valid_and_has_a_layer_for_every_element()
    {
        var s = CadStandard.CreateDefault();
        Assert.Empty(s.Validate());
        Assert.Equal(Enum.GetValues<DetailElement>().Length, s.Layers.Count);
    }

    [Fact]
    public void Company_rules_win_and_carry_their_layer_and_colours_into_the_layout()
    {
        var b = new Buildup { Name = "Type A", Layers = BuildupParser.Parse("40 SMA surface; 150 Type 1") };
        var g = DetailLayout.Build(b, new DetailSettings(), Company(), 1);

        var asphalt = g.Bands[0].Hatch!;
        Assert.Equal("ACME-ASPH", asphalt.Pattern);
        Assert.Equal("C-DETL-HTCH-ASPH", asphalt.Layer);
        Assert.Equal("200,100,50", asphalt.Color);
        Assert.Equal("254", asphalt.BackgroundColor);
        Assert.Equal(15, asphalt.AngleDeg);
        Assert.Equal(2 * 10, g.Bands[0].HatchScale, 9); // rule scale x detail scale 1:10

        Assert.Equal("GRAVEL", g.Bands[1].Hatch!.Pattern); // falls through to the built-in rules
        Assert.Equal("", g.Bands[1].Hatch!.Layer);         // = the Hatch element layer
    }

    [Fact]
    public void Formats_control_label_title_and_dimension_wording()
    {
        var b = new Buildup { Name = "Type A", Layers = BuildupParser.Parse("40 SMA; 0 Geotextile") };
        var g = DetailLayout.Build(b, new DetailSettings(), Company(), 1);

        Assert.Equal("SMA (40 THK)", g.Labels[0].Text);
        Assert.Equal("GEOTEXTILE", g.Labels[1].Text);
        Assert.Equal("DETAIL: TYPE A", g.Titles[0].Text);
        Assert.DoesNotContain(g.Titles, t => t.Text.StartsWith("SCALE")); // blank format = line skipped
        Assert.Equal("40mm", g.Dimensions[0].Text);
    }

    [Fact]
    public void Text_heights_come_from_the_standard()
    {
        var s = CadStandard.CreateDefault();
        s.TextHeightMm = 3.5;
        s.TitleHeightMm = 5;
        var g = DetailLayout.Build(new Buildup { Layers = BuildupParser.Parse("40 SMA") }, new DetailSettings { ScaleDenominator = 20 }, s, 1);

        Assert.Equal(70, g.TextHeight, 9);
        Assert.Equal(100, g.Titles[0].Height, 9);
    }

    [Fact]
    public void Subgrade_hatch_comes_from_the_standard_and_can_be_switched_off()
    {
        var s = CadStandard.CreateDefault();
        s.SubgradeHatch = new HatchRule { Name = "Subgrade", Pattern = "NONE" };
        var g = DetailLayout.Build(new Buildup { Layers = BuildupParser.Parse("40 SMA") }, new DetailSettings(), s, 1);

        Assert.NotNull(g.Subgrade);
        Assert.Null(g.Subgrade!.Hatch);
    }

    [Fact]
    public void Validate_reports_bad_names_colours_and_scales()
    {
        var s = CadStandard.CreateDefault();
        s.Layer(DetailElement.Text).Name = "BAD:NAME";
        s.Layer(DetailElement.Outline).Color = "BYLAYER";
        s.HatchRules[0].Keywords = " , ";
        s.HatchRules[1].Scale = 0;
        s.HatchRules[1].Color = "300";

        var errors = s.Validate();
        Assert.Contains(errors, e => e.Contains("BAD:NAME"));
        Assert.Contains(errors, e => e.Contains("must be 1-255"));
        Assert.Contains(errors, e => e.Contains("never matches"));
        Assert.Contains(errors, e => e.Contains("scale must be greater"));
        Assert.Contains(errors, e => e.Contains("\"300\""));
    }

    [Theory]
    [InlineData("", ColorKind.ByLayer)]
    [InlineData("bylayer", ColorKind.ByLayer)]
    [InlineData("ByBlock", ColorKind.ByBlock)]
    [InlineData("7", ColorKind.Index)]
    [InlineData("255, 128,0", ColorKind.Rgb)]
    public void Parses_colours(string text, ColorKind kind)
    {
        Assert.True(ColorSpec.TryParse(text, out var c));
        Assert.Equal(kind, c.Kind);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("256")]
    [InlineData("red")]
    [InlineData("1,2")]
    [InlineData("1,2,300")]
    public void Rejects_bad_colours(string text) => Assert.False(ColorSpec.TryParse(text, out _));

    [Fact]
    public void Round_trips_through_json_and_fills_in_missing_layers()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            string path = Path.Combine(dir.FullName, "acme.json");
            StandardStore.Save(Company(), path);
            Assert.Contains("\"Element\": \"Outline\"", File.ReadAllText(path)); // readable enum names

            var loaded = StandardStore.Load(path);
            Assert.Equal("ACME", loaded.Name);
            Assert.Equal("C-DETL-OTLN", loaded.Layer(DetailElement.Outline).Name);
            Assert.Equal("ACME asphalt", loaded.HatchRules[0].Name);

            // A hand-edited file missing most things still loads with defaults.
            File.WriteAllText(path, "{ \"Name\": \"Minimal\", // comment\n \"Layers\": [ { \"Element\": \"Title\", \"Name\": \"T\" } ], }");
            var minimal = StandardStore.Load(path);
            Assert.Equal("T", minimal.Layer(DetailElement.Title).Name);
            Assert.Equal("PAV-OUTLINE", minimal.Layer(DetailElement.Outline).Name);
            Assert.Equal(Enum.GetValues<DetailElement>().Length, minimal.Layers.Count);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Missing_company_file_is_an_error_not_a_silent_fallback()
    {
        Assert.Throws<FileNotFoundException>(() => StandardStore.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
    }

    [Fact]
    public void All_layer_names_lists_element_and_rule_layers_once()
    {
        var names = Company().AllLayerNames().ToList();
        Assert.Contains("C-DETL-OTLN", names);
        Assert.Contains("C-DETL-HTCH-ASPH", names);
        Assert.Single(names, n => n == "PAV-TEXT"); // still used by Leader, listed once
    }
}

public class ExampleStandardFileTests
{
    [Fact]
    public void Shipped_example_company_standard_loads_and_is_valid()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "standards")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);

        var s = StandardStore.Load(Path.Combine(dir!, "standards", "example-company-standard.json"));
        Assert.Empty(s.Validate());
        Assert.Equal("C-DETL-OTLN", s.Layer(DetailElement.Outline).Name);
    }
}
