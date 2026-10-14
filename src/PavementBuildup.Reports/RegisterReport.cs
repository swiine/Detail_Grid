using System.Globalization;
using ClosedXML.Excel;
using PavementBuildup.Core.Design;

namespace PavementBuildup.Reports;

/// <summary>The project pavement register as an Excel sheet: one row per pavement, with links to its report.</summary>
public static class RegisterReport
{
    private static readonly XLColor Band = XLColor.FromHtml("#024360");
    private static readonly XLColor Alabaster = XLColor.FromHtml("#F7F4E7");
    private static readonly XLColor Line = XLColor.FromHtml("#DED7C4");
    private static readonly XLColor Bad = XLColor.FromHtml("#A84933");
    private static readonly XLColor Muted = XLColor.FromHtml("#6F6A5D");

    public static void Save(IReadOnlyList<RegisterEntry> entries, string path, string jobNumber, string? project, string currency = "$")
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Register");
        wb.Properties.Title = $"Pavement register {jobNumber}".Trim();

        var columns = new[] { "Pavement", "Type", "Subgrade CBR", "Traffic / loads", "Result", "Thickness (mm)", $"Cost ({currency}/m²)", "Build-up", "Saved", "Report", "Excel", "Design file" };
        ws.Range(1, 1, 2, columns.Length).Style.Fill.BackgroundColor = Band;
        ws.Cell(1, 1).Value = "Pavement register" + (jobNumber.Trim().Length > 0 ? "  ·  Job " + jobNumber.Trim() : "");
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        ws.Cell(1, 1).Style.Font.FontColor = Alabaster;
        ws.Cell(2, 1).Value = (string.IsNullOrWhiteSpace(project) ? "" : project.Trim() + "   ·   ") + "TTW Pavement   ·   " + DateTime.Now.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#8ECAEF");
        ws.Row(1).Height = 24;

        int row = 4;
        for (int c = 0; c < columns.Length; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = columns[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = Muted;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
            cell.Style.Border.BottomBorderColor = Band;
        }
        row++;
        foreach (var e in entries)
        {
            ws.Cell(row, 1).Value = e.Name;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = e.Type;
            if (double.TryParse(e.File.Text("cbr"), NumberStyles.Float, CultureInfo.InvariantCulture, out var cbr)) ws.Cell(row, 3).Value = cbr;
            else ws.Cell(row, 3).Value = e.Cbr;
            ws.Cell(row, 4).Value = e.Loads;
            ws.Cell(row, 5).Value = e.Outcome;
            if (e.Result?.Pass == false || e.Error is not null) ws.Cell(row, 5).Style.Font.FontColor = Bad;
            if (e.Result?.ThicknessMm is { } t) ws.Cell(row, 6).Value = t;
            if (e.CostPerM2 is { } cost) { ws.Cell(row, 7).Value = Math.Round(cost, 2); ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00"; }
            ws.Cell(row, 8).Value = e.Result is null ? "" : DesignCompare.BuildupSummary(e.Result);
            ws.Cell(row, 9).Value = e.Saved;
            ws.Cell(row, 9).Style.NumberFormat.Format = "d mmm yyyy h:mm";
            Link(ws.Cell(row, 10), e.ReportHtml, "Open report");
            Link(ws.Cell(row, 11), e.ReportXlsx, "Open workbook");
            Link(ws.Cell(row, 12), e.Path, Path.GetFileName(e.Path));
            var style = ws.Range(row, 1, row, columns.Length).Style;
            style.Border.BottomBorder = XLBorderStyleValues.Hair;
            style.Border.BottomBorderColor = Line;
            style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            row++;
        }
        if (entries.Count == 0) ws.Cell(row++, 1).Value = "No saved designs for this job yet: save a design (or make its report) in the Design tab.";
        ws.Cell(row + 1, 1).Value = "Costs are rough estimates from the rates file, for comparing options only.";
        ws.Cell(row + 1, 1).Style.Font.FontColor = Muted;

        ws.Style.Font.FontName = "Arial";
        ws.Column(1).Width = 32;
        ws.Column(2).Width = 16;
        ws.Column(3).Width = 13;
        ws.Column(4).Width = 30;
        ws.Column(5).Width = 60;
        ws.Column(6).Width = 14;
        ws.Column(7).Width = 13;
        ws.Column(8).Width = 60;
        ws.Column(9).Width = 18;
        ws.Columns(10, 12).Width = 18;
        ws.Column(5).Style.Alignment.WrapText = true;
        ws.Column(8).Style.Alignment.WrapText = true;
        ws.SheetView.FreezeRows(4);
        ws.ShowGridLines = false;
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);
        wb.SaveAs(path);
    }

    private static void Link(IXLCell cell, string? file, string text)
    {
        if (string.IsNullOrEmpty(file)) return;
        cell.Value = text;
        cell.SetHyperlink(new XLHyperlink(new Uri(Path.GetFullPath(file))));
        cell.Style.Font.FontColor = Band;
        cell.Style.Font.Underline = XLFontUnderlineValues.Single;
    }
}
