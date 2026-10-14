using PavementBuildup.Core.Design;

namespace PavementBuildup.Core.Tests;

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
        var d = FlexibleDesign.Design(4.5, 2e5);
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

public class CombinedDesignTests
{
    [Fact]
    public void Road_design_from_traffic_counts()
    {
        var t = RoadDesign.Traffic(new TrafficInputs(0, 2000, 10, 0.5, 1, 2, 20, 1));
        Assert.Equal(100, t.Values["hvday"], 6);
        Assert.Equal(24.297, t.Values["cgf"], 3);
        Assert.Equal(365 * 100 * t.Values["cgf"], t.Values["desa"], 3);

        var r = RoadDesign.Design(5, new TrafficInputs(0, 2000, 10, 0.5, 1, 2, 20, 1), "ACCESS ROAD");
        Assert.True(r.Pass);
        Assert.NotNull(r.BuildupText);
        Assert.Contains(r.Parts, p => p.Title == "Design traffic");
        Assert.Equal(2, r.Tables.Count);
    }

    [Fact]
    public void Road_design_with_a_known_desa_matches_the_flexible_design()
    {
        var r = RoadDesign.Design(5, new TrafficInputs(1e6));
        var d = FlexibleDesign.Design(5, 1e6);
        Assert.Equal(d.BaseMm, r.Values["base"]);
        Assert.Equal(d.SubbaseMm, r.Values["subbase"]);
    }

    [Fact]
    public void Slab_design_takes_the_worst_of_the_checks()
    {
        var s = new SlabInputs { Fc = 32, Cbr = 5, SoilDepth = 5, AxleTonnes = 10, Posts = true, Udl = true };
        var r = SlabDesign.Design(s, "WAREHOUSE");
        Assert.True(r.Pass);
        var t48 = r.Parts.Where(p => p.Title.StartsWith("Wheel loading") || p.Title.StartsWith("Post loading") || p.Title.StartsWith("Uniformly"))
            .Max(p => p.ThicknessMm!.Value);
        Assert.True(r.ThicknessMm >= t48);
        Assert.Contains("CONCRETE PAVEMENT", r.BuildupText);
        Assert.Single(r.Tables);
        Assert.Equal(4, r.Tables[0].Rows.Count);
        Assert.True(ConcreteDesign.PunchingShear(32, s.BasePlateMm, r.ThicknessMm!.Value, s.PostKn * s.LoadFactor).Pass);
    }

    [Fact]
    public void Slab_design_needs_a_load() =>
        Assert.Throws<ArgumentException>(() => SlabDesign.Design(new SlabInputs { Wheels = false }));

    [Fact]
    public void The_report_is_a_complete_page()
    {
        var html = CalcReport.Html(SlabDesign.Design(new SlabInputs { Posts = true }), "Job 123", "A. Engineer");
        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("Job 123", html);
        Assert.Contains("Wheel loading - interior", html);
        Assert.Contains("Punching shear", html);
        Assert.EndsWith("</html>", html);
    }

    [Theory]
    [InlineData("1e6", "ESA", 1e6)]
    [InlineData("1 x 10^6", "ESA", 1e6)]
    [InlineData("100,000", "ESA", 1e5)]
    [InlineData("5%", "%", 5)]
    [InlineData("20t", "kN", 196.2)]
    [InlineData("196.2 kN", "tonne", 20)]
    [InlineData("150mm", "m", 0.15)]
    [InlineData("1.5", "m", 1.5)]
    public void Inputs_read_the_way_engineers_type(string text, string unit, double expected)
    {
        Assert.True(Inputs.TryRead(text, unit, out var v, out var error), error);
        Assert.Equal(expected, v, 6);
    }

    [Fact]
    public void Inputs_say_what_is_wrong()
    {
        Assert.False(Inputs.TryRead("abc", "m", out _, out var e1));
        Assert.Contains("isn't a number", e1);
        Assert.False(Inputs.TryRead("20t", "m", out _, out var e2));
        Assert.Contains("this box is in m", e2);
    }
}

public class CompanyStandardTests
{
    [Fact]
    public void The_TTW_standard_loads()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "standards", "TTW.json"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var std = StandardStore.Load(Path.Combine(dir!, "standards", "TTW.json"));
        Assert.Equal("TTW", std.Name);
        Assert.Equal("Pen-50-c", std.Layer(DetailElement.Membrane).Name);
        Assert.Equal("TTW_stdGlobal_Text_25", std.TextStyle);
        Assert.Equal("PEN-GREY-H", std.Layer(DetailElement.ExistingHatch).Name);
        var g = DetailLayout.Build(new Buildup { Name = "TEST", Layers = BuildupParser.Parse("40mm AC10, 150mm DGB20") }, new DetailSettings(), std, 0.001);
        Assert.NotEmpty(g.Bands);
    }
}

