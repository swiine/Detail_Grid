namespace PavementBuildup.Core.Design;

/// <summary>
/// Concrete industrial floors and pavements, ported formula for formula from the company workbook
/// "T48 and T34 Industrial Pavement Design" (Cement &amp; Concrete Services, v1.4, 2007): CCAA T48-1999 chart
/// fits for wheels, posts and UDL, AS3600 punching shear, and the Westergaard / Ioannides / Pickett checks
/// of T34-1985. Working-strength (unfactored) loads unless stated.
/// </summary>
public static class ConcreteDesign
{
    public const string T48 = "CCAA T48-1999";
    public const string T34 = "CCAA T34-1985";

    /// <summary>Concrete grades the T48 charts cover.</summary>
    public static readonly double[] Grades = { 25, 32, 40, 50 };

    // ------------------------------------------------------------------ soil ------------------

    /// <summary>Modulus of subgrade reaction k (kPa/mm) from CBR (sheet 4).</summary>
    public static double KFromCbr(double cbr) => cbr < 20 ? 3.99 + 21.3 * Math.Log(cbr) : 23.7 + 2.18 * cbr;

    /// <summary>Long-term soil modulus Es (MPa) from CBR, T48 Fig 1.15 (sheets 4-6).</summary>
    public static double EsFromCbr(double cbr) =>
        cbr <= 0 ? 0 : cbr < 30 ? 11.57 * Math.Log(cbr) + 1.642 : 57.401 * Math.Log(cbr) - 151.38;

    /// <summary>Es (MPa) from k: 0.54 (k − 3.99), CBR &lt; 20.</summary>
    public static double EsFromK(double k) => 0.54 * (k - 3.99);

    /// <summary>k from Es: 1.84 Es + 3.99, CBR &lt; 20.</summary>
    public static double KFromEs(double es) => 1.84 * es + 3.99;

    /// <summary>CBR from Es: e^((Es − 1.64) / 11.57), CBR &lt; 20.</summary>
    public static double CbrFromEs(double es) => Math.Exp((es - 1.64) / 11.57);

    public static CalcResult Soil(double cbr)
    {
        var r = new CalcResult { Title = "Soil: CBR vs k vs Es", Reference = "Sheet 4; " + T48 + " Fig 1.15" };
        r.In("CBR", cbr, "%");
        if (!(cbr > 0)) throw new ArgumentException("Enter a CBR greater than 0%.");
        double k = r.Result("k", "Modulus of subgrade reaction k (760 mm plate)", KFromCbr(cbr), "kPa/mm", "0.0");
        double es = r.Result("es", "Soil modulus Es (long term)", EsFromCbr(cbr), "MPa", "0.0");
        r.Step("esk", "Es from k  = 0.54 (k − 3.99)", EsFromK(k), "MPa", "0.0");
        r.Headline = $"k = {CalcResult.Num(k, "0.0")} kPa/mm, Es = {CalcResult.Num(es, "0.0")} MPa";
        r.Notes.Add("k depends on the plate size: if in doubt use the 760 mm plate value (the smaller k).");
        return r;
    }

    // ------------------------------------------------------------------ common ----------------

    /// <summary>Flexural strength 0.7 √f'c (MPa).</summary>
    public static double Fcf(double fc) => 0.7 * Math.Sqrt(fc);

    /// <summary>Load repetitions factor k2: 0.73 − 0.0846 (log N − 3), 0.5 for N ≥ 400,000.</summary>
    public static double K2(double n) => n <= 399999 ? 0.73 - 0.0846 * (Math.Log10(n) - 3) : 0.5;

    /// <summary>Concrete calibration factor k4 by grade.</summary>
    public static double K4(double fc) => fc switch
    {
        20 => 1.03, 25 => 1.07, 32 => 1.12, 40 => 1.16, 50 => 1.2,
        _ => throw new ArgumentException("f'c must be 25, 32, 40 or 50 MPa (the T48 charts)."),
    };

    /// <summary>Repetitions from a reckoner: passes per day x 5 days x 52 weeks x years.</summary>
    public static double Repetitions(double perDay, double years) => perDay * 5 * 52 * years;

    private static double RoundUp5(double mm) => Math.Ceiling(mm / 5 - 1e-9) * 5;

