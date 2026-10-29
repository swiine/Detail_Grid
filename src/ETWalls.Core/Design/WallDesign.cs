using System.Globalization;

namespace ETWalls.Core.Design;

/// <summary>
/// The wall checks (preview method, to be replaced by the company spreadsheet method):
/// Rankine earth pressure, factors of safety for sliding / overturning, middle third, bearing;
/// AS 3600-2018 style reinforcement for cantilever walls; Broms embedment for post walls.
/// A line-for-line port of calc.js in the preview page, so both give the same numbers.
/// </summary>
public static class WallDesign
{
    public const double GammaW = 9.81, GammaC = 24;
    internal const double D2R = Math.PI / 180;

    public static double KaRankine(double phi, double beta)
    {
        double p = phi * D2R, b = Math.Min(beta, phi - 0.01) * D2R;
        double cb = Math.Cos(b), r = Math.Sqrt(Math.Max(0, cb * cb - Math.Pow(Math.Cos(p), 2)));
        return cb * (cb - r) / (cb + r);
    }

    public static double KpRankine(double phi) => Math.Pow(Math.Tan((45 + phi / 2) * D2R), 2);

    internal static string F(double v, int dp) => double.IsFinite(v) ? v.ToString("F" + dp, CultureInfo.InvariantCulture) : "∞";
    internal static string G(double v) => v.ToString(CultureInfo.InvariantCulture);

    internal static Check Chk(string id, string name, double value, double limit, string unit, double util, string note) =>
        new(id, name, value, limit, unit, util, note);

    // ---------- stability of a wall acting as one block ----------

    internal static Stability Stability(WallInput inp, List<Weight> weights, double backX, double backH, double B, bool seismic, double key = 0)
    {
        double beta = inp.Slope, Ka = KaRankine(inp.Phi, beta);
        double cb = Math.Cos(beta * D2R), sb = Math.Sin(beta * D2R);
        double H = backH;
        var loads = new List<Load>();
        double Pa = 0.5 * Ka * inp.Gamma * H * H;
        loads.Add(new("Soil", Pa * cb, Pa * sb, H / 3, backX));
        if (inp.Q > 0)
        {
            double Pq = Ka * inp.Q * H;
            loads.Add(new("Surcharge", Pq * cb, Pq * sb, H / 2, backX));
        }
        double U = 0, xU = 0;
        if (!inp.Drained && inp.Hw > 0)
        {
            double hw = Math.Min(inp.Hw, H);
            loads.Add(new("Water", 0.5 * GammaW * hw * hw, 0, hw / 3, backX));
            U = 0.5 * GammaW * hw * B; xU = 2 * B / 3;
        }
        if (seismic && inp.Kh > 0)
        {
            loads.Add(new("Earthquake", 0.375 * inp.Kh * inp.Gamma * H * H, 0, 0.6 * H, backX));
            foreach (var w in weights) loads.Add(new("Wall inertia", inp.Kh * w.W, 0, w.Y, w.X));
        }
        double sumH = loads.Sum(l => l.Ph);
        double Mo = loads.Sum(l => l.Ph * l.Y) + U * xU;
        double sumW = weights.Sum(w => w.W);
        double V = sumW + loads.Sum(l => l.Pv) - U;
        double Mr = weights.Sum(w => w.W * w.X) + loads.Sum(l => l.Pv * l.X);

        double passive = 0;
        if (inp.UsePassive && inp.D > 0) passive = 0.5 * 0.5 * KpRankine(inp.PhiF) * inp.GammaF * inp.D * inp.D; // half mobilised
        // Shear key under the base: the soil in front of the key pushes back (half mobilised, like the passive above).
        double keyPush = key > 0 ? 0.5 * 0.5 * KpRankine(inp.PhiF) * inp.GammaF * (Math.Pow(inp.D + key, 2) - inp.D * inp.D) : 0;
        double slideR = Math.Max(0, V) * Math.Tan(inp.Delta * D2R) + inp.CF * B + passive + keyPush;

        double xR = (Mr - Mo) / V;   // resultant from the toe
        double e = B / 2 - xR;
        double qmax;
        if (V <= 0 || xR <= 0) qmax = double.PositiveInfinity;
        else if (Math.Abs(e) <= B / 6) qmax = V / B * (1 + 6 * Math.Abs(e) / B);
        else qmax = 2 * V / (3 * (B / 2 - Math.Abs(e)));
        double qmin = Math.Abs(e) <= B / 6 ? V / B * (1 - 6 * Math.Abs(e) / B) : 0;

        double fsS = seismic ? 1.1 : 1.5, fsO = seismic ? 1.5 : 2.0;
        string sfx = seismic ? " (earthquake)" : "";
        double FoSs = slideR / sumH, FoSo = Mr / Math.Max(1e-9, Mo);
        var checks = new List<Check>
        {
            Chk("slide" + sfx, "Sliding" + sfx, FoSs, fsS, "FoS", fsS / FoSs,
                $"Resists {F(slideR, 1)} kN/m (base friction δ = {G(inp.Delta)}°{(inp.CF > 0 ? ", cohesion" : "")}{(passive > 0 ? ", passive" : "")}{(keyPush > 0 ? $", shear key {F(keyPush, 1)} kN/m" : "")}) vs pushes {F(sumH, 1)} kN/m."),
            Chk("over" + sfx, "Overturning" + sfx, FoSo, fsO, "FoS", fsO / FoSo,
                $"Restoring {F(Mr, 1)} kNm/m vs overturning {F(Mo, 1)} kNm/m about the toe."),
            Chk("ecc" + sfx, "Resultant in middle third" + sfx, Math.Abs(e), B / 6, "m", Math.Abs(e) / (B / 6),
                $"e = {F(Math.Abs(e), 3)} m, limit B/6 = {F(B / 6, 3)} m."),
            Chk("bear" + sfx, "Bearing pressure" + sfx, qmax, inp.Qa, "kPa", qmax / inp.Qa,
                $"q max {F(qmax, 0)} kPa, q min {F(qmin, 0)} kPa, allowable {G(inp.Qa)} kPa."),
        };
        return new Stability(Ka, loads, sumH, Mo, V, Mr, e, qmax, qmin, checks);
    }

