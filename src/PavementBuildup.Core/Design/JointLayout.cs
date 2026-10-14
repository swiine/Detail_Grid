using System.Text.Json;
using System.Text.Json.Serialization;

namespace PavementBuildup.Core.Design;

public enum SlabKind { Unreinforced, Reinforced, SteelFibre }

public sealed class SpacingRule
{
    public double TimesThickness { get; set; } = 25;
    public double CapM { get; set; } = 6;
}

public sealed class JointRuleSet
{
    public string Name { get; set; } = "TTW rule of thumb";
    public string Source { get; set; } = "";
    public SpacingRule Unreinforced { get; set; } = new() { TimesThickness = 25, CapM = 6 };
    public SpacingRule Reinforced { get; set; } = new() { TimesThickness = 60, CapM = 12 };
    public SpacingRule SteelFibre { get; set; } = new() { TimesThickness = 30, CapM = 8 };
    public double AspectLimit { get; set; } = 1.5;

    public SpacingRule For(SlabKind kind) => kind switch
    {
        SlabKind.Reinforced => Reinforced,
        SlabKind.SteelFibre => SteelFibre,
        _ => Unreinforced,
    };

    public override string ToString() => Name;
}

public sealed class JointRulesFile
{
    public const string FileName = "TTW_JointRules.json";
    public List<JointRuleSet> Sets { get; set; } = new();
    [JsonIgnore] public string? SourcePath { get; private set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, PropertyNameCaseInsensitive = true,
    };

    public static JointRulesFile CreateDefault() => new()
    {
        Sets =
        {
            new JointRuleSet { Source = "Rule of thumb: check against CCAA T48 and the project specification" },
            new JointRuleSet
            {
                Name = "Example council (edit me)", Source = "Replace with the council's specification clause",
                Unreinforced = new() { TimesThickness = 24, CapM = 4.5 }, Reinforced = new() { TimesThickness = 50, CapM = 10 },
                SteelFibre = new() { TimesThickness = 30, CapM = 6 }, AspectLimit = 1.25,
            },
        },
    };

