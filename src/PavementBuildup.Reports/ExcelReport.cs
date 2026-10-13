using System.Globalization;
using ClosedXML.Excel;
using PavementBuildup.Core.Design;

namespace PavementBuildup.Reports;

/// <summary>
/// The design calculation as an Excel workbook: a Summary sheet (job, result, inputs, results, a link to the
/// HTML report), a What-if sheet, one sheet per check, and Notes. Numbers are real numbers, so they can be
/// used in other sheets.
/// </summary>
public static class ExcelReport
{
    private static readonly XLColor Band = XLColor.FromHtml("#024360");
    private static readonly XLColor Alabaster = XLColor.FromHtml("#F7F4E7");
    private static readonly XLColor Line = XLColor.FromHtml("#DED7C4");
    private static readonly XLColor Ok = XLColor.FromHtml("#325442");
    private static readonly XLColor Bad = XLColor.FromHtml("#A84933");
    private static readonly XLColor Muted = XLColor.FromHtml("#6F6A5D");

    /// <param name="htmlFileName">The HTML report's file name next to the workbook (linked from the Summary), or null.</param>
    public static void Save(CalcResult design, string path, string? project, string? engineer, string? checker, string? htmlFileName)
    {
        using var wb = new XLWorkbook();
        wb.Properties.Title = design.Title;
        wb.Properties.Subject = project ?? "";
        wb.Properties.Author = engineer ?? "";

        var ws = wb.Worksheets.Add("Summary");
        int row = Header(ws, design.Title, design.Reference);
        row = Meta(ws, row, project, engineer, checker);

        var headline = ws.Cell(row, 1);
        headline.Value = (design.Pass == false ? "✖ " : "✔ ") + design.Headline;
        headline.Style.Font.Bold = true;
        headline.Style.Font.FontSize = 13;
        headline.Style.Font.FontColor = design.Pass == false ? Bad : Ok;
        ws.Range(row, 1, row, 4).Merge();
        row += 1;
        if (!string.IsNullOrWhiteSpace(htmlFileName))
        {
            var link = ws.Cell(row, 1);
            link.Value = "Open the HTML calculation report";
            link.SetHyperlink(new XLHyperlink(new Uri(Uri.EscapeDataString(htmlFileName).Replace("%2F", "/"), UriKind.Relative)));
            link.Style.Font.FontColor = Band;
            link.Style.Font.Underline = XLFontUnderlineValues.Single;
            row += 1;
        }
        row += 1;

        row = Lines(ws, row, "Inputs", design.Lines.Where(l => l.Kind == LineKind.Input).ToList(), withNote: false);
        row = Lines(ws, row + 1, "Results", design.Lines.Where(l => l.Kind != LineKind.Input).ToList(), withNote: true);
        if (design.Parts.Count > 0)
        {
            row++;
            ws.Cell(row, 1).Value = "Each check is on its own sheet:";
            ws.Cell(row, 1).Style.Font.FontColor = Muted;
            row++;
            for (int i = 0; i < design.Parts.Count; i++, row++)
            {
                var name = SheetName(i + 1, design.Parts[i].Title);
                var c = ws.Cell(row, 1);
                c.Value = $"{design.Parts[i].Title}  →  {design.Parts[i].Headline}";
                c.SetHyperlink(new XLHyperlink($"'{name}'!A1"));
                c.Style.Font.FontColor = design.Parts[i].Pass == false ? Bad : Band;
            }
        }
        Finish(ws);

        if (design.Tables.Count > 0)
        {
            var wi = wb.Worksheets.Add("What if");
            int r = Header(wi, "What if", design.Title);
            foreach (var (title, columns, rows) in design.Tables)
            {
                Section(wi, r++, title, columns.Length);
                for (int c = 0; c < columns.Length; c++) HeadCell(wi.Cell(r, c + 1), columns[c]);
                r++;
                foreach (var cells in rows)
                {
                    bool current = cells[0].StartsWith("►", StringComparison.Ordinal);
                    for (int c = 0; c < cells.Length; c++)
                    {
                        var cell = wi.Cell(r, c + 1);
                        SetValue(cell, cells[c].TrimStart('►', ' '));
                        if (current) { cell.Style.Font.Bold = true; cell.Style.Fill.BackgroundColor = Alabaster; }
                    }
                    r++;
                }
                r++;
            }
            Finish(wi);
        }

        for (int i = 0; i < design.Parts.Count; i++)
        {
            var part = design.Parts[i];
            var ps = wb.Worksheets.Add(SheetName(i + 1, part.Title));
            int r = Header(ps, part.Title, part.Reference);
            var h = ps.Cell(r, 1);
            h.Value = (part.Pass == false ? "✖ " : "") + part.Headline;
            h.Style.Font.Bold = true;
            h.Style.Font.FontColor = part.Pass == false ? Bad : Ok;
            ps.Range(r, 1, r, 4).Merge();
            r += 2;
            r = Lines(ps, r, "Inputs", part.Lines.Where(l => l.Kind == LineKind.Input).ToList(), withNote: false);
            r = Lines(ps, r + 1, "Calculation", part.Lines.Where(l => l.Kind != LineKind.Input).ToList(), withNote: true);
            if (part.Notes.Count > 0)
            {
                r++;
                foreach (var n in part.Notes.Distinct()) { ps.Cell(r, 1).Value = "• " + n; ps.Cell(r, 1).Style.Font.FontColor = Muted; r++; }
            }
            var back = ps.Cell(r + 1, 1);
            back.Value = "← Back to the summary";
            back.SetHyperlink(new XLHyperlink("'Summary'!A1"));
            back.Style.Font.FontColor = Band;
            Finish(ps);
        }

        var notes = design.Notes.Distinct().ToList();
        if (notes.Count > 0)
        {
            var ns = wb.Worksheets.Add("Notes");
            int r = Header(ns, "Notes", design.Title);
            foreach (var n in notes) { ns.Cell(r++, 1).Value = "• " + n; }
            ns.Cell(r + 1, 1).Value = "Calculated with TTW Pavement from the company design workbooks. Results are a design aid: check them before use.";
            ns.Cell(r + 1, 1).Style.Font.FontColor = Muted;
            Finish(ns);
        }

        wb.Worksheet("Summary").SetTabActive();
        wb.SaveAs(path);
    }

