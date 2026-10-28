namespace ETWalls.Core.Design;

public sealed record WhatIfRow(double Value, bool Current, IReadOnlyList<Check> Checks, double CostPerMetre, bool Pass);

public sealed record WhatIfTable(string Label, IReadOnlyList<string> Columns, IReadOnlyList<WhatIfRow> Rows);

public sealed record PhiRow(double Phi, Check Worst, bool Pass);

/// <summary>"What if" tables: the main size a step either side, and the backfill friction angle.</summary>
public static class WhatIf
{
    static readonly string[] Keep = { "slide", "over", "ecc", "bear", "stemM", "heelM", "toeM", "embed", "postM" };

    public static WhatIfTable Size(WallInput inp)
    {
        string label; Func<WallInput, double> get; Action<WallInput, double> set; double[] vals;
        if (inp.Type == WallType.Cantilever && inp.Footing == Footing.Toe)
        {
            label = "Toe length (m)"; get = i => i.Toe; set = (i, v) => i.Toe = v;
            vals = new[] { -0.6, -0.3, 0, 0.3, 0.6, 0.9 }.Select(d => Math.Round(inp.Toe + d, 2)).Where(v => v > 0.2).ToArray();
        }
        else if (inp.Type == WallType.Cantilever)
        {
            label = "Heel length (m)"; get = i => i.Heel; set = (i, v) => i.Heel = v;
            vals = new[] { -0.4, -0.2, 0, 0.2, 0.4, 0.6 }.Select(d => Math.Round(inp.Heel + d, 2)).Where(v => v > 0).ToArray();
        }
        else if (inp.Type == WallType.Post)
        {
            label = "Pier embedment (m)"; get = i => i.Dp; set = (i, v) => i.Dp = v;
            vals = new[] { -0.6, -0.3, 0, 0.3, 0.6 }.Select(d => Math.Round(inp.Dp + d, 2)).Where(v => v > 0.3).ToArray();
        }
        else
        {
            label = "Base width (m)"; get = i => i.Bg; set = (i, v) => i.Bg = v;
            vals = new[] { -0.3, -0.15, 0, 0.15, 0.3, 0.45 }.Select(d => Math.Round(inp.Bg + d, 2)).Where(v => v > inp.Bt).ToArray();
        }
        var rows = new List<WhatIfRow>();
        List<string>? cols = null;
        foreach (var v in vals)
        {
            var x = inp.Clone(); set(x, v);
            var r = WallDesign.RunAll(x);
            var kept = r.Checks.Where(c => Keep.Contains(c.Id)).ToList();
            cols ??= kept.Select(c => c.Name).ToList();
            rows.Add(new(v, Math.Abs(v - get(inp)) < 1e-6, kept, Costs.Estimate(x, r.R).PerMetre, r.Pass));
        }
        return new WhatIfTable(label, cols ?? new(), rows);
    }

    public static List<PhiRow> BackfillPhi(WallInput inp) =>
        new[] { 26.0, 28, 30, 32, 34, 36 }.Select(p =>
        {
            var x = inp.Clone(); x.Phi = p;
            var r = WallDesign.RunAll(x);
            return new PhiRow(p, r.Worst, r.Pass);
        }).ToList();
}
