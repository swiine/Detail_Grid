namespace PavementBuildup.Core;

/// <summary>A resolved hatch: pattern name, base scale and angle (degrees).</summary>
/// <remarks>
/// <see cref="BaseScale"/> is tuned for the metric pattern file (acadiso.pat) so that the hatch
/// looks right on paper; the drawer multiplies it by the detail scale and drawing units.
/// </remarks>
public sealed record HatchSpec(string Pattern, double BaseScale, double AngleDeg);

/// <summary>Picks a sensible section hatch from a course description.</summary>
public static class MaterialLibrary
{
    public const string Auto = "AUTO";
    public const string None = "NONE";

    /// <summary>Patterns offered in the dialog (all ship with AutoCAD / Civil 3D).</summary>
    public static readonly string[] KnownPatterns =
    {
        Auto, None, "AR-SAND", "AR-CONC", "GRAVEL", "EARTH", "ANSI31", "ANSI32", "ANSI37",
        "AR-HBONE", "DOTS", "HONEY", "CROSS", "NET", "SOLID",
    };

    private static readonly Dictionary<string, HatchSpec> PatternDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AR-SAND"] = new("AR-SAND", 0.15, 0),
        ["AR-CONC"] = new("AR-CONC", 0.08, 0),
        ["GRAVEL"] = new("GRAVEL", 0.15, 0),
        ["EARTH"] = new("EARTH", 0.5, 45),
        ["ANSI31"] = new("ANSI31", 0.5, 0),
        ["ANSI32"] = new("ANSI32", 0.5, 0),
        ["ANSI37"] = new("ANSI37", 0.5, 0),
        ["AR-HBONE"] = new("AR-HBONE", 0.05, 0),
        ["DOTS"] = new("DOTS", 0.5, 0),
        ["HONEY"] = new("HONEY", 0.5, 0),
        ["CROSS"] = new("CROSS", 0.5, 0),
        ["NET"] = new("NET", 0.5, 0),
        ["SOLID"] = new("SOLID", 1, 0),
    };

    private sealed record Rule(string[] Keywords, HatchSpec? Spec);

    // First match wins, so the more specific materials come first.
    private static readonly Rule[] Rules =
    {
        new(new[] { "geotextile", "geogrid", "geocomposite", "membrane", "dpm", "separator", "tack", "bond coat", "spray" }, null),
        new(new[] { "block", "paver", "sett", "flag" }, new("ANSI37", 0.5, 0)),
        new(new[] { "concrete", "pqc", "crcp", "jpcp", "urcp", "cbgm", "cbm", "hbm", "lean mix", "leanmix", "cement", "c8/10", "c32/40", "c40/50" }, new("AR-CONC", 0.08, 0)),
        new(new[] { "bedding", "laying course", "sand" }, new("AR-SAND", 0.1, 0)),
        new(new[] { "topsoil", "subgrade", "clay", "earth", "soil", "formation" }, new("EARTH", 0.5, 45)),
        new(new[] { "type 1", "type1", "type 2", "type 3", "type 4", "mot", "sub-base", "subbase", "sub base", "granular",
                    "crushed", "aggregate", "capping", "6f", "hardcore", "stone", "gravel", "rubble", "scalpings" }, new("GRAVEL", 0.15, 0)),
        new(new[] { "sma", "hra", "surface", "wearing", "thin surf", "porous", "pa ", "chip" }, new("AR-SAND", 0.15, 0)),
        new(new[] { "binder" }, new("AR-SAND", 0.3, 0)),
        new(new[] { "dbm", "hdm", "ac 32", "ac32", "base", "macadam", "asphalt", "bitum", "tarmac", "emac" }, new("ANSI31", 0.5, 0)),
    };

    private static readonly HatchSpec Fallback = new("ANSI31", 0.5, 0);

    /// <summary>Resolves the hatch for a layer, or null when it should not be hatched.</summary>
    public static HatchSpec? Resolve(PavementLayer layer)
    {
        if (layer.ThicknessMm <= 0)
            return null;

        var spec = ResolvePattern(layer.HatchPattern, layer.Description);
        if (spec is null)
            return null;

        return spec with { AngleDeg = layer.HatchAngle ?? spec.AngleDeg };
    }

    /// <summary>Hatch for an explicit pattern name, or keyword lookup when the name is AUTO/empty.</summary>
    public static HatchSpec? ResolvePattern(string? pattern, string description)
    {
        pattern = pattern?.Trim();
        if (string.Equals(pattern, None, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!string.IsNullOrEmpty(pattern) && !string.Equals(pattern, Auto, StringComparison.OrdinalIgnoreCase))
            return PatternDefaults.TryGetValue(pattern, out var known) ? known : new HatchSpec(pattern.ToUpperInvariant(), 1.0, 0);

        var text = " " + description.ToLowerInvariant() + " ";
        foreach (var rule in Rules)
        {
            if (rule.Keywords.Any(k => text.Contains(k, StringComparison.Ordinal)))
                return rule.Spec;
        }
        return Fallback;
    }

    /// <summary>True for membranes etc. whose automatic hatch is none.</summary>
    public static bool IsMembrane(string description) =>
        ResolvePattern(Auto, description) is null;
}
