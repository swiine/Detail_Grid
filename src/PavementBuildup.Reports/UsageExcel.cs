using System.Globalization;
using ClosedXML.Excel;
using PavementBuildup.Core;

namespace PavementBuildup.Reports;

public static class UsageExcel
{
    private static readonly XLColor Band = XLColor.FromHtml("#024360");
    private static readonly XLColor Alabaster = XLColor.FromHtml("#F7F4E7");
    private static readonly XLColor Muted = XLColor.FromHtml("#6F6A5D");

    public static void Save(UsageSummary s, IReadOnlyList<UsageEvent> events, string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Summary");
        ws.Range(1, 1, 2, 5).Style.Fill.BackgroundColor = Band;
        ws.Cell(1, 1).Value = "TTW Pavement - time saved";
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        ws.Cell(1, 1).Style.Font.FontColor = Alabaster;
        ws.Cell(2, 1).Value = s.From is null && s.To is null ? "All time"
            : $"{s.From?.ToString("d MMM yyyy", CultureInfo.InvariantCulture) ?? "start"} to {s.To?.AddDays(-1).ToString("d MMM yyyy", CultureInfo.InvariantCulture) ?? "today"}";
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#8ECAEF");
        int r = 4;
        foreach (var (label, value) in new (string, double)[] { ("Hours saved", Math.Round(s.Hours, 1, MidpointRounding.AwayFromZero)), ("Working days saved (7.6 h)", Math.Round(s.Hours / 7.6, 1, MidpointRounding.AwayFromZero)), ("Tool uses", s.Uses), ("People", s.People) })
        {
            ws.Cell(r, 1).Value = label;
            ws.Cell(r, 2).Value = value;
            ws.Cell(r, 2).Style.Font.Bold = true;
            r++;
        }
        r++;
        r = Table(ws, r, "By tool", s.ByTool, s.MinutesPerUse);
        r = Table(ws, r + 1, "By month", s.ByMonth, null);
        r = Table(ws, r + 1, "By person", s.ByPerson, null);
        Table(ws, r + 1, "By program", s.ByApp, null);
        ws.Column(1).Width = 36;
        ws.Columns(2, 4).Width = 14;
        ws.Style.Font.FontName = "Arial";
        ws.ShowGridLines = false;

        var log = wb.Worksheets.Add("Log");
        var heads = new[] { "Time", "Tool", "Program", "Person", "Count", "Detail" };
        for (int c = 0; c < heads.Length; c++)
        {
            log.Cell(1, c + 1).Value = heads[c];
            log.Cell(1, c + 1).Style.Font.Bold = true;
        }
        int row = 2;
        foreach (var e in events)
        {
            log.Cell(row, 1).Value = e.Time;
            log.Cell(row, 1).Style.NumberFormat.Format = "d mmm yyyy h:mm";
            log.Cell(row, 2).Value = e.Tool;
            log.Cell(row, 3).Value = e.App;
            log.Cell(row, 4).Value = e.User;
            log.Cell(row, 5).Value = e.Count;
            log.Cell(row, 6).Value = e.Detail ?? "";
            row++;
        }
        log.Columns(1, 6).AdjustToContents();
        log.SheetView.FreezeRows(1);
        if (row > 2) log.Range(1, 1, row - 1, 6).SetAutoFilter();
        wb.Worksheet("Summary").SetTabActive();
        wb.SaveAs(path);
    }

    private static int Table(IXLWorksheet ws, int r, string title, IReadOnlyList<UsageRow> rows, IReadOnlyDictionary<string, double>? minutes)
    {
        ws.Cell(r, 1).Value = title;
        ws.Cell(r, 1).Style.Font.Bold = true;
        ws.Cell(r, 1).Style.Font.FontColor = Band;
        r++;
        var heads = minutes is null ? new[] { "", "Uses", "Hours saved" } : new[] { "", "Uses", "Min / use", "Hours saved" };
        for (int c = 0; c < heads.Length; c++)
        {
            ws.Cell(r, c + 1).Value = heads[c];
            ws.Cell(r, c + 1).Style.Font.FontColor = Muted;
        }
        r++;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.Name;
            ws.Cell(r, 2).Value = row.Uses;
            int hc = 3;
            if (minutes is not null)
            {
                if (minutes.TryGetValue(row.Name, out var m)) ws.Cell(r, 3).Value = m;
                hc = 4;
            }
            ws.Cell(r, hc).Value = Math.Round(row.Hours, 2);
            r++;
        }
        return r;
    }
}