    public static JointRulesFile LoadCompany()
    {
        var path = StandardStore.FindSupportFile(FileName);
        if (path is null) return CreateDefault();
        try
        {
            var file = JsonSerializer.Deserialize<JointRulesFile>(File.ReadAllText(path), Options);
            if (file is null || file.Sets.Count == 0) return CreateDefault();
            file.SourcePath = path;
            return file;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return CreateDefault(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
        SourcePath = path;
    }

    public JointRuleSet Find(string? name) =>
        Sets.FirstOrDefault(s => string.Equals(s.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Sets.FirstOrDefault() ?? new JointRuleSet();
}

public sealed record JointInputs
{
    public double LengthM { get; init; } = 30;
    public double WidthM { get; init; } = 20;
    public double ThicknessMm { get; init; } = 200;
    public SlabKind Kind { get; init; } = SlabKind.Unreinforced;
    public double MaxSpacingM { get; init; }
    public JointRuleSet? Rules { get; init; }
    public JointRuleSet RuleSet => Rules ?? new JointRuleSet();
    public double AspectLimit => RuleSet.AspectLimit;
}

public sealed record JointPlan(JointInputs Inputs, double SpacingLimitM, int PanelsAlong, int PanelsAcross,
    double PanelLengthM, double PanelWidthM, IReadOnlyList<double> CutsAlongX, IReadOnlyList<double> CutsAlongY, int SawcutDepthMm)
{
    public double SawcutLengthM => CutsAlongX.Count * Inputs.WidthM + CutsAlongY.Count * Inputs.LengthM;
    public double PerimeterM => 2 * (Inputs.LengthM + Inputs.WidthM);
    public int Panels => PanelsAlong * PanelsAcross;
    public double Aspect => Math.Max(PanelLengthM, PanelWidthM) / Math.Min(PanelLengthM, PanelWidthM);
}

public static class JointLayout
{
    public static string Describe(SlabKind kind) => kind switch
    {
        SlabKind.Reinforced => "Reinforced (crack-control mesh)",
        SlabKind.SteelFibre => "Steel fibre reinforced",
        _ => "Unreinforced (plain)",
    };

    public static double SpacingLimit(JointInputs j)
    {
        if (j.MaxSpacingM > 0) return j.MaxSpacingM;
        var rule = j.RuleSet.For(j.Kind);
        return Math.Min(rule.CapM > 0 ? rule.CapM : double.MaxValue, rule.TimesThickness * j.ThicknessMm / 1000);
    }

    public static JointPlan Plan(JointInputs j)
    {
        if (!(j.LengthM > 0) || !(j.WidthM > 0)) throw new ArgumentException("Enter the slab length and width (m).");
        if (!(j.ThicknessMm > 0)) throw new ArgumentException("Enter the slab thickness (mm).");
        if (j.LengthM > 2000 || j.WidthM > 2000) throw new ArgumentException("The slab is too big for a joint layout (over 2 km).");
        double limit = SpacingLimit(j);
        if (!(limit >= 0.5)) throw new ArgumentException("The joint spacing must be at least 0.5 m.");
        double aspectLimit = Math.Max(1, j.AspectLimit);

        int nx = Math.Max(1, (int)Math.Ceiling(j.LengthM / limit - 1e-9));
        int ny = Math.Max(1, (int)Math.Ceiling(j.WidthM / limit - 1e-9));
        for (int guard = 0; guard < 1000; guard++)
        {
            double a = j.LengthM / nx, b = j.WidthM / ny;
            if (Math.Max(a, b) / Math.Min(a, b) <= aspectLimit + 1e-9) break;
            if (a > b) nx++; else ny++;
        }
        double pa = j.LengthM / nx, pb = j.WidthM / ny;
        var xs = Enumerable.Range(1, nx - 1).Select(i => i * pa).ToList();
        var ys = Enumerable.Range(1, ny - 1).Select(i => i * pb).ToList();
        int depth = (int)(Math.Ceiling(j.ThicknessMm / 4 / 5 - 1e-9) * 5);
        return new JointPlan(j, limit, nx, ny, pa, pb, xs, ys, depth);
    }

    public static CalcResult Calc(JointInputs j)
    {
        var p = Plan(j);
        var set = j.RuleSet;
        var r = new CalcResult { Title = "Joint layout", Reference = set.Name + (set.Source.Length > 0 ? " - " + set.Source : "") };
        r.In("Slab length x width", j.LengthM, $"x {j.WidthM:0.##} m", "0.##");
        r.In("Slab thickness", j.ThicknessMm, "mm", "0");
        r.InText("Slab type", Describe(j.Kind));
        r.InText("Joint rules", set.Name);
        var rule = set.For(j.Kind);
        double times = rule.TimesThickness, cap = rule.CapM;
        r.Step("limit", j.MaxSpacingM > 0 ? "Maximum joint spacing (entered)" : $"Maximum joint spacing = min({times:0} x thickness, {cap:0} m)",
            p.SpacingLimitM, "m", "0.00");
        r.Result("nx", "Panels along the length", p.PanelsAlong, "", "0");
        r.Result("ny", "Panels across the width", p.PanelsAcross, "", "0");
        r.Result("a", "Panel size along", p.PanelLengthM, "m", "0.00");
        r.Result("b", "Panel size across", p.PanelWidthM, "m", "0.00");
        r.Check("Panel aspect ratio ≤ " + CalcResult.Num(Math.Max(1, j.AspectLimit), "0.0#"), p.Aspect <= Math.Max(1, j.AspectLimit) + 1e-9, CalcResult.Num(p.Aspect, "0.00"));
        r.Check("Panel size ≤ maximum spacing", Math.Max(p.PanelLengthM, p.PanelWidthM) <= p.SpacingLimitM + 1e-9,
            $"{Math.Max(p.PanelLengthM, p.PanelWidthM):0.00} ≤ {p.SpacingLimitM:0.00} m");
        r.Result("panels", "Number of panels", p.Panels, "", "0");
        r.Result("sawcut", "Sawcut joints, total length", p.SawcutLengthM, "m", "0.0");
        r.Result("perimeter", "Isolation joint round the slab", p.PerimeterM, "m", "0.0");
        r.Result("depth", "Sawcut depth (≥ 1/4 of the thickness)", p.SawcutDepthMm, "mm", "0");
        r.Values["aspect"] = p.Aspect;
        r.ThicknessMm = j.ThicknessMm;
        r.Headline = $"{p.PanelsAlong} x {p.PanelsAcross} panels of {p.PanelLengthM:0.00} x {p.PanelWidthM:0.00} m, sawcut {p.SawcutDepthMm} mm deep";
        r.Notes.Add("Sawcut as soon as the concrete can be cut without ravelling (usually within 6 to 18 hours of placing).");
        r.Notes.Add("Isolation joints round columns, pits, walls and other fixed objects; extra joints or trimmer bars at re-entrant corners.");
        r.Notes.Add("Dowel or key construction joints and joints that carry forklift traffic.");
        r.Notes.Add($"Spacing from \"{set.Name}\" ({JointRulesFile.FileName}): the engineer confirms the joint layout and spacing for each slab.");
        if (j.Kind == SlabKind.Reinforced)
            r.Notes.Add("Reinforced slab: the joint spacing depends on the crack-control reinforcement - set the maximum spacing from the reinforcement design.");
        return r;
    }
}