    private static void Thickness(CalcResult r, double t, double min, string what)
    {
        double adopt = Math.Max(min, RoundUp5(t));
        r.ThicknessMm = adopt;
        r.Values["adopt"] = adopt;
        r.Lines.Add(new CalcLine("Adopt (rounded up to 5 mm, min " + CalcResult.Num(min) + ")", CalcResult.Num(adopt, "0"), "mm", LineKind.Ok));
        r.Headline = $"{what}: {CalcResult.Num(t, "0.0")} mm required, adopt {CalcResult.Num(adopt, "0")} mm";
        r.Pass = true;
    }

    /// <summary>Linear interpolation through (x, y) points; null outside the range.</summary>
    private static double? Interpolate(IReadOnlyList<(double X, double Y)> pts, double x)
    {
        for (int i = 0; i < pts.Count - 1; i++)
            if (x >= pts[i].X - 1e-9 && x <= pts[i + 1].X + 1e-9)
                return pts[i].Y + (x - pts[i].X) / (pts[i + 1].X - pts[i].X) * (pts[i + 1].Y - pts[i].Y);
        return null;
    }

    // ------------------------------------------------------------------ wheels (T48 charts 1.1 / 1.2) -----

    /// <summary>Wheel loads, interior (sheet 6) or edge (sheet 7): thickness for an axle load from T48 Chart 1.1 / 1.2.</summary>
    public static CalcResult Wheel(bool edge, double fc, double k1, double passesPerDay, double years, double k3,
        double wheelSpacing, double soilDepth, double es, double axleLoadKn)
    {
        var r = new CalcResult
        {
            Title = edge ? "Wheel loading - edge" : "Wheel loading - interior",
            Reference = T48 + (edge ? " Chart 1.2" : " Chart 1.1"),
        };
        r.In("f'c concrete", fc, "MPa");
        r.In("k1 material factor", k1);
        r.In("Passes per day", passesPerDay);
        r.In("Years", years);
        r.In("k3 geotechnical calibration", k3);
        r.In("S wheel spacing", wheelSpacing, "m");
        r.In("H soil depth", soilDepth, "m");
        r.In("E soil modulus", es, "MPa");
        r.In("P axle load", axleLoadKn, "kN");

        double n = r.Step("n", "N repetitions = passes x 5 x 52 x years", Repetitions(passesPerDay, years), "", "0");
        double k2 = r.Step("k2", "k2 repetitions factor", K2(n));
        double k4 = r.Step("k4", "k4 concrete calibration", K4(fc));
        double fcf = r.Step("fcf", "f'cf = 0.7 √f'c", Fcf(fc), "MPa");
        double fall = r.Step("fall", "f all = k1 k2 f'cf", k1 * k2 * fcf, "MPa");
        double s = wheelSpacing, h = soilDepth, e = es;
        double fs = r.Step("fs", edge ? "FS2 (wheel spacing)" : "FS1 (wheel spacing)", edge
            ? 0.013 * s * s * s - 0.1186 * s * s + 0.4131 * s + 0.6078
            : 0.0095 * s * s * s - 0.1001 * s * s + 0.4102 * s + 0.5806);
        double fh = r.Step("fh", edge ? "FH2 (soil depth)" : "FH1 (soil depth)", edge
            ? (h < 7 ? -0.0023 * Math.Pow(h, 3) + 0.0353 * h * h - 0.1796 * h + 1.295 : 0.99389 - 0.000556 * h)
            : (h < 7 ? 0.0000007 * Math.Pow(h, 6) - 0.00004 * Math.Pow(h, 5) + 0.0009 * Math.Pow(h, 4) - 0.0108 * Math.Pow(h, 3) + 0.0712 * h * h - 0.2495 * h + 1.3675 : 0.9972 - 0.001389 * h));
        double fe = r.Step("fe", edge ? "FE2 (soil modulus)" : "FE1 (soil modulus)", edge
            ? (e <= 50 ? 0.5284 * Math.Pow(e, 0.2696) : e < 75 ? -0.00005 * e * e + 0.0095 * e + 1.1507 : 1.71 + Math.Sin((e - 105) * 2 * 3.142 / 180) * (1.88 - 1.71))
            : (e <= 50 ? 0.6277 * Math.Pow(e, 0.203) : e < 75 ? -0.00006 * e * e + 0.0103 * e + 1.03 : 1.57 + Math.Sin((e - 105) * 2 * 3.142 / 180) * (1.675 - 1.57)));
        double f = r.Step("f", edge ? "F2 = f all k3 k4 FS2 FH2 FE2" : "F1 = f all k3 k4 FS1 FH1 FE1", fall * k3 * k4 * fs * fh * fe);
        if (f > 5)
        {
            r.Warn($"Stress factor {CalcResult.Num(f, "0.00")} is above the chart range (0-5).");
            r.Pass = false;
            r.Headline = "Outside the T48 chart (stress factor > 5)";
            return r;
        }

        // Thickness curves at 100, 200, 400 and 600 kN; straight lines between (and down to 50 kN).
        double F(double c6, double c5, double c4, double c3, double c2, double c1, double c0) =>
            c6 * Math.Pow(f, 6) + c5 * Math.Pow(f, 5) + c4 * Math.Pow(f, 4) + c3 * Math.Pow(f, 3) + c2 * f * f + c1 * f + c0;
        double t100, t200, t400, t600;
        if (edge)
        {
            t100 = F(0.0124, -1.0545, 17.187, -122.63, 456.11, -932.52, 1129.1);
            t200 = F(-0.7542, 15.62, -130.09, 546.74, -1167.5, 989.75, 451.4);
            t400 = F(0, 0, -3.0634, 51.288, -304.17, 670.99, 158.01);
            t600 = -75.909 * f + 882.05;
        }
        else
        {
            t100 = F(0.8791, -16.187, 119.59, -456.65, 975.1, -1194.3, 920.95);
            t200 = F(-0.1126, 1.9127, -10.568, 7.4724, 138.42, -554.82, 954.72);
            t400 = F(0.0749, -0.4369, -5.5592, 60.51, -187.77, 68.883, 735.13);
            t600 = F(-0.7298, 16.878, -158.56, 769.75, -2006.4, 2504.1, -472.39);
        }
        double t50 = t100 - 50 * ((t200 - t100) / 100);
        var curve = new List<(double, double)> { (50, t50), (100, t100), (200, t200), (400, t400), (600, t600) };
        foreach (var load in new double[] { 50, 75, 100, 150, 200, 300, 400, 500, 600 })
            r.Step("t" + load, $"  t for {load} kN axle", Interpolate(curve, load)!.Value, "mm", "0.0");

        if (Interpolate(curve, axleLoadKn) is not { } t)
        {
            r.Warn("Axle load outside the chart range (50-600 kN). Read the table above.");
            r.Pass = false;
            r.Headline = "Axle load outside 50-600 kN";
            return r;
        }
        r.Result("t", $"Thickness for P = {CalcResult.Num(axleLoadKn)} kN", t, "mm");
        Thickness(r, t, 130, edge ? "Edge" : "Interior");
        r.Notes.Add("Minimum thickness 130 mm; allow for (−) construction tolerances (T48 Table 1.14).");
        r.Notes.Add("10 kN = 1 tonne. Repetitions assume 5 working days a week; over 400,000 is unlimited.");
        return r;
    }

