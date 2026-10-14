namespace PavementBuildup.Core.Design;

public sealed record DesignOption(string Name, DesignFile File, CalcResult Result);

public static class DesignCompare
{
    public static CalcResult Compare(IReadOnlyList<DesignOption> options, string? currency = "$")
    {
        if (options.Count < 2) throw new ArgumentException("Pick at least two designs to compare.");
        currency ??= "$";
        var r = new CalcResult { Title = "Pavement options comparison", Reference = "TTW Pavement design calculations", TablesHeading = "Comparison" };
        r.In("Options compared", options.Count, "", "0");
        foreach (var o in options) r.InText(o.Name, o.Result.Headline);

        var columns = new[] { "" }.Concat(options.Select(o => o.Name)).ToArray();
        var rows = new List<string[]>
        {
            Row("Type", o => o.File.IsSlab ? "Concrete slab" : "Road / car park (flexible)"),
            Row("Subgrade CBR", o => o.File.Text("cbr") + "%"),
            Row("Loads / traffic", o => o.File.LoadSummary()),
            Row("Build-up", o => BuildupSummary(o.Result)),
            Row("Total thickness", o => o.Result.ThicknessMm is { } t ? $"{t:0} mm" : "-"),
            Row("Governed by", Governs),
            Row("Result", o => o.Result.Pass == false ? "NO GOOD" : "OK"),
            Row("Cost per m²", o => o.Result.Values.TryGetValue("cost_m2", out var c) ? $"{currency}{c:0}" : "-"),
            Row("Cost for the area", o => o.Result.Values.TryGetValue("cost_total", out var c) ? $"{currency}{c:#,##0}" : "-"),
        };
        r.Tables.Add(("Options side by side", columns, rows));

        var passing = options.Where(o => o.Result.Pass != false).ToList();
        var parts = new List<string>();
        var thinnest = passing.Where(o => o.Result.ThicknessMm is not null).OrderBy(o => o.Result.ThicknessMm).FirstOrDefault();
        if (thinnest is not null) parts.Add($"thinnest: {thinnest.Name} ({thinnest.Result.ThicknessMm:0} mm)");
        var cheapest = passing.Where(o => o.Result.Values.ContainsKey("cost_m2")).OrderBy(o => o.Result.Values["cost_m2"]).FirstOrDefault();
        if (cheapest is not null) parts.Add($"cheapest: {cheapest.Name} ({currency}{cheapest.Result.Values["cost_m2"]:0}/m²)");
        r.Headline = parts.Count > 0 ? char.ToUpperInvariant(parts[0][0]) + string.Join("  ·  ", parts)[1..] : "No option passes";
        r.Pass = passing.Count > 0;
        if (passing.Count < options.Count) r.Warn($"{options.Count - passing.Count} option(s) do not pass: see their calculations.");
        r.Notes.Add("Costs are rough estimates from the rates file, for comparing options only.");
        foreach (var o in options) r.Parts.Add(o.Result.WithTitle($"{o.Name}: {o.Result.Title}"));
        return r;

        string[] Row(string label, Func<DesignOption, string> cell) => new[] { label }.Concat(options.Select(cell)).ToArray();
    }

    public static string BuildupSummary(CalcResult result)
    {
        if (result.BuildupText is not { } text) return "-";
        var lines = text.Split('\n').Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('[') && !l.StartsWith("SUBGRADE", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Replace("COMPACTED THICKNESS ", "", StringComparison.OrdinalIgnoreCase));
        return string.Join(" / ", lines);
    }

    public static string Governs(DesignOption o)
    {
        var line = o.Result.Lines.FirstOrDefault(l => l.Label == "Governed by");
        if (line is not null) return line.Value;
        if (o.Result.Values.TryGetValue("required", out var req) && o.Result.Values.TryGetValue("granular", out var gran))
            return gran - req < 5 ? "Austroads thickness" : "Company design table";
        return "-";
    }
}
