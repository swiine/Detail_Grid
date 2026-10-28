using static ETWalls.Core.Design.WallDesign;

namespace ETWalls.Core.Design;

/// <summary>A bar size and spacing for one face, per metre of wall.</summary>
public sealed record BarLayout(int Db, int S, double D, double Need, double Prov, bool Ok, string Label);

/// <summary>A line of the bar schedule. Areas in mm² per metre (per pier for post walls), kg per metre (per pier).</summary>
public sealed record ScheduleRow(string Mark, string Where, string Bars, double Req, double Prov, double Kg, string Note);

/// <summary>
/// Reinforcement, AS 3600-2018 style (preview method): the lightest bar size and spacing for every face,
/// stem cut-off height, starter laps, shrinkage bars, and the bending / shear / anchorage checks.
/// </summary>
public sealed class Reinforcement
{
    public static readonly IReadOnlyDictionary<int, double> BarArea = new Dictionary<int, double> { [12] = 110, [16] = 200, [20] = 310, [24] = 450 };
    public static readonly int[] Spacings = { 300, 250, 200, 150, 125, 100 };
    public const double Fsy = 500;

    public BarLayout? StemBack { get; private init; }
    public BarLayout? StemFront { get; private init; }
    public BarLayout? StemHoriz { get; private init; }
    public BarLayout? HeelTop { get; private init; }
    public BarLayout? ToeBottom { get; private init; }
    public BarLayout? BaseLong { get; private init; }
    /// <summary>Height above the base where every second stem bar stops (m), or null to run full height.</summary>
    public double? Cut { get; private init; }
    public double LapA { get; private init; }
    public double LsyA { get; private init; }
    public List<ScheduleRow> Schedule { get; private init; } = new();
    public double TotalKg => Schedule.Sum(r => r.Kg);
    public List<Check> Checks { get; private init; } = new();
    /// <summary>The main bars, e.g. "N12@200" (or the pier bars for a post wall).</summary>
    public string Bars { get; private init; } = "";

    static double RoundUp(double v, double step) => Math.Ceiling(v / step) * step;
    static double A2(double fc) => Math.Max(0.67, 0.85 - 0.0015 * fc);

    public static double PhiMu(double ast, double d, double fc, double b = 1000) =>
        0.85 * ast * Fsy * (d - ast * Fsy / (2 * A2(fc) * fc * b)) / 1e6;

    public static double AstRequired(double M, double d, double fc, double b = 1000)
    {
        if (M <= 0) return 0;
        double A = Fsy * Fsy / (2 * A2(fc) * fc * b), Bq = -Fsy * d, C = M * 1e6 / 0.85;
        double disc = Bq * Bq - 4 * A * C;
        return disc < 0 ? double.PositiveInfinity : (-Bq - Math.Sqrt(disc)) / (2 * A);
    }

    public static double AstMinBending(double Dmm, double d, double fc, double b = 1000) =>
        0.20 * Math.Pow(Dmm / d, 2) * (0.6 * Math.Sqrt(fc)) / Fsy * b * d;

    public static double PhiVuc(double ast, double d, double fc, double b = 1000)
    {
        double beta1 = Math.Max(1.1, 1.1 * (1.6 - d / 1000));
        return 0.7 * beta1 * b * d * Math.Min(4, Math.Cbrt(fc)) * Math.Cbrt(Math.Min(0.04, ast / (b * d))) / 1000;
    }

    /// <summary>Lsy.tb, AS 3600-2018 Cl 13.1.2.2 (straight bar in tension), rounded up to 50 mm.</summary>
    public static double DevLength(int db, double fc, double cover, double s, bool topBar = false)
    {
        double k1 = topBar ? 1.3 : 1.0, k2 = (132.0 - db) / 100;
        double cd = Math.Min(cover, s / 2), k3 = Math.Min(1.0, Math.Max(0.7, 1 - 0.15 * (cd - db) / db));
        double L = Math.Max(0.5 * k1 * k3 * Fsy * db / (k2 * Math.Sqrt(Math.Min(fc, 65))), 0.058 * Fsy * k1 * db);
        return RoundUp(L, 50);
    }

