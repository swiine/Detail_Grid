using System.Text.Json;
using System.Text.Json.Serialization;

namespace PavementBuildup.Core.Design;

public sealed class Rate
{
    public string Name { get; set; } = "";
    public List<string> Match { get; set; } = new();
    public string Unit { get; set; } = "m3";
    public double Price { get; set; }
    public double Density { get; set; } = 2.3;
}

public sealed class RateTable
{
    public const string FileName = "TTW_Rates.json";

    public string Currency { get; set; } = "$";
    public string Note { get; set; } = "PLACEHOLDER RATES: replace with current TTW / supplier rates before using the costs.";
    public double SubgradePrepPerM2 { get; set; } = 4;
    public List<Rate> Rates { get; set; } = new();

    [JsonIgnore] public string? SourcePath { get; private set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, PropertyNameCaseInsensitive = true,
    };

    public static RateTable CreateDefault() => new()
    {
        Rates =
        {
            new() { Name = "Asphalt", Match = { "AC10", "AC14", "AC20", "SMA", "ASPHALT", "WEARING COURSE", "OGA" }, Unit = "t", Price = 210, Density = 2.45 },
            new() { Name = "Prime / primerseal", Match = { "PRIME", "TACK" }, Unit = "m2", Price = 3.5 },
            new() { Name = "Spray seal", Match = { "SEAL" }, Unit = "m2", Price = 8 },
            new() { Name = "Concrete pavers", Match = { "PAVER", "PAVERS" }, Unit = "m2", Price = 85 },
            new() { Name = "Concrete", Match = { "CONCRETE", "F'C", "MPA" }, Unit = "m3", Price = 360 },
            new() { Name = "Bedding sand", Match = { "SAND" }, Unit = "m3", Price = 90 },
            new() { Name = "Stabilised / bound material", Match = { "STABILISED", "CEMENT", "LIME", "BOUND", "CTB" }, Unit = "m3", Price = 130 },
            new() { Name = "DGS20/40 subbase", Match = { "DGS", "SUBBASE", "SUB-BASE" }, Unit = "m3", Price = 95 },
            new() { Name = "DGB20 base", Match = { "DGB", "BASE" }, Unit = "m3", Price = 110 },
            new() { Name = "Select fill", Match = { "SELECT", "FILL" }, Unit = "m3", Price = 60 },
            new() { Name = "Geotextile / geogrid", Match = { "GEOTEXTILE", "GEOGRID", "GEOFABRIC", "MEMBRANE" }, Unit = "m2", Price = 6 },
            new() { Name = "Crushed rock / gravel", Match = { "CRUSHED", "GRAVEL", "ROCK", "AGGREGATE" }, Unit = "m3", Price = 85 },
        },
    };

    public static RateTable LoadCompany()
    {
        var path = StandardStore.FindSupportFile(FileName);
        if (path is null) return CreateDefault();
        try { return Load(path); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return CreateDefault(); }
    }

    public static RateTable Load(string path)
    {
        try
        {
            var table = JsonSerializer.Deserialize<RateTable>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("The rates file is empty.");
            table.SourcePath = path;
            return table;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The rates file {Path.GetFileName(path)} isn't valid: {ex.Message}", ex);
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
        SourcePath = path;
    }

    public Rate? For(string description)
    {
        var d = description.ToUpperInvariant();
        return Rates.FirstOrDefault(r => r.Match.Any(m => StartsWord(d, m.Trim().ToUpperInvariant())));
    }

    private static bool StartsWord(string text, string word)
    {
        if (word.Length == 0) return false;
        for (int i = text.IndexOf(word, StringComparison.Ordinal); i >= 0; i = text.IndexOf(word, i + 1, StringComparison.Ordinal))
            if (i == 0 || !char.IsLetter(text[i - 1])) return true;
        return false;
    }
}

public static class CostEstimate
{
    public static CalcResult For(Buildup buildup, RateTable rates, double areaM2 = 0)
    {
        var r = new CalcResult { Title = "Rough cost estimate", Reference = rates.SourcePath is null ? "Placeholder rates" : Path.GetFileName(rates.SourcePath) };
        if (areaM2 > 0) r.In("Pavement area", areaM2, "m²", "0");
        double total = 0;
        var rows = new List<string[]>();
        foreach (var layer in buildup.Layers)
        {
            var rate = rates.For(layer.Description);
            double thicknessM = layer.TotalMm / 1000;
            if (rate is null)
            {
                rows.Add(new[] { layer.Description, CalcResult.Num(layer.TotalMm, "0"), "-", "no rate" });
                r.Warn($"No rate for \"{layer.Description}\": add one to {RateTable.FileName}.");
                continue;
            }
            double perM2 = rate.Unit.Trim().ToLowerInvariant() switch
            {
                "m2" => rate.Price,
                "t" => rate.Price * thicknessM * rate.Density,
                _ => rate.Price * thicknessM,
            };
            string basis = rate.Unit.Trim().ToLowerInvariant() switch
            {
                "m2" => $"{rates.Currency}{rate.Price:0.##}/m²",
                "t" => $"{rates.Currency}{rate.Price:0.##}/t x {rate.Density:0.##} t/m³",
                _ => $"{rates.Currency}{rate.Price:0.##}/m³",
            };
            total += perM2;
            r.Lines.Add(new CalcLine($"{rate.Name}: {Short(layer.Description)}", CalcResult.Num(perM2, "0.00"), rates.Currency + "/m²", LineKind.Step,
                layer.TotalMm > 0 ? $"{layer.TotalMm:0} mm @ {basis}" : basis));
            rows.Add(new[] { layer.Description, CalcResult.Num(layer.TotalMm, "0"), basis, CalcResult.Num(perM2, "0.00") });
        }
        if (rates.SubgradePrepPerM2 > 0)
        {
            total += rates.SubgradePrepPerM2;
            r.Lines.Add(new CalcLine("Subgrade preparation", CalcResult.Num(rates.SubgradePrepPerM2, "0.00"), rates.Currency + "/m²"));
            rows.Add(new[] { "Subgrade preparation", "", $"{rates.Currency}{rates.SubgradePrepPerM2:0.##}/m²", CalcResult.Num(rates.SubgradePrepPerM2, "0.00") });
        }
        r.Result("cost_m2", "Cost per m²", total, rates.Currency + "/m²", "0.00");
        if (areaM2 > 0) r.Result("cost_total", "Cost for the area", total * areaM2, rates.Currency, "#,##0");
        r.Headline = $"≈ {rates.Currency}{total:0}/m²" + (areaM2 > 0 ? $"  ·  ≈ {rates.Currency}{total * areaM2:#,##0} for {areaM2:#,##0} m²" : "");
        r.Tables.Add(("Rough cost per layer", new[] { "Layer", "mm", "Rate", rates.Currency + "/m²" }, rows));
        if (rates.Note.Trim().Length > 0) r.Notes.Add(rates.Note.Trim());
        r.Notes.Add("Rough estimate for comparing options only: excludes earthworks, kerbs, drainage, line marking, traffic control, margins and GST.");
        return r;
    }

    private static string Short(string text) => text.Length <= 40 ? text : text[..40].TrimEnd() + "…";
}