[Collection("StandardStore")]
public class DefaultStandardTests
{
    [Fact]
    public void The_default_is_TTW_json_in_support_standards_and_can_be_changed()
    {
        var root = Path.Combine(Path.GetTempPath(), "ttw-release-" + Guid.NewGuid().ToString("N"));
        var app = Path.Combine(root, "Standalone Tool");
        var standards = Path.Combine(root, "_support", "_Standards");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(standards);
        var ttw = Path.Combine(standards, "TTW.json");
        File.WriteAllText(ttw, StandardStore.Serialize(CadStandard.CreateDefault()));
        try
        {
            StandardStore.SearchFrom.Clear();
            StandardStore.SearchFrom.Add(app);
            Assert.True(StandardStore.SamePath(ttw, StandardStore.CompanyDefault));
            Assert.True(StandardStore.SamePath(ttw, StandardStore.PathFor(new DetailSettings())));
            Assert.Equal(@"X:\other.json", StandardStore.PathFor(new DetailSettings { StandardPath = @"X:\other.json" }));
            Assert.Equal("", StandardStore.ToSetting(ttw));
        }
        finally
        {
            StandardStore.SearchFrom.Clear();
            Directory.Delete(root, true);
        }
    }
}

public class TemplateDrawingTests
{
    [Fact]
    public void The_template_is_found_next_to_the_standard()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ttw-std-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var std = CadStandard.CreateDefault();
            std.StandardDrawing = "";
            var json = Path.Combine(dir, "TTW.json");
            StandardStore.Save(std, json);
            Assert.Null(StandardStore.Load(json).ResolveStandardDrawing());

            File.WriteAllText(Path.Combine(dir, CadStandard.DefaultTemplate), "dwg");
            var loaded = StandardStore.Load(json);
            Assert.True(StandardStore.SamePath(Path.Combine(dir, "TTW_Pave_Temp.dwg"), loaded.ResolveStandardDrawing()));

            loaded.StandardDrawing = "TTW_Pave_Temp.dwg";
            Assert.True(StandardStore.SamePath(Path.Combine(dir, "TTW_Pave_Temp.dwg"), loaded.ResolveStandardDrawing()));
            var elsewhere = Path.Combine(Path.GetTempPath(), "company", "other.dwt");
            loaded.StandardDrawing = elsewhere;
            Assert.Equal(elsewhere, loaded.ResolveStandardDrawing());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_TTW_standard_names_the_template() =>
        Assert.Equal("TTW_Pave_Temp.dwg", StandardStore.Parse(File.ReadAllText(FindRepoFile("standards", "TTW.json"))).StandardDrawing);

    private static string FindRepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(new[] { dir }.Concat(parts).ToArray()))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(new[] { dir! }.Concat(parts).ToArray());
    }
}

public class SubgradeNoteTests
{
    [Theory]
    [InlineData("SUBGRADE", "SUBGRADE COMPACTED TO 98% SMDD AT ±2% OMC")]
    [InlineData("natural subgrade", "SUBGRADE COMPACTED TO 98% SMDD AT ±2% OMC")]
    [InlineData("Formation", "SUBGRADE COMPACTED TO 98% SMDD AT ±2% OMC")]
    [InlineData("SUBGRADE COMPACTED TO 95% MDD", "SUBGRADE COMPACTED TO 95% MDD")]
    [InlineData("Heavily proof-rolled natural subgrade", "Heavily proof-rolled natural subgrade")]
    [InlineData("EXISTING CONCRETE SLAB", "EXISTING CONCRETE SLAB")]
    [InlineData("SUBGRADE\nREFER TO GEOTECH REPORT", "SUBGRADE\nREFER TO GEOTECH REPORT")]
    public void A_bare_subgrade_gets_the_compaction_note(string written, string label) =>
        Assert.Equal(label, DetailLayout.SubgradeLabel(written, CadStandard.CreateDefault()));

    [Fact]
    public void The_note_can_be_turned_off()
    {
        var std = CadStandard.CreateDefault();
        std.SubgradeNote = "";
        Assert.Equal("SUBGRADE", DetailLayout.SubgradeLabel("SUBGRADE", std));
    }
}

public class ExcelReportTests
{
    [Fact]
    public void The_report_is_also_an_excel_workbook_linked_to_the_html()
    {
        var design = SlabDesign.Design(new SlabInputs { Posts = true, Udl = true }, "WAREHOUSE");
        var dir = Path.Combine(Path.GetTempPath(), "ttw-xl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var xlsx = Path.Combine(dir, "slab design.xlsx");
            PavementBuildup.Reports.ExcelReport.Save(design, xlsx, "Job 1", "A. Engineer", "B. Checker", "slab design.html");
            using var wb = new ClosedXML.Excel.XLWorkbook(xlsx);
            Assert.Equal("Summary", wb.Worksheet(1).Name);
            Assert.Contains(wb.Worksheets, w => w.Name == "What if");
            Assert.Contains(wb.Worksheets, w => w.Name == "1 Wheels interior");
            var summary = wb.Worksheet("Summary");
            var link = summary.CellsUsed().First(c => c.HasHyperlink && c.GetHyperlink().ExternalAddress is not null);
            Assert.Equal("slab design.html", Uri.UnescapeDataString(link.GetHyperlink().ExternalAddress.OriginalString));
            Assert.Contains(summary.CellsUsed(), c => c.DataType == ClosedXML.Excel.XLDataType.Number && c.GetDouble() == 32);

            var html = CalcReport.Html(design, "Job 1", "A. Engineer", null, "slab design.xlsx");
            Assert.Contains("href=\"slab%20design.xlsx\"", html);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Sheet_names_fit_excel() =>
        Assert.True(PavementBuildup.Reports.ExcelReport.SheetName(12, "Wheel stress - close to edge (Ioannides) [T34]").Length <= 31);
}