    // ---------- wall types ----------

    public static Analysis Cantilever(WallInput inp, bool seismic = false)
    {
        double H = inp.H, D = inp.D, tb = inp.Tb, toe = inp.Toe, heel = inp.HeelUsed, t1 = inp.T1, t2 = inp.T2;
        double Ht = H + D, hs = Ht - tb, B = toe + t2 + heel, tanB = Math.Tan(inp.Slope * D2R);
        double wBack = B - (toe + t1);   // soil width over the heel at the top of the stem
        var weights = new List<Weight>
        {
            new(t1 * hs * GammaC, toe + t1 / 2, tb + hs / 2),
            new((t2 - t1) * hs / 2 * GammaC, toe + t1 + (t2 - t1) / 3, tb + hs / 3),
            new(B * tb * GammaC, B / 2, tb / 2),
            new(heel * hs * inp.Gamma, toe + t2 + heel / 2, tb + hs / 2),
            new((t2 - t1) * hs / 2 * inp.Gamma, toe + t1 + 2 * (t2 - t1) / 3, tb + 2 * hs / 3),
        };
        if (tanB > 0) weights.Add(new(0.5 * wBack * wBack * tanB * inp.Gamma, B - wBack / 3, Ht + wBack * tanB / 3));
        if (D > tb) weights.Add(new((D - tb) * toe * inp.Gamma, toe / 2, tb + (D - tb) / 2));
        double hk = inp.KeyUsed, kw = inp.KeyWidth;   // shear key under the stem
        if (hk > 0) weights.Add(new(kw * hk * GammaC, toe + kw / 2, -hk / 2));
        double backH = Ht + wBack * tanB;
        var st = Stability(inp, weights, B, backH, B, seismic, hk);
        var a = new Analysis(WallType.Cantilever, B, Ht, B, backH) { Hs = hs }.With(st);
        if (seismic || inp.NoReo) return a;
        a.Reo = Reinforcement.ForCantilever(inp, hs, B, st);
        a.Checks.AddRange(a.Reo.Checks);
        return a;
    }

    public static Analysis Gravity(WallInput inp, bool seismic = false)
    {
        double Ht = inp.H + inp.D, gw = inp.GammaWall, Bg = inp.Bg, Bt = inp.Bt;
        // Front face battered, back face vertical: a rectangle Bt wide plus a triangle at the front.
        var weights = new List<Weight>
        {
            new(Bt * Ht * gw, Bg - Bt / 2, Ht / 2),
            new((Bg - Bt) * Ht / 2 * gw, 2 * (Bg - Bt) / 3, Ht / 3),
        };
        var st = Stability(inp, weights, Bg, Ht, Bg, seismic);
        return new Analysis(inp.Type, Bg, Ht, Bg, Ht).With(st);
    }

    /// <summary>Plastic section modulus S (mm³) of the post sizes offered.</summary>
    public static readonly IReadOnlyDictionary<string, double> Posts = new Dictionary<string, double>
    {
        ["150UC23"] = 184e3, ["200UC46"] = 497e3, ["250UC73"] = 992e3, ["310UC97"] = 1440e3,
    };

