using PavementBuildup.Core;

namespace PavementBuildup.Core.Tests;

public class DetailNamingTests
{
    [Fact]
    public void Default_is_TTW_pavement_profile_numbered_from_1()
    {
        Assert.Equal("TTW_pavement-profile_#", CadStandard.CreateDefault().DetailNameFormat);
        Assert.Equal("TTW_pavement-profile_1", DetailNaming.Next(null, "Type A", Array.Empty<string>()));
    }

    [Fact]
    public void Uses_one_more_than_the_highest_existing_number_never_reusing_gaps()
    {
        var existing = new[] { "TTW_pavement-profile_1", "ttw_PAVEMENT-profile_7", "TTW_pavement-profile_3", "TTW_pavement-profile_x", "*Model_Space" };
        Assert.Equal("TTW_pavement-profile_8", DetailNaming.Next("TTW_pavement-profile_#", "A", existing));
    }

    [Fact]
    public void Supports_name_token_suffixes_and_no_hash()
    {
        Assert.Equal("DET-2-TYPE A", DetailNaming.Next("DET-#-{name}", "TYPE A", new[] { "DET-1-TYPE A" }));
        Assert.Equal("PAV_TYPE A_2", DetailNaming.Next("PAV_{name}", "TYPE A", new[] { "PAV_TYPE A" }));
        Assert.Equal("PAV_A_B", DetailNaming.Next("PAV_{name}", "A/B", Array.Empty<string>())); // invalid chars replaced
    }
}