    // ------------------------------------------------------------------ posts (T48 chart 1.3) ---------

    /// <summary>Post (rack) loads, interior (sheet 8) or edge (sheet 10).</summary>
    public static CalcResult Post(bool edge, double fc, double k1, double k2, double spacingX, double spacingY,
        double soilDepth, double es, double postLoadKn)
    {
        var r = new CalcResult { Title = edge ? "Post loading - edge" : "Post loading - interior", Reference = T48 + " Chart 1.3" };
        r.In("f'c concrete", fc, "MPa");
        r.In("k1 material factor", k1);
        r.In("k2 repetitions factor", k2);
        r.In("x post spacing", spacingX, "m");
        r.In("y post spacing", spacingY, "m");
        r.In("H soil depth", soilDepth, "m");
        r.In("E soil modulus", es, "MPa");
        r.In("P post load (unfactored)", postLoadKn, "kN");

        double s = r.Step("s", "S average spacing", (spacingX + spacingY) / 2, "m");
        double fcf = r.Step("fcf", "f'cf = 0.7 √f'c", Fcf(fc), "MPa");
        double fall = r.Step("fall", "f all = k1 k2 f'cf", k1 * k2 * fcf, "MPa");
        double fs = r.Step("fs", "FS3 (spacing)", -0.058 * Math.Pow(s, 5) + 0.5755 * Math.Pow(s, 4) - 2.213 * Math.Pow(s, 3) + 3.9613 * s * s - 2.7045 * s + 1.1595);
        double h = soilDepth, e = es;
        double fh = r.Step("fh", "FH3 (soil depth)", h < 7 ? -0.0059 * h * h * h + 0.0898 * h * h - 0.4489 * h + 1.7518 : 1.0044 - 0.00278 * h);
        double fe = r.Step("fe", "FE3 (soil modulus)", edge
            ? (e <= 50 ? 0.446 * Math.Pow(e, 0.2876) : 0.00267 * e + 1.243)
            : (e < 55 ? 0.408 * Math.Pow(e, 0.33) : 0.004 * e + 1.285));
        double f = r.Step("f", "F3 = 1000 (f all / P) FS3 FH3 FE3", 1000 * (fall / postLoadKn) * fs * fh * fe);
        if (f > 130)
        {
            r.Warn("Stress factor F3 is above the chart range (10-130).");
            r.Pass = false;
            r.Headline = "Outside the T48 chart (F3 > 130)";
            return r;
        }
        double t = r.Result("t", "Thickness", edge
            ? (f <= 50 ? 0.0002 * f * f * f + 0.011 * f * f - 9.9689 * f + 856.23 : 0.0211 * f * f - 6.957 * f + 703.61)
            : (f <= 50 ? -0.0023 * f * f * f + 0.4227 * f * f - 29.445 * f + 933.74 : 7409.9 * Math.Pow(f, -0.8851)), "mm");
        if (f < 10) r.Warn("F3 is below the chart range (10-130): check the result.");
        Thickness(r, t, 130, edge ? "Post edge" : "Post interior");
        if (!edge)
            r.Notes.Add("T48 uses k2 = 1 for posts; use the wheel k2 (e.g. 0.5 for N > 400,000) if racks are loaded and unloaded often.");
        r.Notes.Add("Extreme rack layouts may not be the worst case: use an industrial-floor FEM program for those.");
        return r;
    }

