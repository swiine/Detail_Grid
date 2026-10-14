namespace PavementBuildup.Core;

public sealed record HatchSpec(
    string Pattern,
    double BaseScale,
    double AngleDeg,
    string Layer = "",
    string Color = "BYLAYER",
    string BackgroundColor = "",
    string RuleName = "",
    int Transparency = -1,
    double LineWeightMm = -1);

public static partial class MaterialLibrary
{
    public const string Auto = "AUTO";
    public const string None = "NONE";

    public static readonly string[] KnownPatterns =
    {
        Auto, None, "AR-SAND", "AR-CONC", "GRAVEL", "EARTH", "ANSI31", "ANSI32", "ANSI37",
        "AR-HBONE", "DOTS", "HONEY", "CROSS", "NET", "SOLID",
    };

    public static HatchSpec? Resolve(PavementLayer layer, CadStandard standard)
    {
        if (layer.ThicknessMm <= 0)
            return null;

        var rule = RuleFor(layer, standard);
        if (rule is null)
            return null;

        var spec = ToSpec(rule);
        return spec is null ? null : spec with { AngleDeg = layer.HatchAngle ?? spec.AngleDeg };
    }

    public static HatchRule? RuleFor(PavementLayer layer, CadStandard standard)
    {
        var pattern = layer.HatchPattern?.Trim();
        if (string.Equals(pattern, None, StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.IsNullOrEmpty(pattern) || string.Equals(pattern, Auto, StringComparison.OrdinalIgnoreCase))
        {
            var text = $"{BuildupParser.Core(layer.Description)} {layer.Role}".Trim();
            return standard.RuleFor(AsphaltConcreteRegex().Replace(text, "asphalt"));
        }
        return standard.RuleForPattern(pattern);
    }

    public static HatchSpec? ToSpec(HatchRule rule) => rule.IsNone
        ? null
        : new HatchSpec(rule.Pattern.Trim().ToUpperInvariant(), rule.Scale, rule.Angle,
            rule.Layer?.Trim() ?? "", rule.Color ?? "BYLAYER", rule.BackgroundColor ?? "", rule.Name,
            rule.Transparency, rule.LineWeightMm);

    [System.Text.RegularExpressions.GeneratedRegex(@"\b(?:asphalt(?:ic)?|bituminous)\s+concrete\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex AsphaltConcreteRegex();
}
