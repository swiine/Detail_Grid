using System.Globalization;
using System.Text.Json;

namespace PavementBuildup.Core.Design;

public sealed class DesignFile
{
    public const string Extension = ".ttwdesign";

    public string Kind { get; set; } = "Road";
    public string Project { get; set; } = "";
    public string JobNo { get; set; } = "";
    public string PavementName { get; set; } = "";
    public string Engineer { get; set; } = "";
    public string Checker { get; set; } = "";
    public string Desa { get; set; } = "";
    public bool FromCounts { get; set; }
    public int Grade { get; set; } = 1;
    public Dictionary<string, string> Fields { get; set; } = new();
    public Dictionary<string, bool> Checks { get; set; } = new();
    public string JointType { get; set; } = nameof(SlabKind.Unreinforced);
    public string JointRules { get; set; } = "";

    public bool IsSlab => string.Equals(Kind, "Slab", StringComparison.OrdinalIgnoreCase);

    public string Title => PavementName.Trim().Length > 0 ? PavementName.Trim() : Project.Trim();

    public sealed record FieldDef(string Key, string Label, string Unit, string Default);

    public static readonly IReadOnlyList<FieldDef> Defs = new FieldDef[]
    {
        new("cbr", "Subgrade CBR", "%", "5"),
        new("h", "Depth of soil to rock / firm layer", "m", "5"),
        new("es", "Soil modulus Es (optional)", "MPa", ""),
        new("aadt", "Traffic, both directions (AADT)", "veh/day", "2000"),
        new("hv", "Heavy vehicles", "%", "10"),
        new("growth", "Growth", "%", "2"),
        new("years", "Design life", "years", "20"),
        new("esahv", "ESA per heavy vehicle", "", "1"),
        new("df", "Direction factor", "", "0.5"),
        new("ldf", "Lane factor", "", "1"),
        new("axle", "Heaviest axle load", "tonne", "10"),
        new("ws", "Wheel spacing", "m", "1.5"),
        new("passes", "Passes per day", "", "10"),
        new("life", "Design life", "years", "20"),
        new("tyre", "Tyre pressure", "kPa", "700"),
        new("post", "Post load (unfactored)", "kN", "70"),
        new("px", "Post spacing x", "m", "2"),
        new("py", "Post spacing y", "m", "1.5"),
        new("plate", "Base plate width", "mm", "165"),
        new("lf", "Load factor for punching", "", "1.5"),
        new("udl", "Distributed load", "kPa", "30"),
        new("aisle", "Stack or aisle width", "m", "4"),
        new("udlreps", "Loadings over the life", "", "1000"),
        new("k1w", "k1 wheels", "", "0.85"),
        new("k3i", "k3 interior", "", "1.2"),
        new("k3e", "k3 edge", "", "1.05"),
        new("k1p", "k1 posts", "", "0.8"),
        new("k2p", "k2 posts", "", "0.85"),
        new("k1u", "k1 distributed load", "", "0.8"),
        new("area", "Pavement area (optional)", "m2", ""),
        new("jl", "Slab length", "m", "30"),
        new("jw", "Slab width", "m", "20"),
        new("jmax", "Maximum joint spacing (optional)", "m", ""),
    };

    public static readonly IReadOnlySet<string> OptionalKeys = new HashSet<string> { "es", "area", "jmax" };

    public static FieldDef Def(string key) => Defs.First(d => d.Key == key);

    public static readonly IReadOnlyDictionary<string, bool> CheckDefaults = new Dictionary<string, bool>
    {
        ["edges"] = true, ["t34"] = true, ["wheels"] = true, ["posts"] = false, ["udl"] = false, ["joints"] = false,
    };

    public static string ReadUnit(FieldDef d) => d.Unit is "veh/day" or "years" or "" or "m2" ? "" : d.Unit;

    public string Text(string key) => Fields.TryGetValue(key, out var t) ? t : Def(key).Default;

    public bool Check(string key) => Checks.TryGetValue(key, out var on) ? on : CheckDefaults.TryGetValue(key, out var d) && d;

    public double Value(string key)
    {
        var def = Def(key);
        var text = Text(key).Trim();
        if (text.Length == 0 && OptionalKeys.Contains(key)) return 0;
        if (!Inputs.TryRead(text, ReadUnit(def), out var v, out var error))
            throw new ArgumentException($"{def.Label}: {error}");
        if (v < 0) throw new ArgumentException($"{def.Label}: can't be negative.");
        return v;
    }

