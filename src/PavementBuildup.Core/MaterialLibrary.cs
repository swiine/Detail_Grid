namespace PavementBuildup.Core;

/// <summary>A resolved hatch for one band of the detail.</summary>
/// <remarks>
/// <see cref="BaseScale"/> is the pattern scale for a 1:1 detail in a millimetre drawing; the layout
/// multiplies it by the detail scale and drawing units. <see cref="Layer"/> blank means the Hatch layer.
/// </remarks>
public sealed record HatchSpec(
    string Pattern,
    double BaseScale,
    double AngleDeg,
    string Layer = "",
    string Color = "BYLAYER",
    string BackgroundColor = "",
    string RuleName = "");

/// <summary>Picks the hatch for a course using the hatch rules of a <see cref="CadStandard"/>.</summary>
public static class MaterialLibrary
{
    public const string Auto = "AUTO";
    public const string None = "NONE";

    /// <summary>Patterns offered in the course table (all ship with AutoCAD / Civil 3D).</summary>
    public static readonly string[] KnownPatterns =
    {
        Auto, None, "AR-SAND", "AR-CONC", "GRAVEL", "EARTH", "ANSI31", "ANSI32", "ANSI37",
        "AR-HBONE", "DOTS", "HONEY", "CROSS", "NET", "SOLID",
    };

    /// <summary>
    /// The hatch for a course, or null when it is not hatched (zero thickness, NONE, or a rule with pattern NONE).
    /// An explicit pattern on the course overrides the keyword rules.
    /// </summary>
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

    /// <summary>The rule a course uses, or null for an explicit NONE.</summary>
    public static HatchRule? RuleFor(PavementLayer layer, CadStandard standard)
    {
        var pattern = layer.HatchPattern?.Trim();
        if (string.Equals(pattern, None, StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.IsNullOrEmpty(pattern) || string.Equals(pattern, Auto, StringComparison.OrdinalIgnoreCase))
            return standard.RuleFor(layer.Description);
        return standard.RuleForPattern(pattern);
    }

    public static HatchSpec? ToSpec(HatchRule rule) => rule.IsNone
        ? null
        : new HatchSpec(rule.Pattern.Trim().ToUpperInvariant(), rule.Scale, rule.Angle,
            rule.Layer?.Trim() ?? "", rule.Color ?? "BYLAYER", rule.BackgroundColor ?? "", rule.Name);
}
