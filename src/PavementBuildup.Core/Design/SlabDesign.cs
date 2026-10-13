namespace PavementBuildup.Core.Design;

/// <summary>Everything a concrete slab design needs, entered once.</summary>
public sealed record SlabInputs
{
    // Concrete and ground
    public double Fc { get; init; } = 32;
    public double Cbr { get; init; } = 5;
    /// <summary>Soil modulus for wheels / posts / UDL (MPa); 0 = from the CBR (T48 Fig 1.15).</summary>
    public double EsOverride { get; init; }
    public double SoilDepth { get; init; } = 5;
    /// <summary>Check loads at free edges (joints without dowels / slab edges) as well as the interior.</summary>
    public bool CheckEdges { get; init; } = true;

    // Wheels (forklifts, trucks)
    public bool Wheels { get; init; } = true;
    public double AxleTonnes { get; init; } = 10;
    public double WheelSpacing { get; init; } = 1.5;
    public double PassesPerDay { get; init; } = 10;
    public double Years { get; init; } = 20;
    public double TyreKpa { get; init; } = 700;
    public double K1Wheel { get; init; } = 0.85;
    public double K3Interior { get; init; } = 1.2;
    public double K3Edge { get; init; } = 1.05;

    // Racks (post loads)
    public bool Posts { get; init; }
    public double PostKn { get; init; } = 70;
    public double PostX { get; init; } = 2;
    public double PostY { get; init; } = 1.5;
    public double K1Post { get; init; } = 0.8;
    public double K2Post { get; init; } = 0.85;
    public double BasePlateMm { get; init; } = 165;
    /// <summary>P* = factor x P for punching shear (e.g. 1.5 for live load).</summary>
    public double LoadFactor { get; init; } = 1.5;

    // Distributed load (storage)
    public bool Udl { get; init; }
    public double UdlKpa { get; init; } = 30;
    public double AisleWidth { get; init; } = 4;
    public double UdlRepetitions { get; init; } = 1000;
    public double K1Udl { get; init; } = 0.8;

    /// <summary>Also cross-check wheels with Westergaard / Ioannides / Pickett (T34).</summary>
    public bool T34Checks { get; init; } = true;
}

/// <summary>
/// Concrete industrial slab: every T48 check the loads call for, at once, the governing thickness, punching
/// shear at that thickness, T34 cross-checks and a thickness-vs-grade table.
/// </summary>
public static class SlabDesign
{
    public static CalcResult Design(SlabInputs s, string? title = null) => Run(s, title, whatIf: true);

