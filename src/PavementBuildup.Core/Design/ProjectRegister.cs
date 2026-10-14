namespace PavementBuildup.Core.Design;

public sealed record RegisterEntry(string Path, DesignFile File, CalcResult? Result, string? Error, DateTime Saved, string? ReportHtml, string? ReportXlsx)
{
    public string Name => File.Title.Length > 0 ? File.Title : System.IO.Path.GetFileNameWithoutExtension(Path);
    public string Type => File.IsSlab ? "Concrete slab" : "Road / car park";
    public string Cbr => File.Text("cbr") + "%";
    public string Loads => File.LoadSummary();
    public string Outcome => Result?.Headline ?? Error ?? "-";
    public string Thickness => Result?.ThicknessMm is { } t ? $"{t:0} mm" : "-";
    public double? CostPerM2 => Result is not null && Result.Values.TryGetValue("cost_m2", out var c) ? c : null;
}

public static class ProjectRegister
{
    public static List<RegisterEntry> Scan(string root, string? jobNumber, RateTable? rates = null, bool allVersions = false, JointRulesFile? jointRules = null)
    {
        var entries = new List<RegisterEntry>();
        if (!Directory.Exists(root)) return entries;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(root, "*" + DesignFile.Extension, SearchOption.AllDirectories).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return entries; }

        string job = (jobNumber ?? "").Trim();
        foreach (var path in files)
        {
            DesignFile file;
            try { file = DesignFile.Load(path); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { continue; }
            if (job.Length > 0 && !string.Equals(file.JobNo.Trim(), job, StringComparison.OrdinalIgnoreCase)) continue;

            CalcResult? result = null;
            string? error = null;
            try { result = file.Run(rates, jointRules); }
            catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) { error = ex.Message; }

            var (html, xlsx) = ReportsFor(path);
            entries.Add(new RegisterEntry(path, file, result, error, System.IO.File.GetLastWriteTime(path), html, xlsx));
        }

        if (!allVersions)
            entries = entries.GroupBy(e => e.Name.Trim().ToUpperInvariant()).Select(g => g.OrderByDescending(e => e.Saved).First()).ToList();
        return entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenByDescending(e => e.Saved).ToList();
    }

    public static (string? Html, string? Xlsx) ReportsFor(string designPath)
    {
        var html = Path.ChangeExtension(designPath, ".html");
        var xlsx = Path.ChangeExtension(designPath, ".xlsx");
        if (!File.Exists(html))
        {
            try
            {
                html = Directory.EnumerateFiles(Path.GetDirectoryName(designPath)!, "*.html")
                    .OrderByDescending(File.GetLastWriteTime).FirstOrDefault() ?? "";
                xlsx = html.Length > 0 ? Path.ChangeExtension(html, ".xlsx") : "";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { html = xlsx = ""; }
        }
        return (File.Exists(html) ? html : null, File.Exists(xlsx) ? xlsx : null);
    }
}
