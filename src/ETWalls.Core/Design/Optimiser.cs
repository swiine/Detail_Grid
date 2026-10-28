namespace ETWalls.Core.Design;

/// <summary>A set of wall sizes (only the ones that wall type uses are set).</summary>
public sealed record Sizes(
    double? D = null, double? T1 = null, double? T2 = null, double? Tb = null, double? Toe = null, double? Heel = null,
    double? Bg = null, double? Bt = null, double? GammaWall = null,
    double? S = null, double? Dia = null, double? Dp = null, string? Post = null)
{
    public WallInput Apply(WallInput inp)
    {
        var c = inp.Clone();
        if (D is double d) c.D = d;
        if (T1 is double t1) c.T1 = t1;
        if (T2 is double t2) c.T2 = t2;
        if (Tb is double tb) c.Tb = tb;
        if (Toe is double toe) c.Toe = toe;
        if (Heel is double heel) c.Heel = heel;
        if (Bg is double bg) c.Bg = bg;
        if (Bt is double bt) c.Bt = bt;
        if (GammaWall is double gw) c.GammaWall = gw;
        if (S is double s) c.S = s;
        if (Dia is double dia) c.Dia = dia;
        if (Dp is double dp) c.Dp = dp;
        if (Post is not null) c.Post = Post;
        return c;
    }

    /// <summary>The sizes currently in a design.</summary>
    public static Sizes From(WallInput i) => i.Type switch
    {
        WallType.Cantilever => new(D: i.D, T1: i.T1, T2: i.T2, Tb: i.Tb, Toe: i.Toe, Heel: i.HeelUsed),
        WallType.Post => new(S: i.S, Dia: i.Dia, Dp: i.Dp, Post: i.Post),
        _ => new(D: i.D, Bt: i.Bt, Bg: i.Bg, GammaWall: i.GammaWall),
    };
}

public sealed record SizeOption(string Label, Sizes Sizes, double CostPerMetre, double Util, double SteelKg, double BaseWidth);

public sealed record SizingResult(Sizes Sizes, bool Ok, List<SizeOption> Options, int Tried);

/// <summary>
/// Automatic sizing. <see cref="Optimise"/> tries many sizes, keeps the ones where no check is above the margin,
/// and picks the cheapest (plus a few alternatives). <see cref="AutoSize"/> is the simple fallback that grows
/// whatever fails. Same steps and rounding as the preview page, so both pick the same wall.
/// </summary>
public static class Optimiser
{
    /// <summary>JavaScript Math.round (halves round up), so sizes match the preview exactly.</summary>
    static double JsRound(double v) => Math.Floor(v + 0.5);
    static double R50(double v) => JsRound(v * 20) / 20;
    static double R10(double v) => JsRound(v * 10) / 10;
    static double Up50(double v) => Math.Ceiling(v * 20 - 1e-9) / 20;

    static readonly string[] Stab = { "slide", "over", "ecc", "bear" };

