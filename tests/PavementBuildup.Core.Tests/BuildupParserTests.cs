using PavementBuildup.Core;

namespace PavementBuildup.Core.Tests;

public class BuildupParserTests
{
    [Fact]
    public void Parses_semicolon_separated_courses_top_to_bottom()
    {
        var layers = BuildupParser.Parse("40 SMA 10 surface course; 60mm AC 20 dense bin 40/60 binder; 150 Type 1 sub-base");

        Assert.Equal(3, layers.Count);
        Assert.Equal(40, layers[0].ThicknessMm);
        Assert.Equal("SMA 10 surface course", layers[0].Description);
        Assert.Equal("AC 20 dense bin 40/60 binder", layers[1].Description); // 40/60 grade kept intact
        Assert.Equal(150, layers[2].ThicknessMm);
    }

    [Fact]
    public void Accepts_spaced_slash_newlines_and_trailing_thickness()
    {
        var layers = BuildupParser.Parse("SMA surface 40mm / Binder - 60 mm\n225mm: Type 1");

        Assert.Equal(new[] { 40.0, 60.0, 225.0 }, layers.Select(l => l.ThicknessMm));
        Assert.Equal("SMA surface", layers[0].Description);
        Assert.Equal("Binder", layers[1].Description);
        Assert.Equal("Type 1", layers[2].Description);
    }

    [Fact]
    public void Accepts_decimal_comma_and_zero_thickness_membrane()
    {
        var layers = BuildupParser.Parse("37,5 HRA; 0 Geotextile");
        Assert.Equal(37.5, layers[0].ThicknessMm);
        Assert.Equal(0, layers[1].ThicknessMm);
    }

    [Fact]
    public void Reports_courses_without_thickness()
    {
        var errors = new List<string>();
        var layers = BuildupParser.Parse("40 SMA; just some words; 150 Type 1", errors);

        Assert.Equal(2, layers.Count);
        Assert.Empty(errors);                                    // a line without thickness is a remark
        Assert.Equal("SMA\njust some words", layers[0].Description);
        Assert.Throws<FormatException>(() => BuildupParser.Parse("no numbers here"));
    }

    [Fact]
    public void Format_round_trips()
    {
        const string text = "40 SMA 10; 60 AC 20 binder; 0 Geotextile";
        var formatted = BuildupParser.Format(BuildupParser.Parse(text));
        Assert.Equal(text, formatted);
    }
}
