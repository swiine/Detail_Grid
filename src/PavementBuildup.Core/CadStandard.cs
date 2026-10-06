using System.Globalization;
using System.Text.RegularExpressions;

namespace PavementBuildup.Core;

/// <summary>The parts of a detail that each get their own CAD layer.</summary>
public enum DetailElement
{
    Outline,    // course interfaces and side edges
    Hatch,      // default layer for hatches (a hatch rule may override it)
    Membrane,   // zero-thickness courses: geotextile, DPM, slip membrane
    Leader,     // label leader lines and dots
    Text,       // course labels
    Dimension,  // thickness dimensions
    Title,      // title, scale and total depth
    Reinforcement, // bars (dots) and transverse bars (lines)
}

/// <summary>Layer name and properties for one <see cref="DetailElement"/>.</summary>
public sealed class LayerStyle
{
    public DetailElement Element { get; set; }
    public string Name { get; set; } = "";
    /// <summary>ACI number ("7"), "R,G,B" true colour.</summary>
    public string Color { get; set; } = "7";
    public string Linetype { get; set; } = "Continuous";
    /// <summary>Lineweight in mm (0.35); negative means the AutoCAD default.</summary>
    public double LineWeightMm { get; set; } = -1;
    public bool Plot { get; set; } = true;

    public LayerStyle Clone() => (LayerStyle)MemberwiseClone();
}

/// <summary>
/// "When a course description contains one of these keywords, hatch it like this."
/// Rules are checked top to bottom and the first match wins.
/// </summary>
public sealed class HatchRule
{
    public string Name { get; set; } = "";
    /// <summary>Comma-separated, case-insensitive keywords, e.g. "type 1, sub-base, granular".</summary>
    public string Keywords { get; set; } = "";
    /// <summary>Pattern name from acadiso.pat, a custom .pat on the support path, SOLID or NONE (unhatched).</summary>
    public string Pattern { get; set; } = "ANSI31";
    /// <summary>Pattern scale for a 1:1 detail in a millimetre drawing; multiplied by the detail scale.</summary>
    public double Scale { get; set; } = 0.5;
    public double Angle { get; set; }
    /// <summary>Layer for this hatch. Blank uses the Hatch element layer.</summary>
    public string Layer { get; set; } = "";
    /// <summary>"BYLAYER", "BYBLOCK", an ACI number or "R,G,B".</summary>
    public string Color { get; set; } = "BYLAYER";
    /// <summary>Hatch background fill colour; blank for none.</summary>
    public string BackgroundColor { get; set; } = "";

    public IEnumerable<string> KeywordList() =>
        Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => k.ToLowerInvariant());

    public bool Matches(string description)
    {
        var text = " " + description.ToLowerInvariant() + " ";
        return KeywordList().Any(k => text.Contains(k, StringComparison.Ordinal));
    }

    public bool IsNone => string.Equals(Pattern?.Trim(), MaterialLibrary.None, StringComparison.OrdinalIgnoreCase);

    public HatchRule Clone() => (HatchRule)MemberwiseClone();
}

/// <summary>A company CAD standard: what layers, hatches, styles and wording the detail uses.</summary>
public sealed class CadStandard
{
    public string Name { get; set; } = "Default";

    /// <summary>
    /// Units the company draws in: M (1 unit = 1 metre, so 40mm draws as 0.04), MM, CM, or AUTO (read INSUNITS).
    /// Thicknesses are always typed in millimetres.
    /// </summary>
    public string DrawingUnits { get; set; } = "M";

    /// <summary>
    /// Name for each detail's block (or group): '#' is replaced by the next free number, {name} by the
    /// build-up name. E.g. "TTW_pavement-profile_#" gives TTW_pavement-profile_1, _2, ...
    /// </summary>
    public string DetailNameFormat { get; set; } = DetailNaming.DefaultFormat;

    public List<LayerStyle> Layers { get; set; } = new();

    /// <summary>Text style name; blank or missing uses the drawing's current style.</summary>
    public string TextStyle { get; set; } = "";
    /// <summary>Dimension style name; blank or missing uses the drawing's current style.</summary>
    public string DimensionStyle { get; set; } = "";

    public double TextHeightMm { get; set; } = 2.5;
    public double TitleHeightMm { get; set; } = 3.5;
    public bool UpperCaseLabels { get; set; } = true;
    /// <summary>Plotted width of membrane lines (mm); 0 for a plain line.</summary>
    public double MembraneWidthMm { get; set; } = 0.35;