    private static CalcResult Run(SlabInputs s, string? title, bool whatIf)
    {
        if (!(s.Cbr > 0)) throw new ArgumentException("Enter the subgrade CBR (greater than 0%).");
        if (!s.Wheels && !s.Posts && !s.Udl) throw new ArgumentException("Tick at least one load: wheels, racks or a distributed load.");
        var r = new CalcResult { Title = "Concrete slab design", Reference = ConcreteDesign.T48 + "; AS3600; " + ConcreteDesign.T34 };

        double es = s.EsOverride > 0 ? s.EsOverride : ConcreteDesign.EsFromCbr(s.Cbr);
        double k = ConcreteDesign.KFromCbr(s.Cbr);
        r.In("f'c concrete", s.Fc, "MPa");
        r.In("Subgrade CBR", s.Cbr, "%");
        r.In("Soil depth H", s.SoilDepth, "m");
        if (s.Wheels)
        {
            r.In("Heaviest axle", s.AxleTonnes, "tonne");
            r.In("Wheel spacing", s.WheelSpacing, "m");
            r.In("Passes per day / design life", s.PassesPerDay, $"/ {s.Years:0} years");
        }
        if (s.Posts)
        {
            r.In("Rack post load (unfactored)", s.PostKn, "kN");
            r.InText("Post spacing x / y", $"{s.PostX:0.##} / {s.PostY:0.##} m");
        }
        if (s.Udl) r.In("Distributed load", s.UdlKpa, "kPa");
        r.InText("Free edges checked", s.CheckEdges ? "Yes" : "No");
        r.Step("es", s.EsOverride > 0 ? "Soil modulus Es (entered)" : "Soil modulus Es from CBR (T48 Fig 1.15)", es, "MPa", "0.0");
        r.Step("k", "Modulus of subgrade reaction k from CBR", k, "kPa/mm", "0.0");

        var checks = new List<(string Name, CalcResult Calc)>();
        double axleKn = s.AxleTonnes * 9.81;
        if (s.Wheels)
        {
            checks.Add(("Wheels - interior", ConcreteDesign.Wheel(false, s.Fc, s.K1Wheel, s.PassesPerDay, s.Years, s.K3Interior, s.WheelSpacing, s.SoilDepth, es, axleKn)));
            if (s.CheckEdges)
                checks.Add(("Wheels - edge", ConcreteDesign.Wheel(true, s.Fc, s.K1Wheel, s.PassesPerDay, s.Years, s.K3Edge, s.WheelSpacing, s.SoilDepth, es, axleKn)));
        }
        if (s.Posts)
        {
            checks.Add(("Racks - interior post", ConcreteDesign.Post(false, s.Fc, s.K1Post, s.K2Post, s.PostX, s.PostY, s.SoilDepth, es, s.PostKn)));
            if (s.CheckEdges)
                checks.Add(("Racks - edge post", ConcreteDesign.Post(true, s.Fc, s.K1Post, 1, s.PostX, s.PostY, s.SoilDepth, es, s.PostKn)));
        }
        if (s.Udl)
            checks.Add(("Distributed load (UDL)", ConcreteDesign.Udl(s.Fc, s.K1Udl, s.UdlRepetitions, s.AisleWidth, s.SoilDepth, es, s.UdlKpa)));

        // Governing T48 thickness.
        double governing = 0;
        string governedBy = "";
        foreach (var (name, calc) in checks)
        {
            if (calc.Pass == false || calc.ThicknessMm is null)
            {
                r.Lines.Add(new CalcLine(name, "-", "", LineKind.Fail, calc.Headline));
                r.Warn($"{name}: {calc.Headline}");
                continue;
            }
            r.Lines.Add(new CalcLine(name, CalcResult.Num(calc.Values["t"], "0"), "mm", LineKind.Step, "adopt " + CalcResult.Num(calc.ThicknessMm.Value, "0")));
            if (calc.ThicknessMm.Value > governing)
            {
                governing = calc.ThicknessMm.Value;
                governedBy = name;
            }
        }

        // Punching shear at the governing thickness; thicken until it passes.
        if (s.Posts && governing > 0)
        {
            double t = governing;
            double pStar = s.PostKn * s.LoadFactor;
            while (t < 1000 && ConcreteDesign.PunchingShear(s.Fc, s.BasePlateMm, t, pStar).Pass != true) t += 5;
            var punch = ConcreteDesign.PunchingShear(s.Fc, s.BasePlateMm, t, pStar);
            checks.Add(("Racks - punching shear", punch));
            r.Lines.Add(new CalcLine("Racks - punching shear (P* = " + CalcResult.Num(s.LoadFactor) + " x P)", CalcResult.Num(punch.Values["phiVuo"], "0"), "kN",
                LineKind.Step, $"φVuo at {CalcResult.Num(t, "0")} mm vs P* {CalcResult.Num(pStar, "0")} kN"));
            if (t > governing)
            {
                governing = t;
                governedBy = "Racks - punching shear";
            }
        }

        if (governing <= 0)
        {
            r.Pass = false;
            r.Headline = "No thickness found: see the warnings";
            foreach (var (_, calc) in checks) r.Parts.Add(calc);
            return r;
        }

        r.Result("t", "Governing thickness", governing, "mm", "0");
        r.Lines.Add(new CalcLine("Governed by", governedBy, "", LineKind.Ok));
        r.ThicknessMm = governing;
        r.Pass = true;
        r.Headline = $"Adopt {governing:0} mm f'c {s.Fc:0} MPa concrete (governed by {governedBy.ToLowerInvariant()})";

        // T34 cross-checks at the adopted thickness.
        if (s.Wheels && s.T34Checks)
        {
            double n = ConcreteDesign.Repetitions(s.PassesPerDay, s.Years);
            var positions = s.CheckEdges
                ? new[] { ConcreteDesign.WheelPosition.Interior, ConcreteDesign.WheelPosition.NearEdge, ConcreteDesign.WheelPosition.OnEdge, ConcreteDesign.WheelPosition.Corner }
                : new[] { ConcreteDesign.WheelPosition.Interior };
            foreach (var where in positions)
            {
                var c = ConcreteDesign.WheelStress(where, s.AxleTonnes, s.Cbr, s.Fc, 1, 0.15, governing, s.TyreKpa, n);
                checks.Add(("T34 " + where, c));
                r.Lines.Add(new CalcLine("Cross-check: " + c.Title.Replace("Wheel stress - ", ""), c.Pass == true ? "OK" : "NO GOOD", "",
                    c.Pass == true ? LineKind.Ok : LineKind.Warning, $"least {CalcResult.Num(c.ThicknessMm ?? 0, "0")} mm"));
                if (c.Pass != true)
                    r.Notes.Add($"T34 {c.Title.Replace("Wheel stress - ", "").ToLowerInvariant()} needs {CalcResult.Num(c.ThicknessMm ?? 0, "0")} mm (T48 governs the design; consider it for unjointed edges / corners).");
            }
        }

        // What if the concrete grade changes?
        var rows = new List<string[]>();
        foreach (var grade in whatIf ? ConcreteDesign.Grades : Array.Empty<double>())
        {
            string text;
            try
            {
                var alt = Run(s with { Fc = grade, T34Checks = false }, null, whatIf: false);
                text = alt.ThicknessMm is { } t2 ? $"{t2:0} mm" : "-";
            }
            catch (ArgumentException) { text = "-"; }
            rows.Add(new[] { (Math.Abs(grade - s.Fc) < 1e-9 ? "► " : "") + $"{grade:0} MPa", text });
        }
        if (whatIf) r.Tables.Add(("Thickness vs concrete grade", new[] { "f'c", "Governing thickness" }, rows));

        r.BuildupText = (string.IsNullOrWhiteSpace(title) ? "" : $"[{title}]\n")
            + $"{governing:0}mm F'c {s.Fc:0}MPa CONCRETE PAVEMENT\nSUBGRADE MINIMUM CBR {s.Cbr:0.#}% COMPACTED TO 98% SMDD AT ±2% OMC";
        r.Notes.Add("Working-strength design (unfactored loads) to T48; punching shear uses P* = load factor x P.");
        r.Notes.Add("Base / subbase under the slab, joints, reinforcement and curing are not part of this calculation.");
        foreach (var (_, calc) in checks) r.Parts.Add(calc);
        return r;
    }
}