    // ------------------------------------------------------------------ punching shear (AS3600) -------

    /// <summary>Punching shear at an interior post (sheet 9, AS3600-2001 9.2.3).</summary>
    public static CalcResult PunchingShear(double fc, double basePlateMm, double slabMm, double factoredLoadKn)
    {
        var r = new CalcResult { Title = "Punching shear - interior post", Reference = "AS3600-2001 cl. 9.2.3" };
        r.In("f'c concrete", fc, "MPa");
        r.In("b base plate width", basePlateMm, "mm");
        r.In("t pavement thickness", slabMm, "mm");
        r.In("P* factored load (e.g. 1.2G + 1.5Q)", factoredLoadKn, "kN");
        double fcv = r.Step("fcv", "fcv = 0.34 √f'c", 0.34 * Math.Sqrt(fc), "MPa");
        double u = r.Step("u", "u = 4 (b + d), d = 0.9 t", 4 * (basePlateMm + 0.9 * slabMm), "mm", "0");
        double cap = r.Result("phiVuo", "φVuo = 0.7 u d fcv", 0.7 * (u * 0.9 * slabMm * fcv) / 1000, "kN", "0.0");
        bool ok = factoredLoadKn < cap;
        r.Check("φVuo ≥ P*", ok, $"{CalcResult.Num(cap, "0.0")} vs {CalcResult.Num(factoredLoadKn, "0.0")} kN");
        r.Headline = ok ? $"OK for punching shear (φVuo {CalcResult.Num(cap, "0")} kN > P* {CalcResult.Num(factoredLoadKn, "0")} kN)"
                        : $"FAILS in punching shear (φVuo {CalcResult.Num(cap, "0")} kN ≤ P* {CalcResult.Num(factoredLoadKn, "0")} kN)";
        r.Notes.Add("Assumes the full perimeter: a post closer than d to an edge or corner must be checked by hand.");
        return r;
    }

    // ------------------------------------------------------------------ UDL (T48 chart 1.4) -----------