    /// <summary>Course label. Tokens: {thickness} {description}.</summary>
    public string LabelFormat { get; set; } = "{thickness}mm {description}";
    /// <summary>Label for zero-thickness courses. Tokens: {description}.</summary>
    public string MembraneLabelFormat { get; set; } = "{description}";
    /// <summary>Thickness dimension text. Tokens: {thickness}.</summary>
    public string DimensionFormat { get; set; } = "{thickness}mm";
    /// <summary>Title lines. Tokens: {name} {scale} {total}. Blank lines are skipped.</summary>
    public string TitleFormat { get; set; } = "{name}";
    public string ScaleFormat { get; set; } = "SCALE 1:{scale}";
    public string TotalFormat { get; set; } = "TOTAL CONSTRUCTION DEPTH = {total}mm";

    /// <summary>Bar type prefix when none is typed, e.g. "H" (UK B500), "N" (AU), "T", "Ø".</summary>
    public string BarPrefix { get; set; } = "H";
    /// <summary>One set of bars. Tokens: {prefix} {diameter} {spacing}.</summary>
    public string BarFormat { get; set; } = "{prefix}{diameter} @ {spacing} c/c";
    /// <summary>Reinforcement label. Tokens: {bars} {transverse} {face} {cover}. Lines with an empty {transverse} drop the " + ".</summary>
    public string ReinforcementLabelFormat { get; set; } = "{bars} {face} + {transverse} - {cover}mm COVER";
    public string TopFaceText { get; set; } = "TOP";
    public string BottomFaceText { get; set; } = "BTM";
    /// <summary>Dimension the cover from the face to the bars.</summary>
    public bool ShowCoverDimension { get; set; } = true;
    /// <summary>Cover dimension text. Tokens: {cover}.</summary>
    public string CoverDimensionFormat { get; set; } = "{cover}";

    public List<HatchRule> HatchRules { get; set; } = new();
    /// <summary>Used when no rule matches a course.</summary>
    public HatchRule DefaultHatch { get; set; } = new() { Name = "Default", Pattern = "ANSI31", Scale = 0.5 };
    /// <summary>Used for the subgrade strip.</summary>
    public HatchRule SubgradeHatch { get; set; } = new() { Name = "Subgrade", Pattern = "EARTH", Scale = 0.5, Angle = 45 };

    public LayerStyle Layer(DetailElement element) =>
        Layers.FirstOrDefault(l => l.Element == element) ?? DefaultLayer(element);

    /// <summary>The first rule whose keywords appear in the description, else <see cref="DefaultHatch"/>.</summary>
    public HatchRule RuleFor(string description) =>
        HatchRules.FirstOrDefault(r => r.Matches(description)) ?? DefaultHatch;

    /// <summary>Settings for an explicit pattern: a rule using that pattern if there is one, else a plain rule.</summary>
    public HatchRule RuleForPattern(string pattern) =>
        HatchRules.Append(DefaultHatch).Append(SubgradeHatch)
            .FirstOrDefault(r => string.Equals(r.Pattern, pattern, StringComparison.OrdinalIgnoreCase))
        ?? new HatchRule { Name = pattern, Pattern = pattern.ToUpperInvariant(), Scale = 1.0 };

    /// <summary>Every layer name the standard can put objects on.</summary>
    public IEnumerable<string> AllLayerNames() =>
        Enum.GetValues<DetailElement>().Select(e => Layer(e).Name)
            .Concat(HatchRules.Append(DefaultHatch).Append(SubgradeHatch).Select(r => r.Layer))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fills in anything missing (e.g. a layer row deleted from the JSON by hand).</summary>
    public CadStandard Normalize()
    {
        Layers ??= new();
        HatchRules ??= new();
        DefaultHatch ??= new() { Name = "Default", Pattern = "ANSI31", Scale = 0.5 };
        SubgradeHatch ??= new() { Name = "Subgrade", Pattern = "EARTH", Scale = 0.5, Angle = 45 };
        foreach (var e in Enum.GetValues<DetailElement>())
            if (Layers.All(l => l.Element != e))
                Layers.Add(DefaultLayer(e));
        Layers = Layers.OrderBy(l => l.Element).ToList();
        return this;
    }

    /// <summary>Problems that would stop the detail being drawn properly. Empty when the standard is usable.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        foreach (var l in Layers)
        {
            if (!IsValidSymbolName(l.Name))
                errors.Add($"Layer for {l.Element}: \"{l.Name}\" is not a valid layer name.");
            if (!ColorSpec.TryParse(l.Color, out var c) || c.Kind is ColorKind.ByLayer or ColorKind.ByBlock)
                errors.Add($"Layer {l.Name}: colour \"{l.Color}\" must be 1-255 or R,G,B.");
            if (string.IsNullOrWhiteSpace(l.Linetype))
                errors.Add($"Layer {l.Name}: linetype is blank.");
        }

