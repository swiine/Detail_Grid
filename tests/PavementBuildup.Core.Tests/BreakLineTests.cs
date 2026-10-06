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
