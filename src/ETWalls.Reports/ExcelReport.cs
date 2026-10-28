using ClosedXML.Excel;
using ETWalls.Core.Design;

namespace ETWalls.Reports;

/// <summary>
/// The calculation as an Excel workbook: Summary, Inputs, Checks, Reinforcement, Cost (live formulas, so the rates
/// can be changed in Excel) and What if. Links back to the HTML report saved next to it.
/// </summary>
public static class ExcelReport
{
    static readonly XLColor Charcoal = XLColor.FromHtml("#1F252B"), Orange = XLColor.FromHtml("#E2621B"), Light = XLColor.FromHtml("#F5F4F1");

    public static void Save(DesignRun run, string path, string htmlFileName, DateTime? date = null)
    {
        using var wb = Build(run, htmlFileName, date);
        wb.SaveAs(path);
    }

    public static XLWorkbook Build(DesignRun run, string htmlFileName, DateTime? date = null)
    {
        var inp = run.Input; var r = run.Result; var L = inp.Length;
        var wb = new XLWorkbook();

        // Summary
        var s = wb.AddWorksheet("Summary");
        s.Cell(1, 1).Value = "ET Walls (Engineering Technologies) - retaining wall calculation";
        s.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(Charcoal);
        s.Range(1, 1, 1, 2).Style.Border.SetBottomBorder(XLBorderStyleValues.Medium).Border.SetBottomBorderColor(Orange);
        int row = 3;
        void Kv(string k, XLCellValue v) { s.Cell(row, 1).Value = k; s.Cell(row, 1).Style.Font.SetFontColor(XLColor.FromHtml("#64676C")); s.Cell(row, 2).Value = v; row++; }
        Kv("Project", inp.Project); Kv("Job no.", inp.JobNumber); Kv("Wall", inp.WallName); Kv("Designed by", inp.DesignedBy); Kv("Checked by", inp.CheckedBy);
        Kv("Date", (date ?? DateTime.Today).ToString("yyyy-MM-dd")); row++;
        Kv("Wall type", DesignRun.TypeName(inp.Type)); Kv("Retained height (m)", inp.H); Kv("Wall length (m)", L);
        Kv("Sizing", inp.Custom ? "Custom" : "Automatic, lowest cost"); Kv("Size (mm)", DesignRun.SizeText(inp));
        Kv("Result", r.Pass ? "All checks pass" : "Fails: " + string.Join(", ", r.Checks.Where(c => !c.Pass).Select(c => c.Name)));
        Kv("Governing check", r.Worst.Name); Kv("Governing use (%)", Math.Round(r.Worst.Util * 1000) / 10);
        Kv("Cost per metre ($)", Math.Round(run.Cost.PerMetre)); Kv("Cost for the wall ($)", Math.Round(run.TotalCost)); row++;
        s.Cell(row, 1).Value = "HTML report";
        s.Cell(row, 2).Value = htmlFileName + " (same folder)";
        s.Cell(row, 2).SetHyperlink(new XLHyperlink(htmlFileName));
        row += 2;
        s.Cell(row, 1).Value = "Check every result. Method: see the HTML report, section 8.";
        s.Column(1).Width = 26; s.Column(2).Width = 70;

        // Inputs
        Table(wb.AddWorksheet("Inputs"), new[] { "Group", "Input", "Value", "Unit", "Source" }, new double[] { 12, 32, 34, 8, 14 },
            ReportRows.Inputs(run).Select(x => new XLCellValue[] { x.Group, x.Name, Num(x.Value), x.Unit, x.Source }));

        // Checks
        Table(wb.AddWorksheet("Checks"), new[] { "Check", "Value", "Unit", "Needs", "Used %", "Result", "Working" }, new double[] { 30, 11, 8, 12, 9, 8, 100 },
            r.Checks.Select(c => new XLCellValue[] { c.Name, double.IsFinite(c.Value) ? Math.Round(c.Value, 3) : "∞", c.Unit, (c.Unit == "FoS" ? ">= " : "<= ") + Math.Round(c.Limit, 3), Math.Round(c.Util * 1000) / 10, c.Pass ? "PASS" : "FAIL", c.Note }));

        // Reinforcement
        if (r.R.Reo is { } reo)
        {
            var ws = wb.AddWorksheet("Reinforcement");
            int last = Table(ws, new[] { "Mark", "Where", "Bars", "Needs (mm²)", "Gives (mm²)", "kg", "Note" }, new double[] { 6, 38, 16, 12, 12, 8, 64 },
                reo.Schedule.Select(x => new XLCellValue[] { x.Mark, x.Where, x.Bars, Math.Round(x.Req), Math.Round(x.Prov), Math.Round(x.Kg, 1), x.Note }));
            ws.Cell(last + 1, 2).Value = "Total steel";
            ws.Cell(last + 1, 6).FormulaA1 = $"SUM(F2:F{last})";
            ws.Cell(last + 1, 7).Value = inp.Type == WallType.Post ? "per pier" : "per metre of wall";
            ws.Row(last + 1).Style.Font.SetBold();
        }

        // Cost, with formulas
        {
            var ws = wb.AddWorksheet("Cost");
            var items = run.Cost.Items;
            int n = items.Count, lengthRow = n + 3;
            Header(ws, new[] { "Item", "Qty per m", "Unit", "Rate ($)", "$ per m", "$ for wall" });
            for (int i = 0; i < n; i++)
            {
                int rr = i + 2; var it = items[i];
                ws.Cell(rr, 1).Value = it.Name; ws.Cell(rr, 2).Value = Math.Round(it.Qty, 3); ws.Cell(rr, 3).Value = it.Unit; ws.Cell(rr, 4).Value = it.Rate;
                ws.Cell(rr, 5).FormulaA1 = $"B{rr}*D{rr}";
                ws.Cell(rr, 6).FormulaA1 = $"E{rr}*$B${lengthRow}";
            }
            ws.Cell(n + 2, 1).Value = "Total"; ws.Cell(n + 2, 5).FormulaA1 = $"SUM(E2:E{n + 1})"; ws.Cell(n + 2, 6).FormulaA1 = $"SUM(F2:F{n + 1})";
            ws.Row(n + 2).Style.Font.SetBold();
            ws.Range(n + 2, 1, n + 2, 6).Style.Border.SetTopBorder(XLBorderStyleValues.Medium).Border.SetTopBorderColor(Orange);
            ws.Cell(lengthRow, 1).Value = "Wall length (m)"; ws.Cell(lengthRow, 2).Value = L;
            ws.Range(2, 4, n + 2, 6).Style.NumberFormat.Format = "$#,##0.00";
            ws.Cell(lengthRow, 4).Value = "Change a rate or the length and the totals update.";
            ws.Cell(lengthRow, 4).Style.Font.SetItalic().Font.SetFontColor(XLColor.FromHtml("#64676C"));
            if (run.Sizing.Options.Count > 0)
            {
                int o = lengthRow + 2;
                foreach (var (h, c) in new[] { "Options that pass every check", "Size (mm)", "", "", "$ per m", "Highest check %" }.Select((h, c) => (h, c + 1)))
                    ws.Cell(o, c).Value = h;
                StyleHeader(ws.Range(o, 1, o, 6));
                foreach (var opt in run.Sizing.Options)
                {
                    o++;
                    ws.Cell(o, 1).Value = opt.Label; ws.Cell(o, 2).Value = DesignRun.SizeText(opt.Sizes.Apply(inp));
                    ws.Cell(o, 5).Value = Math.Round(opt.CostPerMetre); ws.Cell(o, 6).Value = Math.Round(opt.Util * 1000) / 10;
                }
            }
            foreach (var (c, w) in new[] { (1, 42.0), (2, 56.0), (3, 8.0), (4, 12.0), (5, 12.0), (6, 16.0) }) ws.Column(c).Width = w;
        }

        // What if
        {
            var ws = wb.AddWorksheet("What if");
            var wt = WhatIf.Size(inp);
            var head = new List<string> { wt.Label }; head.AddRange(wt.Columns.Select(c => c + " %")); head.Add("$ per m"); head.Add("Result");
            int last = Table(ws, head.ToArray(), head.Select((_, i) => i == 0 ? 20.0 : 14.0).ToArray(),
                wt.Rows.Select(x => new XLCellValue[] { x.Value }.Concat(x.Checks.Select(c => (XLCellValue)(Math.Round(c.Util * 1000) / 10))).Append(Math.Round(x.CostPerMetre)).Append(x.Pass ? "PASS" : "FAIL").ToArray()));
            int p = last + 2;
            ws.Cell(p, 1).Value = "Backfill φ (°)"; ws.Cell(p, 2).Value = "Worst check %"; ws.Cell(p, 3).Value = "Governs";
            StyleHeader(ws.Range(p, 1, p, 3));
            foreach (var x in WhatIf.BackfillPhi(inp)) { p++; ws.Cell(p, 1).Value = x.Phi; ws.Cell(p, 2).Value = Math.Round(x.Worst.Util * 1000) / 10; ws.Cell(p, 3).Value = x.Worst.Name; }
        }
        return wb;
    }

    static XLCellValue Num(string s) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : s;

    static void Header(IXLWorksheet ws, string[] heads)
    {
        for (int c = 0; c < heads.Length; c++) ws.Cell(1, c + 1).Value = heads[c];
        StyleHeader(ws.Range(1, 1, 1, heads.Length));
        ws.SheetView.FreezeRows(1);
    }

    static void StyleHeader(IXLRange r) => r.Style.Fill.SetBackgroundColor(Charcoal).Font.SetFontColor(Light).Font.SetBold();

    /// <summary>Header row plus rows; returns the last row used.</summary>
    static int Table(IXLWorksheet ws, string[] heads, double[] widths, IEnumerable<XLCellValue[]> rows)
    {
        Header(ws, heads);
        int r = 1;
        foreach (var vals in rows) { r++; for (int c = 0; c < vals.Length; c++) ws.Cell(r, c + 1).Value = vals[c]; }
        for (int c = 0; c < widths.Length; c++) ws.Column(c + 1).Width = widths[c];
        ws.Columns().Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top);
        return r;
    }
}
