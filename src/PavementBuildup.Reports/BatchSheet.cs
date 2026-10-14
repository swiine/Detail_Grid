using ClosedXML.Excel;
using PavementBuildup.Core;

namespace PavementBuildup.Reports;

public sealed record BatchItem(int Row, string Name, string Text, Buildup? Buildup, IReadOnlyList<string> Problems);

public static class BatchSheet
{
    public static void WriteTemplate(string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Pavements");
        var heads = new[] { "Name", "Layer 1", "Layer 2", "Layer 3", "Layer 4", "Layer 5", "Layer 6", "Subgrade", "Build-up (instead of layers)" };
        for (int c = 0; c < heads.Length; c++)
        {
            ws.Cell(1, c + 1).Value = heads[c];
            ws.Cell(1, c + 1).Style.Font.Bold = true;
            ws.Cell(1, c + 1).Style.Font.FontColor = XLColor.FromHtml("#F7F4E7");
            ws.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#024360");
        }
        string[][] examples =
        {
            new[] { "TYPE A - CARPARK", "40mm AC10 wearing course", "prime", "150mm DGB20 base", "180mm DGS20 subbase", "", "", "Subgrade CBR 5% compacted to 98% SMDD", "" },
            new[] { "TYPE B - FOOTPATH", "", "", "", "", "", "", "", "100mm 32MPa concrete with SL72 mesh\n75mm DGB20 base\nsubgrade" },
            new[] { "TYPE C - PAVERS", "80mm concrete pavers", "30mm bedding sand", "200mm DGB20 base", "", "", "", "subgrade", "" },
        };
        for (int r = 0; r < examples.Length; r++)
            for (int c = 0; c < examples[r].Length; c++)
                ws.Cell(r + 2, c + 1).Value = examples[r][c];
        ws.Columns(1, 8).Width = 28;
        ws.Column(9).Width = 50;
        ws.Column(9).Style.Alignment.WrapText = true;
        ws.Cell(7, 1).Value = "One pavement per row, layers top to bottom with thicknesses. Delete the examples. Use either the layer columns or the build-up column.";
        ws.Cell(7, 1).Style.Font.FontColor = XLColor.FromHtml("#6F6A5D");
        ws.SheetView.FreezeRows(1);
        wb.SaveAs(path);
    }

    public static List<BatchItem> Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var used = ws.RangeUsed();
        var items = new List<BatchItem>();
        if (used is null) return items;

        int headRow = 0, nameCol = 0;
        for (int r = 1; r <= Math.Min(10, used.LastRow().RowNumber()) && headRow == 0; r++)
            for (int c = 1; c <= used.LastColumn().ColumnNumber(); c++)
                if (Head(ws.Cell(r, c)) is "name" or "title" or "pavement" or "pavement type") { headRow = r; nameCol = c; break; }
        if (headRow == 0) throw new InvalidDataException("The first sheet needs a heading row with a \"Name\" column (Make template shows the layout).");

        int lastCol = used.LastColumn().ColumnNumber();
        var layerCols = new List<int>();
        int buildupCol = 0, subgradeCol = 0;
        for (int c = 1; c <= lastCol; c++)
        {
            var h = Head(ws.Cell(headRow, c));
            if (c == nameCol || h.Length == 0) continue;
            if (h.StartsWith("build", StringComparison.Ordinal) || h is "pavement build-up" or "spec" or "specification") buildupCol = c;
            else if (h.StartsWith("subgrade", StringComparison.Ordinal)) subgradeCol = c;
            else if (h.StartsWith("layer", StringComparison.Ordinal) || h.StartsWith("course", StringComparison.Ordinal)) layerCols.Add(c);
        }
        if (buildupCol == 0 && layerCols.Count == 0)
            throw new InvalidDataException("Add a \"Build-up\" column or \"Layer 1\", \"Layer 2\"... columns next to \"Name\".");

        for (int r = headRow + 1; r <= used.LastRow().RowNumber(); r++)
        {
            var name = ws.Cell(r, nameCol).GetFormattedString().Trim();
            var lines = new List<string>();
            var whole = buildupCol > 0 ? ws.Cell(r, buildupCol).GetFormattedString().Trim() : "";
            if (whole.Length > 0) lines.Add(whole);
            else lines.AddRange(layerCols.Select(c => ws.Cell(r, c).GetFormattedString().Trim()).Where(s => s.Length > 0));
            if (subgradeCol > 0 && ws.Cell(r, subgradeCol).GetFormattedString().Trim() is { Length: > 0 } sg) lines.Add(sg);
            if (name.Length == 0 && lines.Count == 0) continue;
            if (lines.Count == 0 && name.Length > 40) continue;

            var text = string.Join("\n", lines);
            var problems = new List<string>();
            Buildup? buildup = null;
            if (lines.Count == 0) problems.Add("No layers.");
            else
            {
                var read = SmartReader.Read(text, problems);
                if (read.Parsed.Layers.Count == 0) problems.Add("No layers with a thickness found.");
                else buildup = read.Parsed.ToBuildup(name.Length > 0 ? name.ToUpperInvariant() : read.Parsed.Name ?? $"PAVEMENT {r - headRow}");
            }
            items.Add(new BatchItem(r, name.Length > 0 ? name : $"Row {r}", text, buildup, problems));
        }
        return items;
    }

    private static string Head(IXLCell cell) => cell.GetFormattedString().Trim().ToLowerInvariant();
}