    /// <summary>Lightest bar layout carrying M (kNm/m) in a section Dmm thick. pref 0 = any size.</summary>
    public static BarLayout PickBars(double M, double Dmm, double cover, double fc, int pref, double minArea = 0)
    {
        int[] sizes = pref == 0 ? new[] { 12, 16, 20, 24 } : new[] { pref };
        BarLayout? best = null; double bestScore = 0;
        foreach (var db in sizes)
        {
            double d = Dmm - cover - db / 2.0;
            double need = Math.Max(Math.Max(AstRequired(M, d, fc), M > 0 ? AstMinBending(Dmm, d, fc) : 0), minArea);
            double sMax = Math.Min(300, 2 * Dmm);   // crack control spacing
            int s = Spacings.Where(x => x <= sMax).FirstOrDefault(x => BarArea[db] * 1000 / x >= need);
            if (s == 0) continue;
            double prov = BarArea[db] * 1000 / s;
            double score = prov + (s < 150 ? 400 : 0);   // tight spacing is hard to fix and pour
            if (best is null || score < bestScore) { best = new(db, s, d, need, prov, true, $"N{db}@{s}"); bestScore = score; }
        }
        if (best is null)
        {
            int db = sizes[^1]; double d = Dmm - cover - db / 2.0;
            return new(db, 100, d, AstRequired(M, d, fc), BarArea[db] * 10, false, $"N{db}@100 not enough");
        }
        return best;
    }

    static double SteelKg(double areaPerM, double lengthM) => areaPerM * lengthM * 0.00785;

    /// <summary>Bearing pressure under the base at x from the toe (kPa), from the service resultant.</summary>
    public static double BearingAt(double x, double V, double e, double B)
    {
        if (V <= 0) return 0;
        if (Math.Abs(e) <= B / 6) return V / B * (1 + 12 * e * (B / 2 - x) / (B * B));
        double L = 3 * (B / 2 - Math.Abs(e)), xx = e >= 0 ? x : B - x;
        return xx < L ? 2 * V / L * (1 - xx / L) : 0;
    }

    /// <summary>Moment and shear (at d from the face) of a cantilever strip loaded by net(u), u = distance from the face.</summary>
    static (double M, double V) Strip(Func<double, double> net, double L, double d)
    {
        const int n = 200; double du = L / n, M = 0, V = 0;
        for (int i = 0; i < n; i++) { double u = (i + 0.5) * du, w = net(u); M += w * u * du; if (u >= d) V += w * du; }
        return (M, V);
    }