    // ------------------------------------------------------------------ layout helpers ----------

    private static int Header(IXLWorksheet ws, string title, string reference)
    {
        var range = ws.Range(1, 1, 2, 4);
        range.Style.Fill.BackgroundColor = Band;
        ws.Cell(1, 1).Value = title;
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        ws.Cell(1, 1).Style.Font.FontColor = Alabaster;
        ws.Cell(2, 1).Value = "TTW Pavement design calculation" + (reference.Length > 0 ? "   ·   " + reference : "");
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#8ECAEF");
        ws.Cell(2, 1).Style.Font.FontSize = 9;
        ws.Row(1).Height = 24;
        return 4;
    }

    private static int Meta(IXLWorksheet ws, int row, string? project, string? engineer, string? checker)
    {
        foreach (var (label, value) in new[]
        {
            ("Project", project), ("Designed", engineer), ("Checked", checker),
            ("Date", DateTime.Now.ToString("d MMM yyyy", CultureInfo.InvariantCulture)),
        })
        {
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 1).Style.Font.FontColor = Muted;
            ws.Cell(row, 2).Value = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
            row++;
        }
        return row + 1;
    }

    private static void Section(IXLWorksheet ws, int row, string title, int width)
    {
        var c = ws.Cell(row, 1);
        c.Value = title;
        c.Style.Font.Bold = true;
        c.Style.Font.FontColor = Band;
        ws.Range(row, 1, row, Math.Max(1, width)).Style.Border.BottomBorder = XLBorderStyleValues.Medium;
        ws.Range(row, 1, row, Math.Max(1, width)).Style.Border.BottomBorderColor = Band;
    }

    private static void HeadCell(IXLCell c, string text)
    {
        c.Value = text;
        c.Style.Font.Bold = true;
        c.Style.Font.FontSize = 9;
        c.Style.Font.FontColor = Muted;
        c.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        c.Style.Border.BottomBorderColor = Line;
    }

    private static int Lines(IXLWorksheet ws, int row, string title, List<CalcLine> lines, bool withNote)
    {
        if (lines.Count == 0) return row;
        Section(ws, row++, title, withNote ? 4 : 3);
        HeadCell(ws.Cell(row, 1), withNote ? "Step" : "Input");
        HeadCell(ws.Cell(row, 2), "Value");
        HeadCell(ws.Cell(row, 3), "Unit");
        if (withNote) HeadCell(ws.Cell(row, 4), "Note");
        row++;
        foreach (var l in lines)
        {
            ws.Cell(row, 1).Value = l.Label;
            SetValue(ws.Cell(row, 2), l.Value);
            ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(row, 3).Value = l.Unit;
            if (withNote) ws.Cell(row, 4).Value = l.Reference;
            var style = ws.Range(row, 1, row, withNote ? 4 : 3).Style;
            style.Border.BottomBorder = XLBorderStyleValues.Hair;
            style.Border.BottomBorderColor = Line;
            switch (l.Kind)
            {
                case LineKind.Result: style.Font.Bold = true; break;
                case LineKind.Ok: style.Font.Bold = true; style.Font.FontColor = Ok; break;
                case LineKind.Fail or LineKind.Warning: style.Font.Bold = true; style.Font.FontColor = Bad; break;
            }
            ws.Cell(row, 4).Style.Font.FontColor = l.Kind is LineKind.Fail or LineKind.Warning ? Bad : Muted;
            row++;
        }
        return row;
    }

    /// <summary>Numbers as numbers (so they can be used in formulas), everything else as text.</summary>
    private static void SetValue(IXLCell cell, string text)
    {
        var t = text.Trim();
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !t.StartsWith('+'))
        {
            cell.Value = v;
            cell.Style.NumberFormat.Format = t.Contains('E', StringComparison.OrdinalIgnoreCase) ? "0.00E+00" : "General";
        }
        else
            cell.Value = t;
    }

    private static void Finish(IXLWorksheet ws)
    {
        ws.Style.Font.FontName = "Arial";
        ws.Column(1).Width = 58;
        ws.Column(2).Width = 14;
        ws.Column(3).Width = 12;
        ws.Column(4).Width = 34;
        for (int c = 5; c <= 8; c++) ws.Column(c).Width = 12;
        ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        ws.PageSetup.FitToPages(1, 0);
        ws.SheetView.FreezeRows(2);
        ws.ShowGridLines = false;
    }

    /// <summary>"3 Racks - interior post" (≤ 31 characters, no characters Excel refuses in sheet names).</summary>
    public static string SheetName(int index, string title)
    {
        var shortTitle = title
            .Replace("Uniformly distributed load (UDL)", "UDL")
            .Replace("Wheel stress - ", "T34 ")
            .Replace("Wheel loading - ", "Wheels ")
            .Replace("Post loading - ", "Posts ")
            .Replace("Punching shear - interior post", "Punching shear");
        shortTitle = System.Text.RegularExpressions.Regex.Replace(shortTitle, @"\s*\([^)]*\)", ""); // drop "(Westergaard)" etc.
        var clean = new string(shortTitle.Where(ch => "[]:*?/\\".IndexOf(ch) < 0).ToArray()).Trim();
        var name = $"{index} {clean}";
        return name.Length <= 31 ? name : name[..31].TrimEnd();
    }
}
