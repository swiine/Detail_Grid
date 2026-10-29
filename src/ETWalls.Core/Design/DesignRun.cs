namespace ETWalls.Core.Design;

/// <summary>
/// One complete design: the sizes actually used (automatic or custom), every check, the cost and the
/// options the cost search found. What the app shows and the reports print.
/// </summary>
public sealed class DesignRun
{
    public required WallInput Input { get; init; }
    public required WallResult Result { get; init; }
    public required CostEstimate Cost { get; init; }
    /// <summary>The cost search (always run, so a custom size can be compared with the cheapest).</summary>
    public required SizingResult Sizing { get; init; }

    public double TotalCost => Cost.PerMetre * Input.Length;

    /// <summary>Sizes the wall (unless Custom) and runs every check.</summary>
    public static DesignRun For(WallInput input)
    {
        // The search uses the key as entered for a custom size, none when the key is switched off, or tries depths.
        var search = input.Clone();
        if (input.Custom || search.Type != WallType.Cantilever) search.AutoKey = false;
        if (!input.Custom && !input.AutoKey) search.Key = 0;
        var sizing = Optimiser.Optimise(search, input.Margin);
        var used = input.Custom ? input.Clone() : sizing.Sizes.Apply(search);
        if (used.Type == WallType.Cantilever && used.Footing == Footing.Toe) used.Heel = 0;
        var result = WallDesign.RunAll(used);
        return new DesignRun { Input = used, Result = result, Cost = Costs.Estimate(used, result.R), Sizing = sizing };
    }

    /// <summary>"Base 2000 (toe 300, heel 1400), stem 200–300, base 300 thick" and the like (mm).</summary>
    public static string SizeText(WallInput i)
    {
        static string Mm(double v) => Math.Floor(v * 1000 + 0.5).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return i.Type switch
        {
            WallType.Cantilever => $"Base {Mm(i.BaseWidth)} (toe {Mm(i.Toe)}, heel {Mm(i.HeelUsed)}), stem {Mm(i.T1)}–{Mm(i.T2)}, base {Mm(i.Tb)} thick{(i.KeyUsed > 0 ? $", {Mm(i.KeyUsed)} shear key" : "")}",
            WallType.Post => $"{i.Post} at {i.S.ToString(System.Globalization.CultureInfo.InvariantCulture)} m, Ø{Mm(i.Dia)} pier {Mm(i.Dp)} deep",
            _ => $"Base {Mm(i.Bg)}, top {Mm(i.Bt)}",
        };
    }

    public static string TypeName(WallType t) => t switch
    {
        WallType.Cantilever => "Cantilever concrete wall",
        WallType.Block => "Block wall (gravity)",
        WallType.Gravity => "Mass gravity wall",
        _ => "Sleeper and post wall",
    };

    public static string MarginText(double m) => m >= 0.999 ? "None: size to 100% (code minimum)" : $"{Math.Round((1 - m) * 100)}% spare (no check above {Math.Round(m * 100)}%)";

    public const string MethodNote =
        "Preview method, to be replaced by the company spreadsheet method. Earth pressure: Rankine (Ka with backfill slope), surcharge as a uniform pressure, " +
        "Seed–Whitman earthquake increment. Stability: factors of safety 1.5 sliding, 2.0 overturning (1.1 and 1.5 with earthquake), resultant in the middle third, " +
        "bearing against the allowable pressure. Posts: Broms short free-head pile, factor of safety 2.0 (1.5 earthquake). Reinforcement: AS 3600-2018 rectangular " +
        "stress block, φ 0.85 bending, φ 0.7 shear (no ligatures), minimum steel, crack-control spacing 300 mm, Lsy.tb development lengths, laps 1.25 Lsy.t. " +
        "Costs: quantities per metre times the rates in section 3; a guide for comparing options, not a quote.";
}