    /// <summary>Thickness under a uniformly distributed load (sheet 11).</summary>
    public static CalcResult Udl(double fc, double k1, double repetitions, double aisleWidth, double soilDepth, double es, double udlKpa)
    {
        var r = new CalcResult { Title = "Uniformly distributed load (UDL) - thickness", Reference = T48 + " Chart 1.4" };
        r.In("f'c concrete", fc, "MPa");
        r.In("k1 material factor", k1);
        r.In("N repetitions (0 = no calc)", repetitions, "", "0");
        r.In("W stack or aisle width", aisleWidth, "m");
        r.In("H soil depth", soilDepth, "m");
        r.In("E soil modulus", es, "MPa");
        r.In("p UDL", udlKpa, "kPa");
        double n = repetitions;
        double k2 = r.Step("k2", "k2 repetitions factor", n <= 0 ? 0.75 : n <= 399999 ? 0.73 - 0.0846 * (Math.Log10(n) - 3) : 0.5);
        double fcf = r.Step("fcf", "f'cf = 0.7 √f'c", Fcf(fc), "MPa");
        double fall = r.Step("fall", "f all = k1 k2 f'cf", k1 * k2 * fcf, "MPa");
        double w = aisleWidth, h = soilDepth, e = es;
        double fs = r.Step("fs", "FS4 (width)", w < 3
            ? 0.0039 * Math.Pow(w, 6) - 0.0776 * Math.Pow(w, 5) + 0.6244 * Math.Pow(w, 4) - 2.6195 * Math.Pow(w, 3) + 6.0877 * w * w - 7.6425 * w + 5.2165
            : 0.0147 * Math.Pow(w, 4) - 0.2364 * Math.Pow(w, 3) + 1.4536 * w * w - 4.0648 * w + 5.2443);
        double fh = r.Step("fh", "FH4 (soil depth)", h < 8 ? 0.00191 * Math.Pow(h, 4) - 0.0422 * Math.Pow(h, 3) + 0.347 * h * h - 1.2849 * h + 2.8298 : -0.005 * h + 0.98);
        double fe = r.Step("fe", "FE4 (soil modulus)", e < 50 ? 0.2421 * Math.Pow(e, 0.5321) : 0.0235 * e + 0.8235);
        double f = r.Step("f", "F4 = 1000 (f all / p) FS4 FH4 FE4", 1000 * (fall / udlKpa) * fs * fh * fe);
        if (f > 100)
        {
            r.Warn("F4 is above the chart range (30-100): the thickness is under 125 mm.");
            r.Pass = false;
            r.Headline = "Outside the T48 chart (F4 > 100)";
            return r;
        }
        double raw = f < 83 ? -417.76 * Math.Log(f) + 2115 : -0.2418 * f * f + 36.3 * f - 1087.1;
        double t = r.Result("t", "Thickness", Math.Max(120, raw), "mm");
        if (f < 30) r.Warn("F4 is below the chart range (30-100): check the result.");
        Thickness(r, t, 125, "UDL");
        return r;
    }

    /// <summary>Ec (MPa) for deflection, AS3600 draft 2005 values used in sheet 12.</summary>
    public static double EcForDeflection(double fc) => fc switch
    {
        <= 25 => 26700, <= 32 => 30100, <= 40 => 32800, <= 50 => 34800, _ => 37400,
    };

    /// <summary>Deflection under a UDL (sheet 12, T48 eqn 9).</summary>
    public static CalcResult UdlDeflection(double ec, double slabMm, double udlKpa, double width, double vSoil, double cbr, double soilDepth)
    {
        var r = new CalcResult { Title = "Uniformly distributed load (UDL) - deflection", Reference = T48 + " eqn 9 (3.4.14)" };
        r.In("Ec concrete modulus", ec, "MPa", "0");
        r.In("t pavement thickness", slabMm, "mm");
        r.In("p UDL", udlKpa, "kPa");
        r.In("W loaded width", width, "m");
        r.In("vs soil Poisson's ratio", vSoil);
        r.In("CBR", cbr, "%");
        r.In("H soil depth", soilDepth, "m");
        double es = r.Step("es", "Es long term (Fig 1.15)", cbr <= 0 ? 0 : cbr <= 20 ? 10.781 * Math.Log(cbr) + 2.7034 : 58.27 * Math.Log(cbr) - 155.02, "MPa");
        double lc = r.Step("lc", "Lc = t [Ec / (Es (1 − vs²))]^0.33", slabMm * Math.Pow(ec / (es * (1 - vSoil * vSoil)), 0.33) / 1000, "m");
        double S(double ws) => udlKpa * width * (1 - vSoil * vSoil) * ws / es;
        double w25 = lc <= 10 ? -0.0004 * lc * lc * lc + 0.0147 * lc * lc - 0.166 * lc + 0.8685 : 0.23;
        double w5 = lc < 8 ? 0.000008 * Math.Pow(lc, 5) - 0.0003 * Math.Pow(lc, 4) + 0.0039 * lc * lc * lc - 0.0103 * lc * lc - 0.1499 * lc + 1.2588 : 0.0025 * lc * lc - 0.0628 * lc + 0.7656;
        double w10 = lc < 8 ? -0.0006 * lc * lc * lc + 0.0209 * lc * lc - 0.2653 * lc + 1.6968 : 0.0018 * lc * lc - 0.0504 * lc + 0.9064;
        double w20 = lc < 8 ? -0.0005 * lc * lc * lc + 0.0199 * lc * lc - 0.261 * lc + 1.8684 : 0.003 * lc * lc - 0.0801 * lc + 1.2393;
        var curve = new List<(double, double)> { (2.5, S(w25)), (5, S(w5)), (10, S(w10)), (20, S(w20)) };
        foreach (var depth in new[] { 2.5, 3.75, 5, 7.5, 10, 15, 20 })
            r.Step("s" + CalcResult.Num(depth), $"  s for H = {CalcResult.Num(depth)} m", Interpolate(curve, depth)!.Value, "mm", "0.00");
        if (Interpolate(curve, soilDepth) is not { } s)
        {
            r.Warn("Soil depth outside 2.5-20 m. Read the table above.");
            r.Headline = "Soil depth outside 2.5-20 m";
            return r;
        }
        r.Result("s", $"Deflection s = p W (1 − vs²) ws / Es at H = {CalcResult.Num(soilDepth)} m", s, "mm", "0.00");
        r.Headline = $"Deflection {CalcResult.Num(s, "0.0")} mm";
        return r;
    }