    public static SizingResult Optimise(WallInput inp, double maxUtil = 1)
    {
        var cands = new List<SizeOption>();
        bool Ok(WallResult r) => r.Checks.All(c => c.Util <= maxUtil + 1e-9);
        bool TryG(Sizes g)
        {
            var x = g.Apply(inp); x.NoReo = false;
            var r = WallDesign.RunAll(x);
            if (!Ok(r)) return false;
            var cost = Costs.Estimate(x, r.R);
            cands.Add(new("", g, cost.PerMetre, r.Worst.Util, r.R.Reo?.TotalKg ?? 0, r.R.B));
            return true;
        }
        bool PassesStab(Sizes g)
        {
            var x = g.Apply(inp); x.NoReo = true;
            var cs = new List<Check>(WallDesign.Analyse(x, false).Checks);
            if (inp.Kh > 0) cs.AddRange(WallDesign.Analyse(x, true).Checks);
            return cs.Where(c => Stab.Contains(c.BaseId)).All(c => c.Util <= maxUtil);
        }

        if (inp.Type == WallType.Cantilever)
        {
            double D = inp.D > 0 ? inp.D : 0.5, t1 = 0.2;
            bool toeOnly = inp.Footing == Footing.Toe;
            // Stem: the thinnest stem whose bending and shear pass (the base sizes don't affect them).
            double? t2min = null;
            for (double t = 0.2; t <= 1.2 + 1e-9; t += 0.05)
            {
                var r = WallDesign.RunAll(new Sizes(D: D, T1: t1, T2: R50(t), Tb: 0.6, Toe: toeOnly ? 2.0 : 0.6, Heel: toeOnly ? 0 : 1.5).Apply(inp));
                if (r.Checks.Where(c => c.Id is "stemM" or "stemV").All(c => c.Util <= maxUtil)) { t2min = R50(t); break; }
            }
            if (t2min is double tm && toeOnly)
            {
                // Footing in front only (boundary wall): heel 0, so only the wall and the base hold it down. Grow the toe.
                foreach (var t2 in new[] { tm, R50(tm + 0.05), R50(tm + 0.1) })
                    for (double tb = 0.3; tb <= 0.9 + 1e-9; tb += 0.05)
                    {
                        double toe = 0.3; bool found = false;
                        for (; toe <= 8 + 1e-9; toe += 0.1)
                            if (PassesStab(new(D: D, T1: t1, T2: t2, Tb: R50(tb), Toe: R10(toe), Heel: 0))) { found = true; break; }
                        if (!found) continue;
                        foreach (var tt in new[] { toe, toe + 0.1, toe + 0.2 }) TryG(new(D: D, T1: t1, T2: t2, Tb: R50(tb), Toe: R10(tt), Heel: 0));
                    }
            }
            else if (t2min is double tm2)
            {
                foreach (var t2 in new[] { tm2, R50(tm2 + 0.05), R50(tm2 + 0.1) })
                    for (double tb = 0.3; tb <= 0.8 + 1e-9; tb += 0.05)
                        for (double toe = 0.3; toe <= 2.0 + 1e-9; toe += 0.1)
                        {
                            // Shortest heel that passes stability, then the full checks at that heel and one step longer.
                            double heel = 0.3; bool found = false;
                            for (; heel <= 6 + 1e-9; heel += 0.1)
                                if (PassesStab(new(D: D, T1: t1, T2: t2, Tb: R50(tb), Toe: R10(toe), Heel: R10(heel)))) { found = true; break; }
                            if (!found) continue;
                            foreach (var h in new[] { heel, heel + 0.1 }) TryG(new(D: D, T1: t1, T2: t2, Tb: R50(tb), Toe: R10(toe), Heel: R10(h)));
                        }
            }
        }
        else if (inp.Type is WallType.Gravity or WallType.Block)
        {
            double D = inp.D > 0 ? inp.D : 0.3, gw = inp.Type == WallType.Block ? 21 : 23;
            double[] tops = inp.Type == WallType.Block ? new[] { 0.3, 0.45, 0.6, 0.8, 1.0, 1.2 } : new[] { 0.3, 0.4, 0.5, 0.6, 0.8 };
            foreach (var Bt in tops)
                for (double Bg = Bt + 0.1; Bg <= 8; Bg += 0.05)
                    if (TryG(new(D: D, Bt: Bt, Bg: R50(Bg), GammaWall: gw))) break;
        }
        else
        {
            double s = inp.S > 0 ? inp.S : 2.4;
            foreach (var dia in new[] { 0.45, 0.6, 0.75, 0.9 })
                foreach (var post in WallDesign.Posts.Keys)
                    for (double Dp = 1.0; Dp <= 8; Dp += 0.1)
                    {
                        var g = new Sizes(S: s, Dia: dia, Dp: R10(Dp), Post: post);
                        var r = WallDesign.RunAll(g.Apply(inp));
                        if (!r.Checks.Where(c => c.Id.StartsWith("postM")).All(c => c.Util <= maxUtil)) break;   // needs a bigger post
                        if (TryG(g)) break;
                    }
        }

        if (cands.Count == 0) return new SizingResult(AutoSize(inp).Sizes, false, new(), 0);
        var sorted = cands.OrderBy(c => c.CostPerMetre).ToList();
        var best = sorted[0];
        var near = sorted.Where(c => c.CostPerMetre <= best.CostPerMetre * 1.25).ToList();
        var options = new List<SizeOption> { best with { Label = "Cheapest" } };
        void Pick(string label, Func<SizeOption, double> key)
        {
            var c = near.Where(x => !options.Any(o => o.Sizes == x.Sizes)).OrderBy(key).FirstOrDefault();
            if (c is not null && key(c) < key(options[0]) - 1e-6) options.Add(c with { Label = label });
        }
        Pick(inp.Type == WallType.Post ? "Smaller pier" : "Narrowest base (least digging)", c => c.BaseWidth);
        if (inp.Type == WallType.Cantilever) Pick("Least steel", c => c.SteelKg);
        Pick("Most spare capacity", c => c.Util);
        return new SizingResult(best.Sizes, true, options, cands.Count);
    }

