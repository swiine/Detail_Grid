namespace PavementBuildup.Core.Design;

/// <summary>Traffic for a flexible pavement design, either known or worked out from counts.</summary>
public sealed record TrafficInputs(
    double KnownDesa,              // > 0: use this DESA and ignore the counts
    double Aadt = 0,               // vehicles per day, both directions
    double HeavyPercent = 0,       // % heavy vehicles
    double DirectionFactor = 0.5,  // share in the design direction
    double LaneFactor = 1,         // share in the design lane
    double GrowthPercent = 0,      // annual growth, % per year
    double Years = 20,             // design life
    double EsaPerHeavy = 1);       // ESA per heavy vehicle (= axle groups per HV x ESA per group)

/// <summary>
/// Road / car park design: traffic (known or from counts), Austroads thickness, the company build-up, and a
/// thickness-vs-CBR table, as one calculation.
/// </summary>
public static class RoadDesign
{
    /// <summary>Cumulative growth factor ((1 + r)^P − 1) / r, or P with no growth.</summary>
    public static double GrowthFactor(double growthPercent, double years)
    {
        double r = growthPercent / 100;
        return Math.Abs(r) < 1e-9 ? years : (Math.Pow(1 + r, years) - 1) / r;
    }

    /// <summary>DESA = 365 x AADT x DF x LDF x %HV/100 x CGF x ESA/HV (Austroads AGPT02 form).</summary>
    public static CalcResult Traffic(TrafficInputs t)
    {
        var r = new CalcResult { Title = "Design traffic", Reference = "Austroads AGPT02 (design traffic)" };
        r.In("AADT (both directions)", t.Aadt, "veh/day", "0");
        r.In("Heavy vehicles", t.HeavyPercent, "%");
        r.In("Direction factor DF", t.DirectionFactor);
        r.In("Lane distribution factor LDF", t.LaneFactor);
        r.In("Annual growth", t.GrowthPercent, "%");
        r.In("Design life", t.Years, "years");
        r.In("ESA per heavy vehicle", t.EsaPerHeavy);
        if (!(t.Aadt > 0) || !(t.HeavyPercent > 0) || !(t.Years > 0))
            throw new ArgumentException("Enter the AADT, % heavy vehicles and design life (or a known DESA).");
        double hv = r.Step("hvday", "Heavy vehicles per day in the design lane = AADT x DF x LDF x %HV", t.Aadt * t.DirectionFactor * t.LaneFactor * t.HeavyPercent / 100, "HV/day", "0.0");
        double cgf = r.Step("cgf", "Cumulative growth factor ((1+r)^P − 1)/r", GrowthFactor(t.GrowthPercent, t.Years), "", "0.00");
        double nhv = r.Step("nhv", "Heavy vehicles over the design life = 365 x HV/day x CGF", 365 * hv * cgf, "HV", "0");
        double desa = r.Result("desa", "DESA = heavy vehicles x ESA/HV", nhv * t.EsaPerHeavy, "ESA", "0");
        r.Headline = $"DESA = {desa:0.00E+0} ESA";
        r.Notes.Add("ESA per heavy vehicle depends on the road and vehicle mix: use project traffic data where you have it.");
        return r;
    }

    /// <summary>The whole flexible design: traffic, Austroads check, company build-up and a thickness-vs-CBR table.</summary>
    public static CalcResult Design(double cbr, TrafficInputs traffic, string? title = null)
    {
        CalcResult? trafficCalc = null;
        double desa = traffic.KnownDesa;
        if (!(desa > 0))
        {
            trafficCalc = Traffic(traffic);
            desa = trafficCalc.Values["desa"];
        }
        var design = FlexibleDesign.Calc(cbr, desa, title);

        var r = new CalcResult
        {
            Title = "Road / car park pavement design",
            Reference = design.Reference,
            Headline = design.Headline,
            Pass = design.Pass,
            ThicknessMm = design.ThicknessMm,
            BuildupText = design.BuildupText,
        };
        r.In("Subgrade CBR", cbr, "%");
        r.In("Design traffic", desa, "ESA", "0.##E+0");
        r.Values["desa"] = desa;
        foreach (var line in design.Lines.Where(l => l.Kind != LineKind.Input)) r.Lines.Add(line);
        foreach (var key in design.Values.Keys) r.Values[key] = design.Values[key];
        r.Notes.AddRange(design.Notes);
        if (trafficCalc is not null) r.Parts.Add(trafficCalc);

        // What if the CBR is different? (same traffic)
        var rows = new List<string[]>();
        foreach (var c in new[] { 2.0, 3, 4, 5, 7, 10, 15 }.Append(cbr).Distinct().OrderBy(x => x))
        {
            var alt = FlexibleDesign.Design(c, desa);
            rows.Add(new[]
            {
                (Math.Abs(c - cbr) < 1e-9 ? "► " : "") + CalcResult.Num(c, "0.#") + "%",
                alt.Surfacing, alt.BaseMm.ToString(), alt.SubbaseMm.ToString(), alt.GranularMm.ToString(), CalcResult.Num(alt.Required, "0"),
            });
        }
        r.Tables.Add(("Thickness vs subgrade CBR (same traffic)", new[] { "CBR", "Surfacing", "DGB20", "DGS20", "Granular", "Austroads" }, rows));

        // And if the traffic is one level up or down?
        var trows = new List<string[]>();
        foreach (var row in FlexibleDesign.TrafficRows)
        {
            var alt = FlexibleDesign.Design(cbr, row.Desa);
            trows.Add(new[] { (Math.Abs(row.Desa - desa) / desa < 0.01 ? "► " : "") + row.Label, alt.Surfacing, alt.BaseMm.ToString(), alt.SubbaseMm.ToString(), alt.GranularMm.ToString() });
        }
        r.Tables.Add(($"Thickness vs traffic (CBR {CalcResult.Num(cbr, "0.#")}%)", new[] { "DESA", "Surfacing", "DGB20", "DGS20", "Granular" }, trows));
        r.Parts.Add(design);
        return r;
    }
}
