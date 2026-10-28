using ClosedXML.Excel;
using ETWalls.Core.Design;
using ETWalls.Reports;

namespace ETWalls.Core.Tests;

public class ReportTests
{
    static DesignRun Run(WallType type = WallType.Cantilever, bool custom = false) => DesignRun.For(new WallInput
    {
        Type = type, Project = "Carpark retaining walls", JobNumber = "241001", WallName = "W1 - Carpark east", DesignedBy = "A. Engineer", CheckedBy = "C. Checker", Custom = custom,
    });

    [Theory]
    [InlineData(WallType.Cantilever)]
    [InlineData(WallType.Block)]
    [InlineData(WallType.Gravity)]
    [InlineData(WallType.Post)]
    public void HtmlReportHasEverySection(WallType type)
    {
        var html = HtmlReport.Build(Run(type), "W1 - Carpark east.xlsx");
        foreach (var h in new[] { "1: Summary", "2: Typical wall section", "3: Inputs", "4: Checks", "5: Reinforcement", "6: Cost estimate", "7: What if", "8: Method and notes" })
            Assert.Contains($"<h2>{h}</h2>", html);
        Assert.Contains("<svg", html);
        Assert.Contains("W1%20-%20Carpark%20east.xlsx", html);
        var dir = Environment.GetEnvironmentVariable("ETWALLS_DRAWINGS");
        if (dir is not null) File.WriteAllText(Path.Combine(dir, $"report-{type}.html"), html);
    }

    [Fact]
    public void AutomaticDesignIsTheCheapestOption()
    {
        var run = Run();
        Assert.False(run.Input.Custom);
        Assert.Equal(run.Sizing.Options[0].CostPerMetre, run.Cost.PerMetre, 6);
        Assert.True(run.Result.Pass);
    }

    [Fact]
    public void ExcelCostSheetUsesFormulas()
    {
        var run = Run();
        using var wb = ExcelReport.Build(run, "W1 - Carpark east.html");
        Assert.Equal(new[] { "Summary", "Inputs", "Checks", "Reinforcement", "Cost", "What if" }, wb.Worksheets.Select(w => w.Name));
        var cost = wb.Worksheet("Cost");
        Assert.Equal("B2*D2", cost.Cell(2, 5).FormulaA1);
        int n = run.Cost.Items.Count;
        Assert.Equal($"E2*$B${n + 3}", cost.Cell(2, 6).FormulaA1);
        Assert.Equal(run.Input.Length, cost.Cell(n + 3, 2).GetDouble());
        Assert.Equal(Math.Round(run.Cost.PerMetre, 2), Math.Round(cost.Cell(n + 2, 5).Value.GetNumber(), 2), 2);
        var dir = Environment.GetEnvironmentVariable("ETWALLS_DRAWINGS");
        if (dir is not null) wb.SaveAs(Path.Combine(dir, "report.xlsx"));
    }
}
