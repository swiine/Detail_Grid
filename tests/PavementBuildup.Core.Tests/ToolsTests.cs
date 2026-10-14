using System.Text;
using PavementBuildup.Core.Design;
using PavementBuildup.Export;
using PavementBuildup.Reports;

namespace PavementBuildup.Core.Tests;

public class ToolsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ttw-tools-" + Guid.NewGuid().ToString("N"));

    public ToolsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static DesignFile Road(string name, string cbr = "5", string desa = "1 x 10^5", string job = "241001") => new()
    {
        Kind = "Road", JobNo = job, PavementName = name, Project = "Test job", Desa = desa, Fields = { ["cbr"] = cbr },
    };

    private static DesignFile Slab(string name) => new()
    {
        Kind = "Slab", JobNo = "241001", PavementName = name, Fields = { ["cbr"] = "5", ["jl"] = "30", ["jw"] = "20" },
        Checks = { ["joints"] = true },
    };

    [Fact]
    public void A_design_file_gives_the_same_answer_as_the_calculation()
    {
        var file = Road("Type A", "4", "5e5");
        var direct = RoadDesign.Design(4, new TrafficInputs(5e5), "TYPE A");
        var run = file.Run();
        Assert.Equal(direct.Headline, run.Headline);
        Assert.Equal(direct.BuildupText, run.BuildupText);
    }

    [Fact]
    public void A_design_file_reads_the_wizards_table_rows_and_typed_traffic()
    {
        Assert.Equal(1e6, Road("x", desa: "1 x 10^6  -  Collector road with heavy vehicles").DesaValue());
        Assert.Equal(2e5, Road("x", desa: "2e5").DesaValue());
        var bad = Road("x", cbr: "five");
        var ex = Assert.Throws<ArgumentException>(() => bad.Run());
        Assert.Contains("Subgrade CBR", ex.Message);
    }

    [Fact]
    public void Old_design_files_without_the_new_fields_still_open()
    {
        const string old = """{"Kind":"Slab","Project":"P","JobNo":"1","PavementName":"Floor","Engineer":"","Checker":"","Desa":"","FromCounts":false,"Grade":1,"Fields":{"cbr":"5","axle":"8"},"Checks":{"wheels":true}}""";
        var file = DesignFile.FromJson(old);
        var r = file.Run();
        Assert.NotNull(r.ThicknessMm);
        Assert.DoesNotContain(r.Parts, p => p.Title == "Joint layout");
        Assert.Equal(SlabKind.Unreinforced, file.JointKind);
    }

    [Fact]
    public void A_slab_with_joints_ticked_gets_the_joint_layout_in_its_calculation()
    {
        var r = Slab("Warehouse").Run();
        var joints = Assert.Single(r.Parts, p => p.Title == "Joint layout");
        Assert.Equal(r.ThicknessMm, joints.ThicknessMm);
        Assert.Contains(r.Lines, l => l.Label == "Joint layout");
    }

    [Fact]
    public void Costs_add_up_layer_by_layer_and_subbase_is_not_priced_as_base()
    {
        var rates = RateTable.CreateDefault();
        var read = SmartReader.Read("40mm AC10 wearing course\nprime\n150mm DGB20 base\n200mm DGS20/40 subbase\nsubgrade", new List<string>());
        var cost = CostEstimate.For(read.Parsed.ToBuildup("A"), rates, 1000);
        Assert.Equal("DGS20/40 subbase", rates.For("200mm COMPACTED THICKNESS DGS20/40 SUBBASE")!.Name);
        double expected = 210 * 0.040 * 2.45 + 3.5 + 110 * 0.150 + 95 * 0.200 + rates.SubgradePrepPerM2;
        Assert.Equal(expected, cost.Values["cost_m2"], 6);
        Assert.Equal(expected * 1000, cost.Values["cost_total"], 6);
    }

    [Fact]
    public void A_layer_without_a_rate_is_flagged_not_hidden()
    {
        var read = SmartReader.Read("50mm unobtainium\nsubgrade", new List<string>());
        var cost = CostEstimate.For(read.Parsed.ToBuildup("A"), RateTable.CreateDefault());
        Assert.Contains(cost.Lines, l => l.Kind == LineKind.Warning && l.Value.Contains("unobtainium", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rates_file_round_trips_and_pavers_are_not_priced_as_concrete()
    {
        var path = Path.Combine(_dir, RateTable.FileName);
        RateTable.CreateDefault().Save(path);
        var rates = RateTable.Load(path);
        Assert.Equal(path, rates.SourcePath);
        Assert.Equal("Concrete pavers", rates.For("80mm CONCRETE PAVERS")!.Name);
        Assert.Equal("Asphalt", rates.For("50mm ASPHALT CONCRETE")!.Name);
        Assert.Equal("DGB20 base", rates.For("150mm UNBOUND GRANULAR BASE")!.Name);
        Assert.Equal("Concrete", rates.For("200mm F'c 32MPa CONCRETE PAVEMENT")!.Name);
    }

    [Fact]
    public void A_design_run_with_rates_carries_its_cost()
    {
        var r = Road("Type A").Run(RateTable.CreateDefault());
        Assert.True(r.Values["cost_m2"] > 0);
        Assert.Contains(r.Parts, p => p.Title == "Rough cost estimate");
    }

    [Theory]
    [InlineData(30, 20, 200, SlabKind.Unreinforced, 6, 4)]
    [InlineData(12, 2.5, 200, SlabKind.Unreinforced, 4, 1)]
    [InlineData(10, 3, 150, SlabKind.Unreinforced, 3, 1)]
    [InlineData(40, 30, 300, SlabKind.Unreinforced, 7, 5)]
    public void Joint_layout_keeps_panels_under_the_spacing_and_near_square(double l, double w, double t, SlabKind kind, int nx, int ny)
    {
        var p = JointLayout.Plan(new JointInputs { LengthM = l, WidthM = w, ThicknessMm = t, Kind = kind });
        Assert.Equal(nx, p.PanelsAlong);
        Assert.Equal(ny, p.PanelsAcross);
        Assert.True(p.Aspect <= 1.5 + 1e-9);
        Assert.True(Math.Max(p.PanelLengthM, p.PanelWidthM) <= p.SpacingLimitM + 1e-9);
        Assert.Equal(nx - 1, p.CutsAlongX.Count);
    }

    [Fact]
    public void Joint_spacing_can_be_set_and_sawcuts_are_a_quarter_deep()
    {
        var p = JointLayout.Plan(new JointInputs { LengthM = 20, WidthM = 8, ThicknessMm = 180, MaxSpacingM = 4 });
        Assert.Equal(4, p.SpacingLimitM);
        Assert.Equal(5, p.PanelsAlong);
        Assert.Equal(2, p.PanelsAcross);
        Assert.Equal(45, p.SawcutDepthMm);
        Assert.Equal(4 * 8 + 1 * 20, p.SawcutLengthM, 6);
    }

    [Fact]
    public void A_council_rule_set_changes_the_spacing_and_panel_shape()
    {
        var path = Path.Combine(_dir, JointRulesFile.FileName);
        JointRulesFile.CreateDefault().Save(path);
        var council = System.Text.Json.JsonSerializer.Deserialize<JointRulesFile>(File.ReadAllText(path))!.Find("Example council (edit me)");
        Assert.Equal(1.25, council.AspectLimit);
        var p = JointLayout.Plan(new JointInputs { LengthM = 30, WidthM = 20, ThicknessMm = 200, Rules = council });
        Assert.Equal(4.5, p.SpacingLimitM);
        Assert.Equal(7, p.PanelsAlong);
        Assert.Equal(5, p.PanelsAcross);
        Assert.Contains("Example council", JointLayout.Calc(new JointInputs { Rules = council }).Reference);
        Assert.Equal("TTW rule of thumb", JointRulesFile.CreateDefault().Find("no such council").Name);
    }

    [Fact]
    public void Joint_plan_saves_as_dxf_and_pdf()
    {
        var plan = JointLayout.Plan(new JointInputs { LengthM = 30, WidthM = 20, ThicknessMm = 200 });
        var dxf = Path.Combine(_dir, "joints.dxf");
        using (var fs = File.Create(dxf)) JointDxf.Write(plan, CadStandard.CreateDefault(), 200, fs);
        var doc = netDxf.DxfDocument.Load(dxf);
        Assert.Equal(5 + 3, doc.Entities.Lines.Count());
        using var ms = new MemoryStream();
        PdfExport.WriteJoints(plan, "SLAB JOINTS", new PdfSheet { JobNumber = "241001" }, ms);
        AssertPdf(ms.ToArray(), 1);
    }

    [Fact]
    public void Compare_puts_options_side_by_side_and_names_the_thinnest_and_cheapest()
    {
        var rates = RateTable.CreateDefault();
        var a = Road("Option 1 - CBR 3", "3");
        var b = Road("Option 2 - CBR 10 (stabilised)", "10");
        var options = new[] { a, b }.Select(f => new DesignOption(f.Title, f, f.Run(rates))).ToList();
        var r = DesignCompare.Compare(options);
        var (_, columns, rows) = Assert.Single(r.Tables);
        Assert.Equal(new[] { "", a.Title, b.Title }, columns);
        Assert.Contains(rows, row => row[0] == "Cost per m²" && row[1].StartsWith('$'));
        Assert.Contains("Option 2", r.Headline);
        Assert.Equal(2, r.Parts.Count);
        Assert.Throws<ArgumentException>(() => DesignCompare.Compare(options.Take(1).ToList()));

        var xlsx = Path.Combine(_dir, "compare.xlsx");
        ExcelReport.Save(r, xlsx, "Test", "", "", null);
        using var wb = new ClosedXML.Excel.XLWorkbook(xlsx);
        Assert.True(wb.Worksheets.Contains("Comparison"));
    }

    [Fact]
    public void Register_finds_a_jobs_designs_on_any_day_and_keeps_the_latest_of_each()
    {
        var exports = Path.Combine(_dir, "_Exports");
        string Save(string day, DesignFile f, string file, bool report = false)
        {
            var folder = Path.Combine(exports, day, f.JobNo, f.Title);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, file + DesignFile.Extension);
            f.Save(path);
            if (report) File.WriteAllText(Path.ChangeExtension(path, ".html"), "<html></html>");
            return path;
        }
        var older = Save("2026-10-01", Road("Type A", "3"), "a");
        File.SetLastWriteTime(older, new DateTime(2026, 10, 1));
        Save("2026-10-14", Road("Type A", "5"), "a", report: true);
        Save("2026-10-14", Slab("Warehouse"), "w");
        Save("2026-10-14", Road("Other job", job: "999"), "o");

        var entries = ProjectRegister.Scan(exports, "241001", RateTable.CreateDefault());
        Assert.Equal(new[] { "Type A", "Warehouse" }, entries.Select(e => e.Name));
        var a = entries[0];
        Assert.Equal("5%", a.Cbr);
        Assert.NotNull(a.ReportHtml);
        Assert.NotNull(a.CostPerM2);
        Assert.Equal(4, ProjectRegister.Scan(exports, "", null, allVersions: true).Count);

        var xlsx = Path.Combine(_dir, "register.xlsx");
        RegisterReport.Save(entries, xlsx, "241001", "Test job");
        using var wb = new ClosedXML.Excel.XLWorkbook(xlsx);
        var ws = wb.Worksheet("Register");
        Assert.Equal("Type A", ws.Cell(5, 1).GetString());
        Assert.True(ws.Cell(5, 10).HasHyperlink);
    }

    private static void AssertPdf(byte[] bytes, int pages)
    {
        var text = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains($"/Count {pages}", text);
        int xref = int.Parse(text[(text.LastIndexOf("startxref", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        Assert.StartsWith("xref", text[xref..]);
        var offsets = text[xref..].Split('\n').Skip(3).TakeWhile(l => l.EndsWith(" n ", StringComparison.Ordinal)).Select(l => int.Parse(l[..10])).ToList();
        for (int i = 0; i < offsets.Count; i++) Assert.StartsWith($"{i + 1} 0 obj", text[offsets[i]..]);
    }

    [Fact]
    public void Detail_pdf_is_a_valid_one_page_file()
    {
        var read = SmartReader.Read("[TYPE A - CARPARK]\n40mm AC10\nprime\n150mm DGB20 base\n200mm DGS20 subbase\nsubgrade", new List<string>());
        using var ms = new MemoryStream();
        PdfExport.Write(read.Parsed.ToBuildup("TYPE A - CARPARK"), new DetailSettings { ScaleDenominator = 10 }, CadStandard.CreateDefault(),
            new PdfSheet { Project = "Smith St", JobNumber = "241001" }, ms);
        AssertPdf(ms.ToArray(), 1);
    }

    [Fact]
    public void Batch_template_reads_back_as_three_details_that_save_to_one_dxf_and_pdf()
    {
        var path = Path.Combine(_dir, "batch.xlsx");
        BatchSheet.WriteTemplate(path);
        var items = BatchSheet.Read(path);
        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.NotNull(i.Buildup));
        Assert.Equal("TYPE A - CARPARK", items[0].Buildup!.Name);
        Assert.Equal(3, items[0].Buildup!.Layers.Count(l => l.TotalMm > 0));
        Assert.Contains("concrete", items[1].Buildup!.Layers[0].Description, StringComparison.OrdinalIgnoreCase);

        var buildups = items.Select(i => i.Buildup!).ToList();
        var dxf = Path.Combine(_dir, "batch.dxf");
        using (var fs = File.Create(dxf)) DxfExport.WriteMany(buildups, new DetailSettings(), CadStandard.CreateDefault(), fs, columns: 2);
        var doc = netDxf.DxfDocument.Load(dxf);
        var inserts = doc.Entities.Inserts.ToList();
        Assert.Equal(3, inserts.Count);
        Assert.Equal(3, inserts.Select(i => i.Block.Name).Distinct().Count());
        Assert.True(inserts[1].Position.X > inserts[0].Position.X);
        Assert.True(inserts[2].Position.Y < inserts[0].Position.Y);

        using var ms = new MemoryStream();
        PdfExport.WriteMany(buildups, new DetailSettings(), CadStandard.CreateDefault(), new PdfSheet(), ms);
        AssertPdf(ms.ToArray(), 3);
    }

    [Fact]
    public void A_batch_row_without_thicknesses_is_reported()
    {
        var path = Path.Combine(_dir, "bad.xlsx");
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.Worksheets.Add("S");
            ws.Cell(1, 1).Value = "Name";
            ws.Cell(1, 2).Value = "Build-up";
            ws.Cell(2, 1).Value = "Good";
            ws.Cell(2, 2).Value = "100mm concrete\nsubgrade";
            ws.Cell(3, 1).Value = "Bad";
            ws.Cell(3, 2).Value = "some asphalt";
            wb.SaveAs(path);
        }
        var items = BatchSheet.Read(path);
        Assert.NotNull(items[0].Buildup);
        Assert.Null(items[1].Buildup);
        Assert.NotEmpty(items[1].Problems);
    }

    [Theory]
    [InlineData("1.1.0", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.10", "1.0.9", true)]
    [InlineData("v2", "1.9.9", true)]
    [InlineData("1.0.0", "1.0.0+abc", false)]
    [InlineData("rubbish", "1.0.0", false)]
    public void Version_compare(string latest, string current, bool newer) => Assert.Equal(newer, VersionCheck.IsNewer(latest, current));

    [Fact]
    public async Task Version_check_reads_the_network_release_named_in_update_json()
    {
        var network = Path.Combine(_dir, "network", "TTW_Pave_Tool");
        Directory.CreateDirectory(Path.Combine(network, "_support"));
        File.WriteAllText(Path.Combine(network, "_support", VersionCheck.VersionFile), """{ "Version": "1.2.0", "Date": "2026-11-01", "Notes": "Rigid design" }""");
        var local = Path.Combine(_dir, "local", "TTW_Pave_Tool");
        Directory.CreateDirectory(Path.Combine(local, "_support"));
        Directory.CreateDirectory(Path.Combine(local, "Standalone Tool"));
        File.WriteAllText(Path.Combine(local, "_support", VersionCheck.UpdateFile),
            System.Text.Json.JsonSerializer.Serialize(new { ReleaseFolder = network }));

        var start = new[] { Path.Combine(local, "Standalone Tool") };
        Assert.Equal(network, VersionCheck.ReleaseFolder(start));
        var found = await VersionCheck.CheckAsync(start, "1.0.0", TimeSpan.FromSeconds(5));
        Assert.Equal("1.2.0", found!.Version);
        Assert.Equal("Rigid design", found.Notes);
        Assert.Null(await VersionCheck.CheckAsync(start, "1.2.0", TimeSpan.FromSeconds(5)));
        Assert.Null(await VersionCheck.CheckAsync(new[] { _dir }, "1.0.0", TimeSpan.FromSeconds(5)));
    }
}
