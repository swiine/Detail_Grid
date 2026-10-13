using PavementBuildup.Core.Design;

namespace PavementBuildup.Core.Tests;

/// <summary>
/// Every design calculation against the numbers the company workbooks give for the same inputs
/// ("Pavement build-ups" and "T48 and T34 Industrial Pavement Design").
/// </summary>
public class DesignTests
{
    private static void Near(double expected, double actual, double tol = 0.01) =>
        Assert.True(Math.Abs(expected - actual) <= tol, $"expected {expected}, got {actual}");

    [Fact]
    public void Austroads_charts_match_the_workbook()
    {
        Near(469.488, FlexibleDesign.Fig84(2, 100000));
        Near(469.262, FlexibleDesign.Fig122(2, 100000));
    }

    [Theory]
    // DESA, CBR, AC, base, subbase (company table cells)
    [InlineData(1e4, 1, "40 AC10", 150, 385)]
    [InlineData(1e4, 5, "40 AC10", 120, 125)]
    [InlineData(1e5, 6, "40 AC10", 115, 150)]
    [InlineData(1e6, 3, "50 AC14", 150, 370)]
    [InlineData(1e7, 10, "50 AC14", 160, 165)]
    public void Company_table_designs_come_back_for_its_own_inputs(double desa, double cbr, string ac, int dgb, int dgs)
    {
        var d = FlexibleDesign.Design(cbr, desa);
        Assert.Equal(ac, d.Surfacing);
        Assert.Equal(dgb, d.BaseMm);
        Assert.Equal(dgs, d.SubbaseMm);
        Assert.True(d.GranularMm + 0.5 >= d.Required);
    }

    [Fact]
    public void In_between_inputs_take_the_safe_side_of_the_table()
    {
        var d = FlexibleDesign.Design(4.5, 2e5);          // → 5x10^5 row, CBR 4% column
        Assert.Equal(150, d.BaseMm);
        Assert.Equal(260, d.SubbaseMm);
    }

    [Fact]
    public void Flexible_design_gives_a_build_up_the_reader_can_draw()
    {
        var calc = FlexibleDesign.Calc(5, 1e6, "CARPARK");
        var read = SmartReader.Read(calc.BuildupText, new List<string>());
        Assert.Equal("CARPARK", read.Parsed.Name);
        Assert.Equal(new[] { 50.0, 0, 150, 245 }, read.Parsed.Layers.Select(l => l.ThicknessMm));
        Assert.Contains("CBR 5%", read.Parsed.SubgradeText);
    }

    [Fact]
    public void Wheel_interior_matches_sheet_6()
    {
        var r = ConcreteDesign.Wheel(false, 32, 0.85, 10, 20, 1.2, 1.5, 2, 15, 100);
        Near(52000, r.Values["n"]);
        Near(3.1164, r.Values["f"], 0.0001);
        Near(133.596, r.Values["t50"]); Near(154.406, r.Values["t75"]); Near(175.216, r.Values["t100"]);
        Near(216.836, r.Values["t150"]); Near(258.456, r.Values["t200"]); Near(315.944, r.Values["t300"]);
        Near(373.432, r.Values["t400"]); Near(426.621, r.Values["t500"]); Near(479.811, r.Values["t600"]);
        Assert.Equal(180, r.ThicknessMm);
    }

    [Fact]
    public void Wheel_edge_matches_sheet_7()
    {
        var r = ConcreteDesign.Wheel(true, 32, 0.9, 40, 20, 1.05, 1.8, 9, 31.8, 100);
        Near(3.09881, r.Values["f"], 0.0001);
        Near(208.804, r.Values["t50"]); Near(264.669, r.Values["t100"]); Near(376.399, r.Values["t200"]);
        Near(560.135, r.Values["t400"]); Near(646.822, r.Values["t600"]);
    }