    public double DesaValue()
    {
        var text = Desa.Trim();
        if (text.Length == 0) return FlexibleDesign.TrafficRows[2].Desa;
        foreach (var row in FlexibleDesign.TrafficRows)
            if (text.StartsWith(row.Label + "  -", StringComparison.Ordinal) || text == row.Label) return row.Desa;
        var head = text.Split(new[] { "  -  " }, StringSplitOptions.None)[0];
        if (Inputs.TryRead(head, "ESA", out var v, out var error)) return v;
        throw new ArgumentException("Design traffic: " + error);
    }

    public double Fc => ConcreteDesign.Grades[Math.Clamp(Grade, 0, ConcreteDesign.Grades.Length - 1)];

    public SlabKind JointKind => Enum.TryParse<SlabKind>(JointType, true, out var k) ? k : SlabKind.Unreinforced;

    public CalcResult Run(RateTable? rates = null, JointRulesFile? jointRules = null)
    {
        string? title = Title.Length == 0 ? null : Title.ToUpperInvariant();
        CalcResult result;
        if (!IsSlab)
        {
            var traffic = FromCounts
                ? new TrafficInputs(0, Value("aadt"), Value("hv"), Value("df"), Value("ldf"), Value("growth"), Value("years"), Value("esahv"))
                : new TrafficInputs(DesaValue());
            result = RoadDesign.Design(Value("cbr"), traffic, title);
        }
        else
        {
            var s = new SlabInputs
            {
                Fc = Fc, Cbr = Value("cbr"), EsOverride = Value("es"), SoilDepth = Value("h"),
                CheckEdges = Check("edges"), T34Checks = Check("t34"),
                Wheels = Check("wheels"), AxleTonnes = Value("axle"), WheelSpacing = Value("ws"), PassesPerDay = Value("passes"),
                Years = Value("life"), TyreKpa = Value("tyre"), K1Wheel = Value("k1w"), K3Interior = Value("k3i"), K3Edge = Value("k3e"),
                Posts = Check("posts"), PostKn = Value("post"), PostX = Value("px"), PostY = Value("py"), BasePlateMm = Value("plate"),
                LoadFactor = Value("lf"), K1Post = Value("k1p"), K2Post = Value("k2p"),
                Udl = Check("udl"), UdlKpa = Value("udl"), AisleWidth = Value("aisle"), UdlRepetitions = Value("udlreps"), K1Udl = Value("k1u"),
            };
            result = SlabDesign.Design(s, title);
            if (Check("joints") && result.ThicknessMm is { } t)
            {
                var joints = JointLayout.Calc(Joints(t, jointRules));
                result.Parts.Add(joints);
                result.Lines.Add(new CalcLine("Joint layout", joints.Headline, "", LineKind.Step, "see Joint layout"));
            }
        }
        if (rates is not null && result.BuildupText is { } text)
        {
            var read = SmartReader.Read(text, new List<string>());
            var cost = CostEstimate.For(read.Parsed.ToBuildup(title ?? "PAVEMENT"), rates, Value("area"));
            result.Parts.Add(cost);
            result.Values["cost_m2"] = cost.Values["cost_m2"];
            if (cost.Values.TryGetValue("cost_total", out var total)) result.Values["cost_total"] = total;
            result.Lines.Add(new CalcLine("Rough cost (placeholder-able rates)", cost.Headline, "", LineKind.Step, "see Rough cost estimate"));
        }
        return result;
    }

    public JointInputs Joints(double thicknessMm, JointRulesFile? rules = null) => new()
    {
        LengthM = Value("jl"), WidthM = Value("jw"), ThicknessMm = thicknessMm, Kind = JointKind, MaxSpacingM = Value("jmax"),
        Rules = rules?.Find(JointRules),
    };

    public string LoadSummary()
    {
        try
        {
            if (!IsSlab)
                return FromCounts
                    ? $"AADT {Text("aadt")}, {Text("hv")}% HV, {Text("years")} yrs"
                    : "DESA " + DesaValue().ToString("0.0E+00", CultureInfo.InvariantCulture);
            var parts = new List<string> { $"f'c {Fc:0} MPa" };
            if (Check("wheels")) parts.Add($"{Text("axle")} t axle");
            if (Check("posts")) parts.Add($"racks {Text("post")} kN");
            if (Check("udl")) parts.Add($"UDL {Text("udl")} kPa");
            return string.Join(" · ", parts);
        }
        catch (ArgumentException) { return "-"; }
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static DesignFile FromJson(string json) =>
        JsonSerializer.Deserialize<DesignFile>(json, Options) ?? throw new InvalidDataException("The design file is empty.");

    public void Save(string path) => File.WriteAllText(path, ToJson());

    public static DesignFile Load(string path)
    {
        try { return FromJson(File.ReadAllText(path)); }
        catch (JsonException ex) { throw new InvalidDataException($"{Path.GetFileName(path)} isn't a design file: {ex.Message}", ex); }
    }
}