        foreach (var r in HatchRules.Append(DefaultHatch).Append(SubgradeHatch))
        {
            string who = $"Hatch \"{(string.IsNullOrWhiteSpace(r.Name) ? r.Pattern : r.Name)}\"";
            if (string.IsNullOrWhiteSpace(r.Pattern))
                errors.Add($"{who}: pattern is blank (use NONE for no hatch).");
            if (!r.IsNone && !(r.Scale > 0))
                errors.Add($"{who}: scale must be greater than zero.");
            if (!string.IsNullOrWhiteSpace(r.Layer) && !IsValidSymbolName(r.Layer))
                errors.Add($"{who}: \"{r.Layer}\" is not a valid layer name.");
            if (!ColorSpec.TryParse(r.Color, out _))
                errors.Add($"{who}: colour \"{r.Color}\" is not BYLAYER, BYBLOCK, 1-255 or R,G,B.");
            if (!string.IsNullOrWhiteSpace(r.BackgroundColor) && !ColorSpec.TryParse(r.BackgroundColor, out _))
                errors.Add($"{who}: background colour \"{r.BackgroundColor}\" is not valid.");
        }
        foreach (var r in HatchRules.Where(r => !r.KeywordList().Any()))
            errors.Add($"Hatch rule \"{r.Name}\" has no keywords, so it never matches.");

        if (!(TextHeightMm > 0) || !(TitleHeightMm > 0))
            errors.Add("Text heights must be greater than zero.");
        if (!DetailLayout.StandardUnitChoices.Contains((DrawingUnits ?? "").Trim().ToUpperInvariant()))
            errors.Add($"Drawing units \"{DrawingUnits}\" must be one of {string.Join(", ", DetailLayout.StandardUnitChoices)}.");
        if (BarFormat is null || !BarFormat.Contains("{diameter}", StringComparison.OrdinalIgnoreCase))
            errors.Add("Bar format must include {diameter}.");
        if (MembraneWidthMm < 0)
            errors.Add("Membrane line width cannot be negative.");
        return errors;
    }

    // AutoCAD symbol names: up to 255 chars, none of <>/\":;?*|,='`
    private static readonly Regex InvalidSymbolChars = new(@"[<>/\\"":;?*|,='`]");

    public static bool IsValidSymbolName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 255 && !InvalidSymbolChars.IsMatch(name) && name.Trim() == name;

    public static string Fill(string format, params (string Token, string Value)[] values)
    {
        var s = format ?? "";
        foreach (var (token, value) in values)
            s = s.Replace("{" + token + "}", value, StringComparison.OrdinalIgnoreCase);
        return s.Trim();
    }

    /// <summary>Label for one reinforcement mat, e.g. "H16 @ 150 c/c BTM + H10 @ 300 c/c - 50mm COVER".</summary>
    public string ReinforcementLabel(Reinforcement r)
    {
        string prefix = string.IsNullOrWhiteSpace(r.Prefix) ? BarPrefix ?? "" : r.Prefix;
        string Bars(double dia, double spacing) =>
            Fill(BarFormat ?? "{prefix}{diameter} @ {spacing} c/c", ("prefix", prefix), ("diameter", Number(dia)), ("spacing", Number(spacing)));

        string format = ReinforcementLabelFormat ?? "";
        string transverse = r.HasTransverse ? Bars(r.TransverseDiameterMm, r.TransverseSpacingMm) : "";
        if (transverse.Length == 0)
            format = Regex.Replace(format, @"\s*(?:\+|&|and)\s*\{transverse\}", "", RegexOptions.IgnoreCase);

        string text = Fill(format,
            ("bars", Bars(r.DiameterMm, r.SpacingMm)),
            ("transverse", transverse),
            ("face", r.Face == BarFace.Top ? TopFaceText ?? "" : BottomFaceText ?? ""),
            ("cover", Number(r.CoverMm)));
        return Regex.Replace(text, @"\s{2,}", " ").Trim(); // not upper-cased: "c/c" stays lower case
    }

    public static string Number(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    public CadStandard Clone()
    {
        var c = (CadStandard)MemberwiseClone();
        c.Layers = Layers.Select(l => l.Clone()).ToList();
        c.HatchRules = HatchRules.Select(r => r.Clone()).ToList();
        c.DefaultHatch = DefaultHatch.Clone();
        c.SubgradeHatch = SubgradeHatch.Clone();
        return c;
    }

    private static LayerStyle DefaultLayer(DetailElement e) => e switch
    {
        DetailElement.Outline => new() { Element = e, Name = "PAV-OUTLINE", Color = "7", LineWeightMm = 0.35 },
        DetailElement.Hatch => new() { Element = e, Name = "PAV-HATCH", Color = "8", LineWeightMm = 0.13 },
        DetailElement.Membrane => new() { Element = e, Name = "PAV-MEMBRANE", Color = "1", LineWeightMm = 0.35 },
        DetailElement.Leader => new() { Element = e, Name = "PAV-TEXT", Color = "2", LineWeightMm = 0.18 },
        DetailElement.Text => new() { Element = e, Name = "PAV-TEXT", Color = "2", LineWeightMm = 0.18 },
        DetailElement.Dimension => new() { Element = e, Name = "PAV-DIM", Color = "3", LineWeightMm = 0.18 },
        DetailElement.Title => new() { Element = e, Name = "PAV-TITLE", Color = "4", LineWeightMm = 0.25 },
        DetailElement.Reinforcement => new() { Element = e, Name = "PAV-REBAR", Color = "1", LineWeightMm = 0.35 },
        _ => new() { Element = e, Name = "PAV-" + e.ToString().ToUpperInvariant() },
    };

    /// <summary>The built-in standard (also what a new standard file starts as).</summary>
    public static CadStandard CreateDefault() => new CadStandard
    {
        Name = "Default",
        HatchRules = new()
        {
            Rule("Membrane", "geotextile, geogrid, geocomposite, membrane, dpm, separator, tack, bond coat, spray", "NONE", 0),
            Rule("Block paving", "block, paver, sett, flag", "ANSI37", 0.5),
            Rule("Concrete / bound", "concrete, pqc, crcp, jpcp, urcp, cbgm, cbm, hbm, lean mix, leanmix, cement, c8/10, c32/40, c40/50", "AR-CONC", 0.08),
            Rule("Bedding / laying course", "bedding, laying course, sand", "AR-SAND", 0.1),
            Rule("Soils", "topsoil, subgrade, clay, earth, soil, formation", "EARTH", 0.5, 45),
            Rule("Granular", "type 1, type1, type 2, type 3, type 4, mot, sub-base, subbase, sub base, granular, crushed, aggregate, capping, 6f, hardcore, stone, gravel, rubble, scalpings", "GRAVEL", 0.15),
            Rule("Asphalt surface", "sma, hra, surface, wearing, thin surf, porous, pa , chip", "AR-SAND", 0.15),
            Rule("Asphalt binder", "binder", "AR-SAND", 0.3),
            Rule("Asphalt base", "dbm, hdm, ac 32, ac32, base, macadam, asphalt, bitum, tarmac, emac", "ANSI31", 0.5),
        },
    }.Normalize();

    private static HatchRule Rule(string name, string keywords, string pattern, double scale, double angle = 0) =>
        new() { Name = name, Keywords = keywords, Pattern = pattern, Scale = scale, Angle = angle };
}