    /// <summary>Sleeper wall: posts cantilever from bored piers. Broms (1964) short free-head pile in cohesionless soil.</summary>
    public static Analysis PostWall(WallInput inp, bool seismic = false)
    {
        double H = inp.H, s = inp.S, dia = inp.Dia, Dp = inp.Dp;
        double Ka = KaRankine(inp.Phi, inp.Slope), Kp = KpRankine(inp.PhiF), cb = Math.Cos(inp.Slope * D2R);
        double P = (0.5 * Ka * inp.Gamma * H * H + Ka * inp.Q * H) * cb * s;
        double M0 = (0.5 * Ka * inp.Gamma * H * H * H / 3 + Ka * inp.Q * H * H / 2) * cb * s;
        if (seismic && inp.Kh > 0) { P += 0.375 * inp.Kh * inp.Gamma * H * H * s; M0 += 0.375 * inp.Kh * inp.Gamma * H * H * s * 0.6 * H; }
        if (!inp.Drained && inp.Hw > 0) { double h = Math.Min(inp.Hw, H); P += 0.5 * GammaW * h * h * s; M0 += 0.5 * GammaW * Math.Pow(h, 3) / 3 * s; }
        double e = M0 / P;   // height of the push above ground in front
        double Cap(double L) => 0.5 * inp.GammaF * dia * Math.Pow(L, 3) * Kp / (e + L);
        double fsReq = seismic ? 1.5 : 2.0;
        double Lreq = 0.1; while (Cap(Lreq) < fsReq * P && Lreq < 20) Lreq += 0.01;
        double FoS = Cap(Dp) / P;
        double f = 0.82 * Math.Sqrt(P / (inp.GammaF * dia * Kp));
        double Mmax = P * (e + 0.67 * f);
        double Mstar = (seismic ? 1.0 : 1.5) * Mmax;
        double S = Posts.TryGetValue(inp.Post, out var sv) ? sv : 497e3, phiMs = 0.9 * 300 * S / 1e6;
        string sfx = seismic ? " (earthquake)" : "";
        var a = new Analysis(WallType.Post, dia, H + Dp, 0, H)
        {
            Ka = Ka, Kp = Kp, Push = P, PushHeight = e, Lreq = Lreq, EmbedFoS = FoS, Mmax = Mmax,
            Reo = seismic ? null : Reinforcement.ForPier(inp),
        };
        a.Checks.Add(Chk("embed" + sfx, "Post embedment" + sfx, FoS, fsReq, "FoS", fsReq / FoS,
            $"Push {F(P, 1)} kN per post at {F(e, 2)} m above ground. Broms capacity {F(Cap(Dp), 1)} kN with {F(Dp, 2)} m embedment. Needs at least {F(Lreq, 2)} m."));
        a.Checks.Add(Chk("postM" + sfx, "Post bending" + sfx, Mstar, phiMs, "kNm", Mstar / phiMs,
            $"M* = {F(Mstar, 1)} kNm (max moment {F(0.67 * f, 2)} m below ground). {inp.Post} φMs = {F(phiMs, 1)} kNm (fy 300 MPa)."));
        return a;
    }

    public static Analysis Analyse(WallInput inp, bool seismic = false) => inp.Type switch
    {
        WallType.Post => PostWall(inp, seismic),
        WallType.Gravity or WallType.Block => Gravity(inp, seismic),
        _ => Cantilever(inp, seismic),
    };

    /// <summary>All checks: the static case, plus the earthquake case when kh &gt; 0.</summary>
    public static WallResult RunAll(WallInput inp)
    {
        var r = Analyse(inp, false);
        var checks = new List<Check>(r.Checks);
        if (inp.Kh > 0) checks.AddRange(Analyse(inp, true).Checks);
        var worst = checks.Aggregate((a, c) => c.Util > a.Util ? c : a);
        return new WallResult(r, checks, worst);
    }
}

public sealed record Load(string Name, double Ph, double Pv, double Y, double X);
public sealed record Weight(double W, double X, double Y);
internal sealed record Stability(double Ka, List<Load> Loads, double SumH, double Mo, double V, double Mr, double E, double Qmax, double Qmin, List<Check> Checks);

/// <summary>One wall analysed (static or earthquake case).</summary>
public sealed class Analysis
{
    public Analysis(WallType kind, double b, double ht, double backX, double backH)
    {
        Kind = kind; B = b; Ht = ht; BackX = backX; BackH = backH;
    }
    public WallType Kind { get; }
    /// <summary>Base width (pier diameter for a post wall).</summary>
    public double B { get; }
    /// <summary>Total height, underside of footing to top of wall.</summary>
    public double Ht { get; }
    public double Hs { get; init; }
    /// <summary>The vertical plane the soil pushes on: x from the toe, height.</summary>
    public double BackX { get; }
    public double BackH { get; }
    public double Ka { get; set; }
    public double Kp { get; set; }
    public List<Load> Loads { get; private set; } = new();
    public double SumH { get; private set; }
    public double Mo { get; private set; }
    public double V { get; private set; }
    public double Mr { get; private set; }
    public double E { get; private set; }
    public double Qmax { get; private set; }
    public double Qmin { get; private set; }
    // Post walls
    public double Push { get; init; }
    public double PushHeight { get; init; }
    public double Lreq { get; init; }
    public double EmbedFoS { get; init; }
    public double Mmax { get; init; }

    public List<Check> Checks { get; } = new();
    public Reinforcement? Reo { get; set; }

    internal Analysis With(Stability st)
    {
        Ka = st.Ka; Loads = st.Loads; SumH = st.SumH; Mo = st.Mo; V = st.V; Mr = st.Mr; E = st.E; Qmax = st.Qmax; Qmin = st.Qmin;
        Checks.AddRange(st.Checks);
        return this;
    }
}

public sealed record WallResult(Analysis R, List<Check> Checks, Check Worst)
{
    public bool Pass => Checks.All(c => c.Pass);
}