    /// <summary>Rules of thumb, then grow the part that fails until every check passes.</summary>
    public static SizingResult AutoSize(WallInput inp)
    {
        HashSet<string> Failing(Sizes g) => WallDesign.RunAll(g.Apply(inp)).Checks.Where(c => !c.Pass).Select(c => c.BaseId).ToHashSet();
        static bool Has(HashSet<string> f, params string[] ids) => ids.Any(f.Contains);
        SizingResult Done(Sizes g, bool ok) => new(g, ok, new(), 0);

        if (inp.Type == WallType.Post)
        {
            var names = WallDesign.Posts.Keys.ToList();
            var g = new Sizes(S: 2.4, Dia: inp.H <= 1.5 ? 0.45 : 0.6, Dp: Up50(Math.Max(1.0, inp.H)), Post: names[0]);
            for (int i = 0; i < 120; i++)
            {
                var f = Failing(g); if (f.Count == 0) return Done(g, true);
                if (Has(f, "embed")) g = g with { Dp = Up50(g.Dp!.Value + 0.1) };
                if (Has(f, "postM")) { int k = names.IndexOf(g.Post!); if (k == names.Count - 1) return Done(g, false); g = g with { Post = names[k + 1] }; }
            }
            return Done(g, false);
        }
        if (inp.Type is WallType.Gravity or WallType.Block)
        {
            double D = 0.3, Ht = inp.H + D, Bt0 = inp.Type == WallType.Block ? 0.3 : 0.5;
            var g = new Sizes(D: D, Bt: Bt0, Bg: Up50(Math.Max(Bt0 + 0.1, 0.4 * Ht)), GammaWall: inp.Type == WallType.Block ? 21 : 23);
            for (int i = 0; i < 120; i++)
            {
                if (inp.Type == WallType.Block) g = g with { Bt = Math.Max(0.3, Up50(0.45 * g.Bg!.Value)) };   // stepped blocks: the top grows with the base
                if (Failing(g).Count == 0) return Done(g, true);
                g = g with { Bg = Up50(g.Bg!.Value + 0.05) };
            }
            return Done(g, false);
        }
        {
            double D = 0.5, Ht = inp.H + D;
            bool toeOnly = inp.Footing == Footing.Toe;
            var g = new Sizes(D: D, T1: 0.2, T2: Math.Max(0.25, Up50(Ht / 12)), Tb: Math.Max(0.35, Up50(Ht / 10)),
                Toe: toeOnly ? Up50(0.6 * Ht) : Math.Max(0.4, Up50(0.15 * Ht)), Heel: toeOnly ? 0 : Math.Max(0.6, Up50(0.35 * Ht)));
            for (int i = 0; i < 120; i++)
            {
                var f = Failing(g); if (f.Count == 0) return Done(g, true);
                if (toeOnly && Has(f, "bear", "ecc", "slide", "over")) g = g with { Toe = Up50(g.Toe!.Value + 0.1) };
                else if (Has(f, "bear", "ecc")) g = g with { Toe = Up50(g.Toe!.Value + 0.1), Heel = Up50(g.Heel!.Value + 0.1) };
                else if (Has(f, "slide", "over")) g = g with { Heel = Up50(g.Heel!.Value + 0.1) };
                if (Has(f, "stemM", "stemV", "bars")) g = g with { T2 = Up50(g.T2!.Value + 0.05) };
                if (Has(f, "heelM", "heelV", "toeM", "toeV", "anchor", "bars")) g = g with { Tb = Up50(g.Tb!.Value + 0.05) };
            }
            return Done(g, false);
        }
    }
}
