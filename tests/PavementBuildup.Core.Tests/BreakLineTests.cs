using PavementBuildup.Core;

namespace PavementBuildup.Core.Tests;

public class BreakLineTests
{
    [Fact]
    public void Default_standard_uses_BREAKLINE_with_the_TTW_block()
    {
        var s = CadStandard.CreateDefault();
        Assert.Equal("BREAKLINE", s.BreakLineMethod);
        Assert.Equal("TTW_stdCountry_Block_Break", s.BreakLineBlock);
        Assert.Empty(s.Validate());
    }

    [Fact]
    public void Script_sets_block_size_extension_then_picks_both_ends_in_WCS()
    {
        var script = BreakLineScript.Build("TTW_stdCountry_Block_Break", 0.05, 0,
            new[] { ((100.0, 200.0, 0.0), (100.0, 199.61, 0.0)) });

        Assert.Equal(
            "BREAKLINE\nBlock\nTTW_stdCountry_Block_Break\nSize\n0.05\nExtension\n0\n*100,200,0\n*100,199.61,0\n\n",
            script);
    }

    [Fact]
    public void Validates_break_line_settings()
    {
        var s = CadStandard.CreateDefault();
        s.BreakLineBlock = "BAD:NAME";
        s.BreakLineSizeMm = 0;
        var errors = s.Validate();
        Assert.Contains(errors, e => e.Contains("BAD:NAME"));
        Assert.Contains(errors, e => e.Contains("size"));

        s.BreakLineMethod = "BUILTIN"; // block/size only matter for BREAKLINE
        Assert.Empty(s.Validate());
    }
}
