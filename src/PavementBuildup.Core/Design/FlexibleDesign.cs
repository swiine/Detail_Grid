namespace PavementBuildup.Core.Design;

/// <summary>
/// Granular pavement with thin bituminous surfacing (Austroads AGPT02), as in the company workbook
/// "Pavement build-ups": Fig 8.4 (heavily trafficked) and Fig 12.2 (lightly trafficked) give the total
/// granular thickness; the company table gives the AC / DGB20 / DGS20 split.
/// </summary>
public static class FlexibleDesign
{
    /// <summary>Austroads Fig 8.4: (219 − 211·log CBR + 58·log²CBR) · log(DESA / 120), mm.</summary>
    public static double Fig84(double cbr, double desa) => Curve(cbr) * Math.Log10(desa / 120);

    /// <summary>Austroads Fig 12.2 (lightly trafficked): max(100, 0.475 · (…) · log(14 · DESA)), mm.</summary>
    public static double Fig122(double cbr, double desa) => Math.Max(100, 0.475 * Curve(cbr) * Math.Log10(14 * desa));

    private static double Curve(double cbr)
    {
        double l = Math.Log10(cbr);
        return 219 - 211 * l + 58 * l * l;
    }

    /// <summary>Lightly-trafficked chart below 1×10⁵ ESA, otherwise Fig 8.4 (they meet at 1×10⁵).</summary>
    public const double LightTrafficLimit = 1e5;

    /// <summary>Traffic levels (rows) of the company table, with their descriptions.</summary>
    public static readonly (double Desa, string Label, string Description)[] TrafficRows =
    {
        (1e4, "1 x 10^4", "Minor road, infrequent heavy vehicles"),
        (5e4, "5 x 10^4", "Minor road with occasional heavy vehicles"),
        (1e5, "1 x 10^5", "Local access, infrequent heavy vehicles (SI EFSG min. for vehicular traffic)"),
        (5e5, "5 x 10^5", "Local access with occasional heavy vehicles (SI EFSG min. for buses & trucks)"),
        (1e6, "1 x 10^6", "Collector road with heavy vehicles"),
        (5e6, "5 x 10^6", "Main road (liaise with the relevant authorities)"),
        (1e7, "1 x 10^7", "Main road (liaise with the relevant authorities)"),
    };

    /// <summary>
    /// Company table (Pavement Design Table Updated): DGB20 base and DGS20 subbase (mm) by traffic row and
    /// CBR 1..10 %. Surfacing: 40 AC10 up to 5×10⁵ ESA, 50 AC14 above.
    /// </summary>
    private static readonly int[,] Base =
    {
        { 150, 150, 150, 140, 120, 100, 100, 100, 100, 100 },
        { 150, 150, 150, 150, 130, 100, 100, 100, 100, 100 },
        { 150, 150, 150, 150, 150, 115, 120, 100, 100, 100 },
        { 200, 200, 150, 150, 150, 150, 150, 125, 125, 120 },
        { 200, 200, 150, 150, 150, 150, 150, 150, 140, 130 },
        { 200, 200, 150, 150, 150, 150, 150, 150, 150, 150 },
        { 200, 200, 200, 200, 200, 200, 200, 160, 160, 160 },
    };

    private static readonly int[,] Subbase =
    {
        { 385, 245, 175, 140, 125, 120, 105, 100, 100, 100 },
        { 460, 300, 215, 165, 150, 150, 130, 110, 100, 100 },
        { 490, 320, 235, 180, 150, 150, 120, 125, 110, 100 },
        { 595, 385, 330, 260, 215, 180, 150, 150, 130, 120 },
        { 660, 430, 370, 295, 245, 205, 175, 150, 140, 130 },
        { 815, 545, 460, 375, 315, 265, 230, 200, 180, 155 },
        { 880, 595, 450, 360, 295, 245, 205, 215, 190, 165 },
    };