public enum ColorKind { ByLayer, ByBlock, Index, Rgb }

/// <summary>A colour as written in a standard: BYLAYER, BYBLOCK, ACI 1-255, or "R,G,B".</summary>
public readonly record struct ColorSpec(ColorKind Kind, short Index, byte R, byte G, byte B)
{
    public static bool TryParse(string? text, out ColorSpec color)
    {
        color = new ColorSpec(ColorKind.ByLayer, 256, 0, 0, 0);
        var s = (text ?? "").Trim();
        if (s.Length == 0 || s.Equals("BYLAYER", StringComparison.OrdinalIgnoreCase))
            return true;
        if (s.Equals("BYBLOCK", StringComparison.OrdinalIgnoreCase))
        {
            color = new ColorSpec(ColorKind.ByBlock, 0, 0, 0, 0);
            return true;
        }
        if (short.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var aci))
        {
            if (aci is < 1 or > 255) return false;
            color = new ColorSpec(ColorKind.Index, aci, 0, 0, 0);
            return true;
        }
        var parts = s.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 3 && parts.All(p => byte.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            color = new ColorSpec(ColorKind.Rgb, -1, byte.Parse(parts[0], CultureInfo.InvariantCulture),
                byte.Parse(parts[1], CultureInfo.InvariantCulture), byte.Parse(parts[2], CultureInfo.InvariantCulture));
            return true;
        }
        return false;
    }

    public override string ToString() => Kind switch
    {
        ColorKind.ByLayer => "BYLAYER",
        ColorKind.ByBlock => "BYBLOCK",
        ColorKind.Index => Index.ToString(CultureInfo.InvariantCulture),
        _ => $"{R},{G},{B}",
    };
}