    // ------------------------------------------------------------------ T34 closed-form checks -------

    public enum WheelPosition { Interior, NearEdge, OnEdge, Corner }

    /// <summary>
    /// Wheel stress check at a chosen slab thickness: Westergaard interior (sheet 13), Ioannides near the edge
    /// (14) or on the edge (15), Pickett corner (16). Also finds the least thickness (5 mm steps) that passes.
    /// </summary>
    public static CalcResult WheelStress(WheelPosition where, double axleTonnes, double cbr, double fc, double ninetyDayFactor,
        double poisson, double slabMm, double tyreKpa, double repetitions)
    {
        var (title, reference, estimateFactor) = where switch
        {
            WheelPosition.Interior => ("Wheel stress - interior (Westergaard)", T34 + ", Westergaard", 1.1),
            WheelPosition.NearEdge => ("Wheel stress - close to edge (Ioannides)", "TRB 1043-1985, Ioannides", 1.66),
            WheelPosition.OnEdge => ("Wheel stress - on the edge (Ioannides)", T34 + ", Ioannides", 1.8),
            _ => ("Wheel stress - on the corner (Pickett)", "PCA 1951, Pickett", 1.6),
        };
        var r = new CalcResult { Title = title, Reference = reference };
        r.In("P single axle", axleTonnes, "tonne");
        r.In("CBR", cbr, "%");
        r.In("f'c concrete", fc, "MPa");
        r.In("90-day flexural factor (1 or 1.1)", ninetyDayFactor);
        r.In("v Poisson's ratio (concrete)", poisson);
        r.In("h slab thickness chosen", slabMm, "mm");
        r.In("p tyre pressure", tyreKpa, "kPa");
        r.In("N repetitions", repetitions, "", "0");

        double pw = r.Step("pw", "Pw = P x 9.81 / 2", 0.5 * axleTonnes * 9.81, "kN");
        double k = r.Step("k", "k = 3.99 + 21.3 ln(CBR)", 3.99 + 21.3 * Math.Log(cbr, 2.71828), "kPa/mm");
        double fcf = r.Step("fcf", "f'cf = MF x 0.7 √f'c", ninetyDayFactor * Fcf(fc), "MPa");
        double ec = r.Step("ec", "Ec (AS3600 draft 2005 fit)", -8.0907 * fc * fc + 927.7 * fc + 8644.8, "MPa", "0");
        r.Step("hest", $"h estimate = {CalcResult.Num(estimateFactor)} √(Pw x 1000 / (0.5 f'cf))", estimateFactor * Math.Sqrt(pw * 1000 / (0.5 * fcf)), "mm", "0");
        double ff = r.Step("ff", "Fatigue factor 0.73 − 0.0846 (log N − 3)", repetitions < 400000 ? 0.73 - 0.0846 * (Math.Log10(repetitions) - 3) : 0.5);
        double fall = r.Step("fall", "f'cf allowable = FF x f'cf", ff * fcf, "MPa");

        // The stress at a thickness (the sheet's formulas, including its characteristic-length expression).
        (double L, double B, double Stress) At(double h)
        {
            double l = Math.Pow(ec * h * h * h / (12 * (1 - Math.Pow(poisson, 12)) * k / 1000), 0.25);
            double area = pw / tyreKpa * 1_000_000;
            double radius = Math.Sqrt(area / 3.142);
            double b = radius > 1.72 * h ? radius : Math.Sqrt(1.6 * radius * radius + h * h) - 0.675 * h;
            double ratio = l / b;
            double stress = where switch
            {
                WheelPosition.Interior => 0.316 * (pw * 1000 / (h * h)) * (4 * Math.Log10(ratio) + 1.069),
                WheelPosition.NearEdge => 0.803 * (pw * 1000 / (h * h)) * (4 * Math.Log10(ratio) + 0.666 / ratio - 0.034),
                WheelPosition.OnEdge => 0.803 * (pw * 1000 / (h * h)) * (4 * Math.Log10(ratio) + 0.282 / ratio + 0.65),
                _ => 4.2 * (pw * 1000 / (h * h)) * (1 - Math.Sqrt(b / l) / (0.925 + 0.22 * (b / l))),
            };
            return (l, b, stress);
        }

        var (lc, bc, sigma) = At(slabMm);
        r.Step("l", "l characteristic length", lc, "mm", "0");
        r.Step("b", "b equivalent tyre radius", bc, "mm", "0");
        r.Result("stress", "Applied stress σ", sigma, "MPa", "0.000");
        bool ok = fall >= 0.995 * sigma;
        r.Check("f'cf allowable ≥ σ", ok, $"{CalcResult.Num(fall, "0.000")} vs {CalcResult.Num(sigma, "0.000")} MPa");

        double min = 100;
        while (min < 1000 && ff * fcf < 0.995 * At(min).Stress) min += 5;
        r.Result("hmin", "Least thickness that passes (5 mm steps)", min, "mm", "0");
        r.Values["adopt"] = min;
        r.ThicknessMm = min;
        r.Headline = (ok ? "OK" : "NO GOOD") + $" at {CalcResult.Num(slabMm, "0")} mm (σ {CalcResult.Num(sigma, "0.00")} vs {CalcResult.Num(fall, "0.00")} MPa); least that passes {CalcResult.Num(min, "0")} mm";
        r.Notes.Add("f'c 25 to 50 MPa only. Uses the workbook's characteristic-length formula, which has (1 − v¹²) where Westergaard has (1 − v²); the difference in l is under 1%.");
        return r;
    }