    /// <summary>Designs the pavement for a subgrade CBR (%) and design traffic (ESA).</summary>
    public static FlexibleResult Design(double cbr, double desa)
    {
        if (!(cbr > 0)) throw new ArgumentException("Enter a subgrade CBR greater than 0%.");
        if (!(desa >= 1000)) throw new ArgumentException("Enter the design traffic in ESA (at least 1,000).");

        double fig84 = Fig84(cbr, desa), fig122 = Fig122(cbr, desa);
        bool light = desa < LightTrafficLimit;
        double required = light ? fig122 : fig84;
        var notes = new List<string>();

        // Company table: the next traffic row up and the next CBR column down (both on the safe side).
        int row = Array.FindIndex(TrafficRows, r => r.Desa >= desa * 0.999);
        int col = (int)Math.Clamp(Math.Floor(cbr + 1e-9), 1, 10) - 1;
        int baseMm, subbaseMm;
        string surfacing;
        if (row >= 0)
        {
            baseMm = Base[row, col];
            subbaseMm = Subbase[row, col];
            surfacing = TrafficRows[row].Desa <= 5e5 ? "40 AC10" : "50 AC14";
            notes.Add($"Company table: {TrafficRows[row].Label} ESA row, CBR {col + 1}% column ({TrafficRows[row].Description}).");
            if (cbr > 10) notes.Add("CBR above 10%: the CBR 10% column is used (conservative).");
            if (cbr < 1) notes.Add("CBR below 1%: the CBR 1% column is used - treat the subgrade first.");
        }
        else
        {
            // Beyond the table: Austroads total, DGB 200 and the rest as subbase.
            surfacing = "50 AC14";
            baseMm = 200;
            subbaseMm = Math.Max(100, RoundUp5(required) - baseMm);
            notes.Add("Design traffic is above the company table (1 x 10^7 ESA): total from Austroads Fig 8.4, base 200 mm. Liaise with the relevant authority.");
        }

        int granular = baseMm + subbaseMm;
        if (granular + 0.5 < required)
        {
            // Never less than Austroads: add the shortfall to the subbase.
            int add = RoundUp5(required) - granular;
            subbaseMm += add;
            granular += add;
            notes.Add($"Subbase increased by {add} mm to meet the Austroads thickness.");
        }
        if (cbr < 3) notes.Add("Subgrade stabilisation is generally required for CBR < 3%.");
        notes.Add("Granular totals do not include the AC thickness (Austroads allows it to be included).");
        notes.Add("Granular layers over 150 mm are placed in 100-150 mm layers.");

        return new FlexibleResult(cbr, desa, fig84, fig122, required, light, surfacing, baseMm, subbaseMm, granular, notes);
    }

    private static int RoundUp5(double mm) => (int)(Math.Ceiling(mm / 5 - 1e-9) * 5);

    /// <summary>The design as a worked calculation sheet, with the build-up for the detail.</summary>
    public static CalcResult Calc(double cbr, double desa, string? title = null)
    {
        var d = Design(cbr, desa);
        var r = new CalcResult
        {
            Title = "Flexible pavement - granular with thin bituminous surfacing",
            Reference = "Austroads AGPT02 Fig 8.4 / Fig 12.2; company design table",
        };
        r.In("Subgrade CBR", cbr, "%");
        r.In("Design traffic DESA", desa, "ESA", "0.##E+0");
        r.Step("fig84", "Fig 8.4  t = (219 − 211 log CBR + 58 log²CBR) log(DESA/120)", d.Fig84, "mm", "0.0", "heavily trafficked");
        r.Step("fig122", "Fig 12.2 t = max(100, 0.475 (…) log(14 DESA))", d.Fig122, "mm", "0.0", "lightly trafficked");
        r.Result("required", d.LightlyTrafficked ? "Required granular thickness (Fig 12.2, DESA < 1x10^5)" : "Required granular thickness (Fig 8.4)", d.Required, "mm");
        r.Result("ac", "Surfacing " + d.AcType, d.AcMm, "mm", "0");
        r.Result("base", "DGB20 base", d.BaseMm, "mm", "0");
        r.Result("subbase", "DGS20/40 subbase", d.SubbaseMm, "mm", "0");
        r.Result("granular", "Granular total (base + subbase)", d.GranularMm, "mm", "0");
        r.Check("Granular total ≥ Austroads", d.GranularMm + 0.5 >= d.Required,
            $"{d.GranularMm} vs {CalcResult.Num(d.Required, "0")} mm");
        foreach (var n in d.Notes) r.Notes.Add(n);
        r.ThicknessMm = d.AcMm + d.GranularMm;
        r.Headline = $"{d.Surfacing} / {d.BaseMm} DGB20 / {d.SubbaseMm} DGS20 = {d.GranularMm} mm granular (Austroads {CalcResult.Num(d.Required, "0")} mm)";
        r.BuildupText = d.BuildupText(title);
        return r;
    }
}

public sealed record FlexibleResult(
    double Cbr, double Desa, double Fig84, double Fig122, double Required, bool LightlyTrafficked,
    string Surfacing, int BaseMm, int SubbaseMm, int GranularMm, IReadOnlyList<string> Notes)
{
    public int AcMm => int.Parse(Surfacing.Split(' ')[0]);
    public string AcType => Surfacing.Split(' ')[1];

    /// <summary>The build-up in the company's specification wording, ready for the detail.</summary>
    public string BuildupText(string? title = null)
    {
        string cbr = Cbr.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(title)) lines.Add($"[{title}]");
        lines.Add($"{AcMm}mm COMPACTED THICKNESS {AcType} WEARING COURSE");
        lines.Add("PRIME");
        lines.Add($"{BaseMm}mm COMPACTED THICKNESS DGB20 CLASS 1 BASE TO 98% MMDD AT ±2% OMC");
        lines.Add($"{SubbaseMm}mm COMPACTED THICKNESS DGS20/40 SUBBASE TO 98% MMDD AT ±2% OMC");
        lines.Add($"SUBGRADE MINIMUM CBR {cbr}% COMPACTED TO 98% SMDD AT ±2% OMC");
        return string.Join("\n", lines);
    }
}
