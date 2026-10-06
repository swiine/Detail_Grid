using PavementBuildup.Core;

namespace PavementBuildup.Core.Tests;

public class MaterialLibraryTests
{
    private static readonly CadStandard Std = CadStandard.CreateDefault();

    [Theory]
    [InlineData("SMA 10 surface course", "AR-SAND")]
    [InlineData("AC 20 dense bin 40/60 binder", "AR-SAND")]
    [InlineData("AC 32 dense base 40/60", "ANSI31")]
    [InlineData("Type 1 sub-base", "GRAVEL")]
    [InlineData("Capping 6F2", "GRAVEL")]
    [InlineData("PQC C32/40 pavement quality concrete", "AR-CONC")]
    [InlineData("CBGM C8/10", "AR-CONC")]
    [InlineData("Concrete block paving", "ANSI37")]
    [InlineData("Bedding sand laying course", "AR-SAND")]
    [InlineData("Topsoil", "EARTH")]
    [InlineData("Something unusual", "ANSI31")]
    public void Picks_hatch_from_description(string description, string expected)
    {
        var spec = MaterialLibrary.Resolve(new PavementLayer { Description = description, ThicknessMm = 50 }, Std);
        Assert.Equal(expected, spec?.Pattern);
    }

    [Fact]
    public void Membranes_and_zero_thickness_are_not_hatched()
    {
        Assert.Null(MaterialLibrary.Resolve(new PavementLayer { Description = "Geotextile separator", ThicknessMm = 2 }, Std));
        Assert.Null(MaterialLibrary.Resolve(new PavementLayer { Description = "Type 1", ThicknessMm = 0 }, Std));
        Assert.Null(MaterialLibrary.Resolve(new PavementLayer { Description = "Polythene slip membrane", ThicknessMm = 1 }, Std));
    }

    [Fact]
    public void Explicit_pattern_and_angle_override_auto()
    {
        var spec = MaterialLibrary.Resolve(new PavementLayer { Description = "Type 1", ThicknessMm = 150, HatchPattern = "ansi31", HatchAngle = 90 }, Std);
        Assert.Equal("ANSI31", spec!.Pattern);
        Assert.Equal(90, spec.AngleDeg);

        Assert.Null(MaterialLibrary.Resolve(new PavementLayer { Description = "Type 1", ThicknessMm = 150, HatchPattern = "NONE" }, Std));
        Assert.Equal("MYPAT", MaterialLibrary.Resolve(new PavementLayer { Description = "x", ThicknessMm = 10, HatchPattern = "mypat" }, Std)!.Pattern);
    }
}