    [Fact]
    public void Posts_match_sheets_8_and_10()
    {
        var interior = ConcreteDesign.Post(false, 32, 0.8, 0.85, 2, 1.5, 5, 30, 70);
        Near(55.947, interior.Values["f"]);
        Near(210.308, interior.Values["t"]);
        var edge = ConcreteDesign.Post(true, 32, 0.8, 1, 2, 1.5, 5, 30, 70);
        Near(62.2878, edge.Values["f"]);
        Near(352.137, edge.Values["t"]);
    }

    [Fact]
    public void Punching_shear_matches_sheet_9()
    {
        var r = ConcreteDesign.PunchingShear(32, 165, 150, 84);
        Near(218.106, r.Values["phiVuo"]);
        Assert.True(r.Pass);
        Assert.False(ConcreteDesign.PunchingShear(32, 165, 150, 250).Pass);
    }

    [Fact]
    public void Udl_matches_sheets_11_and_12()
    {
        var t = ConcreteDesign.Udl(32, 0.8, 1000, 4, 7, 20.7, 30);
        Near(77.8891, t.Values["f"]);
        Near(295.536, t.Values["t"]);

        var d = ConcreteDesign.UdlDeflection(30100, 150, 30, 4, 0.25, 5, 7.5);
        Near(20.0548, d.Values["es"]);
        Near(1.71213, d.Values["lc"], 0.0001);
        Near(3.50811, d.Values["s2.5"]); Near(5.54834, d.Values["s5"]); Near(7.29718, d.Values["s10"]);
        Near(8.28746, d.Values["s20"]); Near(6.42276, d.Values["s"]);
    }

    [Theory]
    [InlineData(ConcreteDesign.WheelPosition.Interior, 11, 170, 40000, 2.3097, 752.959)]
    [InlineData(ConcreteDesign.WheelPosition.NearEdge, 20, 365, 400000, 1.96091, 1335.53)]
    [InlineData(ConcreteDesign.WheelPosition.OnEdge, 20, 400, 400000, 1.97741, 1430.47)]
    [InlineData(ConcreteDesign.WheelPosition.Corner, 20, 350, 400000, 1.97178, 1294.15)]
    public void T34_checks_match_sheets_13_to_16(ConcreteDesign.WheelPosition where, double tonnes, double h, double n, double stress, double l)
    {
        var r = ConcreteDesign.WheelStress(where, tonnes, 5, 32, 1, 0.15, h, 700, n);
        Near(stress, r.Values["stress"], 0.001);
        Near(l, r.Values["l"], 0.1);
        Assert.True(r.Pass);
        Assert.True(r.ThicknessMm <= h);
    }

    [Fact]
    public void Equivalent_modulus_matches_sheet_5()
    {
        var layers = new[]
        {
            new ConcreteDesign.SoilLayer(1.5, Cbr: 5, B: 1),
            new ConcreteDesign.SoilLayer(2.5, SptN: 12, Pi: 0, B: 0.8),
            new ConcreteDesign.SoilLayer(2, SptN: 22, Pi: 20, B: 0.6),
            new ConcreteDesign.SoilLayer(3, SptN: 35, Pi: 20, B: 0.6),
        };
        var r = ConcreteDesign.EquivalentModulus(layers, 1.8, 2, 2);
        Near(31.9908, r.Values["wheel"], 0.001);
        Near(26.527, r.Values["post"], 0.001);
        Near(26.8551, r.Values["udl"], 0.001);
    }

    [Fact]
    public void Soil_correlations_match_sheet_4()
    {
        Near(38.271, ConcreteDesign.KFromCbr(5), 0.001);
        Near(89.1, ConcreteDesign.KFromCbr(30), 0.001);
        Near(14.3529, ConcreteDesign.EsFromCbr(3), 0.0001);
        Near(3.1731, ConcreteDesign.CbrFromEs(15), 0.0001);
    }

    [Fact]
    public void A_calculation_prints_as_a_report()
    {
        var text = ConcreteDesign.PunchingShear(32, 165, 150, 84).ToText("Job 123", "A. Engineer");
        Assert.Contains("PUNCHING SHEAR", text);
        Assert.Contains("Job 123", text);
        Assert.Contains("RESULT: OK", text);
    }
}
