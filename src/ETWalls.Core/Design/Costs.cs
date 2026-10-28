namespace ETWalls.Core.Design;

public sealed record CostItem(string Key, string Name, double Qty, string Unit, double Rate)
{
    public double Cost => Qty * Rate;
}

public sealed record CostEstimate(List<CostItem> Items)
{
    public double PerMetre => Items.Sum(i => i.Cost);
}

/// <summary>Quantities per metre of wall times the rates. A guide for comparing options, not a quote.</summary>
public static class Costs
{
    public static readonly IReadOnlyDictionary<string, double> PostKgPerM = new Dictionary<string, double>
    {
        ["150UC23"] = 23.4, ["200UC46"] = 46.2, ["250UC73"] = 72.9, ["310UC97"] = 96.8,
    };

    public static CostEstimate Estimate(WallInput inp, Analysis res)
    {
        var R = inp.Rates;
        bool cut = inp.Site != Site.Fill;
        var items = new List<CostItem>();
        void Add(string key, string name, double qty, string unit, double rate) { if (qty > 1e-9) items.Add(new(key, name, qty, unit, rate)); }
        double DrainZone(double h) => inp.Drained ? 0.3 * h : 0;

        if (inp.Type == WallType.Cantilever)
        {
            double toe = inp.Toe, t1 = inp.T1, t2 = inp.T2, tb = inp.Tb, heel = inp.HeelUsed, D = inp.D, H = inp.H;
            double hs = H + D - tb, B = toe + t2 + heel;
            Add("concrete", "Concrete (stem and base)", (t1 + t2) / 2 * hs + B * tb, "m³", R.Concrete);
            if (res.Reo is not null) Add("steel", "Reinforcement", res.Reo.TotalKg, "kg", R.Steel);
            Add("formwork", "Formwork (stem faces, base edges)", 2 * hs + 2 * tb, "m²", R.Formwork);
            double digBack = cut ? (heel + t2 + 0.3) * H : 0;
            Add("excavation", "Excavation" + (cut ? " (footing and behind the wall)" : " (footing)"), (B + 0.6) * D + digBack, "m³", R.Excavation);
            Add("drainage", "Drainage gravel behind the stem", DrainZone(hs), "m³", R.Drainage);
            if (cut) Add("backfill", heel > 0 ? "Compacted backfill over the heel" : "Compacted backfill behind the wall", Math.Max(0, digBack - DrainZone(hs)), "m³", R.Backfill);
            if (inp.Drained) Add("agpipe", "Ag pipe", 1, "m", R.AgPipe);
        }
        else if (inp.Type is WallType.Gravity or WallType.Block)
        {
            double Bg = inp.Bg, Bt = inp.Bt, D = inp.D, H = inp.H, Ht = H + D, vol = (Bg + Bt) / 2 * Ht;
            if (inp.Type == WallType.Block)
            {
                Add("blocks", "Block wall (supply, lay, fill)", vol, "m³", R.Blocks);
                Add("concrete", "Levelling pad", 0.15 * (Bg + 0.2), "m³", R.Concrete);
            }
            else
            {
                Add("concrete", "Mass concrete", vol, "m³", R.Concrete);
                Add("formwork", "Formwork (both faces)", Ht + Math.Sqrt(Ht * Ht + (Bg - Bt) * (Bg - Bt)), "m²", R.Formwork);
            }
            double digBack = cut ? 0.6 * H : 0;
            Add("excavation", "Excavation" + (cut ? " (footing and working room)" : " (footing)"), (Bg + 0.6) * D + digBack, "m³", R.Excavation);
            Add("drainage", "Drainage gravel behind the wall", DrainZone(Ht), "m³", R.Drainage);
            if (cut) Add("backfill", "Compacted backfill", Math.Max(0, digBack - DrainZone(Ht)), "m³", R.Backfill);
            if (inp.Drained) Add("agpipe", "Ag pipe", 1, "m", R.AgPipe);
        }
        else
        {
            double H = inp.H, Dp = inp.Dp, dia = inp.Dia, s = inp.S, per = 1 / s;
            double kgm = PostKgPerM.TryGetValue(inp.Post, out var k) ? k : 46.2;
            Add("postSteel", $"Steel posts {inp.Post} (one every {WallDesign.G(s)} m)", kgm * (H + Dp - 0.15) * per, "kg", R.PostSteel);
            Add("drilling", $"Bored piers Ø{Math.Round(dia * 1000)}", Dp * per, "m", R.Drilling * Math.Pow(dia / 0.6, 2));
            Add("concrete", "Pier concrete", Math.PI * dia * dia / 4 * Dp * per, "m³", R.Concrete);
            if (res.Reo is not null) Add("steel", "Pier reinforcement", res.Reo.TotalKg * per, "kg", R.Steel);
            Add("sleepers", "Sleepers", H, "m²", R.Sleepers);
            Add("drainage", "Drainage gravel behind the sleepers", DrainZone(H), "m³", R.Drainage);
            if (inp.Drained) Add("agpipe", "Ag pipe", 1, "m", R.AgPipe);
        }
        return new CostEstimate(items);
    }
}