    // ------------------------------------------------------------------ equivalent soil modulus ------

    /// <summary>One soil layer for <see cref="EquivalentModulus"/>. Zero means "not known".</summary>
    public sealed record SoilLayer(double DepthM, double Cbr = 0, double SptN = 0, double Pi = 0, double Qc = 0, double A = 0, double B = 1);

    /// <summary>Equivalent Young's modulus Ese of up to 4 soil layers for wheels, posts and UDL (sheet 5, T48 p78-9).</summary>
    public static CalcResult EquivalentModulus(IReadOnlyList<SoilLayer> layers, double wheelSpacing, double postSpacing, double udlSpacing)
    {
        var r = new CalcResult { Title = "Equivalent soil modulus Ese", Reference = T48 + " p78-79, Fig 1.15, 1.16, 1.19" };
        if (layers.Count is 0 or > 4) throw new ArgumentException("Give 1 to 4 soil layers.");
        int n = layers.Count;
        var shortE = new double[n];
        var longE = new double[n];
        var z = new double[n];
        double depth = 0;
        static double MinNonZero(double a, double b) => a > b ? (b == 0 ? a : b) : (a == 0 ? b : a);
        for (int i = 0; i < n; i++)
        {
            var L = layers[i];
            r.InText($"Layer {i + 1}", $"H {CalcResult.Num(L.DepthM)} m, CBR {CalcResult.Num(L.Cbr)}%, SPT N {CalcResult.Num(L.SptN)}, PI {CalcResult.Num(L.Pi)}, qc {CalcResult.Num(L.Qc)} MPa, a {CalcResult.Num(L.A)}, b {CalcResult.Num(L.B)}");
            double esLcbr = EsFromCbr(L.Cbr);
            double cubic = i < 2 ? 0.00002 : 0.0000275; // as in the workbook (columns D-E vs F-G)
            double ratio = L.Pi < 35
                ? -0.0000008 * Math.Pow(L.Pi, 4) + cubic * Math.Pow(L.Pi, 3) + 0.00165 * L.Pi * L.Pi - 0.1276 * L.Pi + 3.5
                : 11.315 * Math.Pow(L.Pi, -0.7462);
            double essCbr = L.B == 0 ? 0 : esLcbr / L.B;
            double essSpt = ratio * L.SptN;
            double eslSpt = L.B * essSpt;
            double essCpt = L.A * L.Qc;
            double eslCpt = L.B * essCpt;
            shortE[i] = MinNonZero(MinNonZero(essSpt, essCbr), essCpt);
            longE[i] = MinNonZero(MinNonZero(eslSpt, esLcbr), eslCpt);
            z[i] = L.DepthM > 0 || i < 2 ? depth + L.DepthM / 2 : 0;
            depth += L.DepthM;
            r.Step("ess" + (i + 1), $"Layer {i + 1}: Ess short term (wheels)", shortE[i], "MPa", "0.0");
            r.Step("esl" + (i + 1), $"Layer {i + 1}: EsL long term (posts, UDL)", longE[i], "MPa", "0.0");
        }

        double Combine(string key, string label, double spacing, Func<int, double, double> wf, double[] e)
        {
            double top = 0, bottom = 0;
            for (int i = 0; i < n; i++)
            {
                double w = wf(i, z[i] / spacing) * layers[i].DepthM;
                top += w;
                if (e[i] != 0) bottom += w / e[i];
            }
            return r.Result(key, label, e.Sum() == 0 || bottom == 0 ? 0 : top / bottom, "MPa", "0.0");
        }

        double wheel = Combine("wheel", $"Ese for WHEELS (S = {CalcResult.Num(wheelSpacing)} m)", wheelSpacing, (i, x) => i switch
        {
            0 => x < 1.51 ? 1 - x / 3 : x > 3.021 ? 0.6114 * Math.Exp(-0.2054 * x) : -0.0139 * Math.Pow(x, 5) + 0.191 * Math.Pow(x, 4) - 1.0032 * Math.Pow(x, 3) + 2.6302 * x * x - 3.5446 * x + 2.446,
            1 => x < 1.51 ? 1 - x / 3 : x > 3.01 ? 0.59 * Math.Exp(-0.2054 * x) : -0.0139 * Math.Pow(x, 5) + 0.1889 * Math.Pow(x, 4) - 1.0032 * Math.Pow(x, 3) + 2.6302 * x * x - 3.5446 * x + 2.446,
            _ => x < 1.51 ? 1 - x / 3.05 : x > 3.01 ? 0.59 * Math.Exp(-0.2054 * x) : -0.0139 * Math.Pow(x, 5) + 0.1889 * Math.Pow(x, 4) - 1.0032 * Math.Pow(x, 3) + 2.6302 * x * x - 3.5446 * x + 2.446,
        }, shortE);
        double post = Combine("post", $"Ese for POSTS (S = {CalcResult.Num(postSpacing)} m)", postSpacing, (_, x) =>
            x < 7 ? -0.000003 * Math.Pow(x, 6) + 0.0001 * Math.Pow(x, 5) - 0.0014 * Math.Pow(x, 4) + 0.0077 * Math.Pow(x, 3) + 0.0078 * x * x - 0.266 * x + 0.997 : 2.786 * Math.Pow(x, -1.6073), longE);
        double udl = Combine("udl", $"Ese for UDL (S = {CalcResult.Num(udlSpacing)} m)", udlSpacing, (_, x) =>
            x < 1.51 ? -0.04 * x * x * x + 0.12 * x * x - 0.31 * x + 1 : -0.0009 * x * x * x + 0.0235 * x * x - 0.2195 * x + 0.9338, longE);

        r.Headline = $"Ese wheels {CalcResult.Num(wheel, "0.0")} MPa, posts {CalcResult.Num(post, "0.0")} MPa, UDL {CalcResult.Num(udl, "0.0")} MPa";
        r.Notes.Add("Ese should not be outside the range of the layers' own Es; it is usually close to the average of the first few layers.");
        return r;
    }
}