    internal static Reinforcement ForCantilever(WallInput inp, double hs, double B, Stability st)
    {
        double toe = inp.Toe, heel = inp.HeelUsed, t1 = inp.T1, t2 = inp.T2, tb = inp.Tb;
        double fc = inp.Fc, cover = inp.Cover; int pref = inp.Bar;
        double Ka = KaRankine(inp.Phi, inp.Slope), cb = Math.Cos(inp.Slope * D2R);
        double hwStem = !inp.Drained ? Math.Max(0, inp.Hw - tb) : 0;
        // Factored stem actions at depth y below the top of the stem (1.5 earth / water, 1.5 surcharge).
        double Mst(double y) => 1.5 * Ka * cb * (inp.Gamma * Math.Pow(y, 3) / 6 + inp.Q * y * y / 2) + 1.5 * GammaW * Math.Pow(Math.Max(0, y - (hs - hwStem)), 3) / 6;
        double Vst(double y) => 1.5 * Ka * cb * (inp.Gamma * y * y / 2 + inp.Q * y) + 1.5 * GammaW * Math.Pow(Math.Max(0, y - (hs - hwStem)), 2) / 2;
        double stemD = t2 * 1000;
        double tD(double y) => (t1 + (t2 - t1) * y / hs) * 1000;

        // A: stem back face, vertical main bars
        double Mstem = Mst(hs);
        var A = PickBars(Mstem, stemD, cover, fc, pref);
        double phiMuA = PhiMu(A.Prov, A.D, fc);
        double dV = A.D / 1000, VstemD = Vst(Math.Max(0, hs - dV));
        double phiVA = PhiVuc(A.Prov, A.D, fc);
        double LsyA = DevLength(A.Db, fc, cover, A.S), lapA = RoundUp(1.25 * LsyA, 50);
        // Cut every second bar where half the steel is enough (only when the spacing stays <= 300 mm).
        double? cut = null;
        if (A.Ok && A.S * 2 <= 300 && hs > 1.2)
        {
            double half = A.Prov / 2, lo = 0, hi = hs;
            for (int i = 0; i < 60; i++)
            {
                double mid = (lo + hi) / 2, dmid = tD(mid) - cover - A.Db / 2.0;
                if (Mst(mid) <= PhiMu(half, dmid, fc)) lo = mid; else hi = mid;
            }
            double z = hs - lo + (tD(lo) + LsyA) / 1000;   // theoretical point + D + Lsy.t (Cl 8.1.10)
            if (z < hs - 0.3) cut = RoundUp(z * 1000, 50) / 1000;
        }

        // E: heel, top bars (soil and surcharge push down, bearing pushes up)
        double wDown = 1.2 * GammaC * tb + 1.2 * inp.Gamma * hs + 1.5 * inp.Q;
        double xHeel0 = toe + t2;
        double Dbase = tb * 1000, dBase = Dbase - cover - 16 / 2.0;
        var heelA = Strip(u => wDown - BearingAt(xHeel0 + u, st.V, st.E, B), heel, dBase / 1000);
        // F: toe, bottom bars (bearing pushes up, base and soil on the toe hold down)
        double wToe = 0.9 * (GammaC * tb + inp.Gamma * Math.Max(0, inp.D - tb));
        var toeA = Strip(u => 1.5 * BearingAt(toe - u, st.V, st.E, B) - wToe, toe, dBase / 1000);
        double shrinkFace = 0.00125 * 1000 * Dbase;   // 0.0025 bD split over two faces
        var E = PickBars(Math.Max(0, heelA.M), Dbase, cover, fc, pref, shrinkFace);
        var Fb = PickBars(Math.Max(0, toeA.M), Dbase, cover, fc, pref, shrinkFace);
        double anchorRoom = tb * 1000 - cover - Fb.Db;   // top of base down to the bottom bars
        double anchorNeed = RoundUp(0.5 * LsyA, 10);
        double phiMuE = PhiMu(E.Prov, E.D, fc), phiMuF = PhiMu(Fb.Prov, Fb.D, fc);
        double phiVE = PhiVuc(E.Prov, E.D, fc), phiVF = PhiVuc(Fb.Prov, Fb.D, fc);
        double LsyE = DevLength(E.Db, fc, cover, E.S, Dbase - cover > 300);

        // B: stem front face vertical, C: stem horizontal each face, G: base longitudinal each face (shrinkage)
        double tAvg = (t1 + t2) / 2 * 1000;
        var Bf = PickBars(0, stemD, cover, fc, 0, 0.00075 * 1000 * stemD);
        var C = PickBars(0, tAvg, cover, fc, 0, 0.00125 * 1000 * tAvg);
        var Gl = PickBars(0, Dbase, cover, fc, 0, shrinkFace);

        double lenA = hs + lapA / 1000, lenB = hs + 0.3, lenEF = B - 2 * cover / 1000;
        var schedule = new List<ScheduleRow>
        {
            new("A", "Stem, back face (soil side), vertical", A.Label, A.Need, A.Prov,
                SteelKg(A.Prov, cut is double c ? (c + hs + lapA / 1000) / 2 + lapA / 1000 : lenA),
                $"Main bars. {(cut is double cc ? $"Every 2nd bar stops {F(cc, 2)} m above the base." : "Run full height.")} Lap {G(lapA)} mm with starters."),
            new("S", "Starters from base into stem", A.Label, A.Need, A.Prov, SteelKg(A.Prov, lapA / 1000 + tb + 0.3),
                $"Cogged under the bottom bars, {G(lapA)} mm above the base.{(anchorNeed > anchorRoom ? " Base too thin for the cog: thicken it." : "")}"),
            new("B", "Stem, front face, vertical", Bf.Label, Bf.Need, Bf.Prov, SteelKg(Bf.Prov, lenB), "Minimum steel (0.15% split over 2 faces)."),
            new("C", "Stem, horizontal, each face", C.Label, C.Need, C.Prov, SteelKg(C.Prov * 2, hs), "Shrinkage and temperature (0.25% bD over 2 faces)."),
            new("E", "Base, top (heel), across the wall", E.Label, E.Need, E.Prov, SteelKg(E.Prov, lenEF),
                heelA.M > 0 ? $"Heel M* = {F(heelA.M, 1)} kNm/m. Anchor {G(LsyE)} mm past the stem." : "Heel barely bends: minimum steel."),
            new("F", "Base, bottom (toe), across the wall", Fb.Label, Fb.Need, Fb.Prov, SteelKg(Fb.Prov, lenEF),
                toeA.M > 0 ? $"Toe M* = {F(toeA.M, 1)} kNm/m. Runs the full base width." : "Toe barely bends: minimum steel."),
            new("G", "Base, along the wall, top and bottom", Gl.Label, Gl.Need, Gl.Prov, SteelKg(Gl.Prov * 2, B), "Shrinkage and temperature."),
        };

        var checks = new List<Check>
        {
            Chk("stemM", "Stem bending", Mstem, phiMuA, "kNm/m", Mstem / phiMuA,
                $"M* = {F(Mstem, 1)} kNm/m at the base of the stem. Needs {Math.Ceiling(A.Need)} mm²/m, {A.Label} gives {Math.Round(A.Prov)} mm²/m (d = {F(A.D, 0)} mm, φMu = {F(phiMuA, 1)})."),
            Chk("stemV", "Stem shear", VstemD, phiVA, "kN/m", VstemD / phiVA, $"V* = {F(VstemD, 1)} kN/m at d from the base vs φVuc = {F(phiVA, 1)} kN/m (no ligatures)."),
            Chk("heelM", "Heel bending", Math.Max(0, heelA.M), phiMuE, "kNm/m", Math.Max(0, heelA.M) / phiMuE,
                $"Down {F(wDown, 0)} kPa (1.2 G + 1.5 Q) less the bearing under the heel. {E.Label} top, φMu = {F(phiMuE, 1)}."),
            Chk("heelV", "Heel shear", Math.Max(0, heelA.V), phiVE, "kN/m", Math.Max(0, heelA.V) / phiVE, $"V* at d from the stem vs φVuc = {F(phiVE, 1)} kN/m."),
            Chk("toeM", "Toe bending", Math.Max(0, toeA.M), phiMuF, "kNm/m", Math.Max(0, toeA.M) / phiMuF,
                $"1.5 × bearing up less 0.9 × base and soil on the toe. {Fb.Label} bottom, φMu = {F(phiMuF, 1)}."),
            Chk("toeV", "Toe shear", Math.Max(0, toeA.V), phiVF, "kN/m", Math.Max(0, toeA.V) / phiVF, $"V* at d from the stem vs φVuc = {F(phiVF, 1)} kN/m."),
            Chk("anchor", "Starter anchorage in base", anchorNeed, anchorRoom, "mm", anchorNeed / anchorRoom,
                $"Cogged starter needs 0.5 Lsy.tb = {G(anchorNeed)} mm in the base; room {F(anchorRoom, 0)} mm (base less bottom cover and bars)."),
        };
        if (!A.Ok || !E.Ok || !Fb.Ok) checks.Add(Chk("bars", "Bars fit", 2, 1, "", 2, "No bar size and spacing works. Make the section thicker."));
        return new Reinforcement { StemBack = A, StemFront = Bf, StemHoriz = C, HeelTop = E, ToeBottom = Fb, BaseLong = Gl, Cut = cut, LapA = lapA, LsyA = LsyA, Schedule = schedule, Checks = checks, Bars = A.Label };
    }

    /// <summary>Bored pier for a post wall: minimum longitudinal steel (0.5% of the pier) and a helix.</summary>
    internal static Reinforcement ForPier(WallInput inp)
    {
        double area = Math.PI * Math.Pow(inp.Dia * 1000, 2) / 4, need = 0.005 * area;
        int n = Math.Max(6, (int)Math.Ceiling(need / BarArea[16]));
        double prov = n * BarArea[16];
        var schedule = new List<ScheduleRow>
        {
            new("P", "Pier, vertical", $"{n}-N16", need, prov, prov * (inp.Dp + 0.1) * 0.00785, $"0.5% of the {Math.Round(inp.Dia * 1000)} mm pier, 75 mm cover. Full depth."),
            new("H", "Pier, helix", "R10 helix @ 200", 0, 0, 0.617 * Math.PI * (inp.Dia - 0.17) * inp.Dp / 0.2, "Pitch 200, closer (100) in the top 600 mm."),
        };
        return new Reinforcement { Schedule = schedule, Bars = $"{n}-N16" };
    }
}
