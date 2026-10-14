using System.Globalization;
using System.Text.RegularExpressions;

namespace PavementBuildup.Core;

public enum DetailElement
{
    Outline,
    Hatch,
    Membrane,
    Leader,
    Text,
    Dimension,
    Title,
    Reinforcement,
    ExistingHatch,
    ExistingOutline,
}

public sealed class LayerStyle
{
    public DetailElement Element { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "7";
    public string Linetype { get; set; } = "Continuous";
    public double LineWeightMm { get; set; } = -1;
    public bool Plot { get; set; } = true;

    public LayerStyle Clone() => (LayerStyle)MemberwiseClone();
}

public sealed class HatchRule
{
    public string Name { get; set; } = "";
    public string Keywords { get; set; } = "";
    public string Pattern { get; set; } = "ANSI31";
    public double Scale { get; set; } = 0.5;
    public double Angle { get; set; }
    public string Layer { get; set; } = "";
    public string Color { get; set; } = "BYLAYER";
    public string BackgroundColor { get; set; } = "";
    public int Transparency { get; set; } = -1;
    public double LineWeightMm { get; set; } = -1;

    public IEnumerable<string> KeywordList() =>
        Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => k.ToLowerInvariant());

    public bool Matches(string description)
    {
        var text = " " + description.ToLowerInvariant() + " ";
        return KeywordList().Any(k => StartsAWord(text, k));
    }

    private static bool StartsAWord(string text, string keyword)
    {
        bool shortWord = keyword.Count(char.IsLetter) <= 3;
        for (int i = text.IndexOf(keyword, StringComparison.Ordinal); i >= 0; i = text.IndexOf(keyword, i + 1, StringComparison.Ordinal))
        {
            bool starts = i == 0 || !char.IsLetter(text[i - 1]);
            int end = i + keyword.Length;
            bool ends = !shortWord || end >= text.Length || !char.IsLetter(text[end]);
            if (starts && ends)
                return true;
        }
        return false;
    }

    public bool IsNone => string.Equals(Pattern?.Trim(), MaterialLibrary.None, StringComparison.OrdinalIgnoreCase);

    public HatchRule Clone() => (HatchRule)MemberwiseClone();
}

public sealed class CadStandard
{
    public string Name { get; set; } = "Default";

    public string DrawingUnits { get; set; } = "M";

    public string DetailNameFormat { get; set; } = DetailNaming.DefaultFormat;

    public string StandardDrawing { get; set; } = "";

    public const string DefaultTemplate = "TTW_Pave_Temp.dwg";

    [System.Text.Json.Serialization.JsonIgnore]
    public string SourceFolder { get; set; } = "";

    public string? ResolveStandardDrawing()
    {
        var name = (StandardDrawing ?? "").Trim();
        var candidates = new List<string>();
        if (name.Length > 0)
        {
            candidates.Add(name);
            if (!Path.IsPathRooted(name) && SourceFolder.Length > 0) candidates.Insert(0, Path.Combine(SourceFolder, name));
        }
        else if (SourceFolder.Length > 0)
            candidates.Add(Path.Combine(SourceFolder, DefaultTemplate));
        var found = candidates.FirstOrDefault(File.Exists);
        return found ?? (name.Length > 0 ? candidates[0] : null);
    }

    public string AiPromptFile { get; set; } = "";

    public string BreakLineMethod { get; set; } = BreakLineCommand;
    public const string BreakLineCommand = "BREAKLINE";
    public const string BreakLineBuiltIn = "BUILTIN";

    public string BreakLineBlock { get; set; } = "TTW_stdCountry_Block_Break";

    public double BreakLineSizeMm { get; set; } = 5;

    public double BreakLineExtensionMm { get; set; }

    public List<LayerStyle> Layers { get; set; } = new();

    public string TextStyle { get; set; } = "";
    public string DimensionStyle { get; set; } = "";

    public double TextHeightMm { get; set; } = 2.5;
    public double TitleHeightMm { get; set; } = 3.5;
    public double ScaleHeightMm { get; set; } = 2.5;
    public double DimensionTextHeightMm { get; set; } = 2.5;
    public double DimensionArrowSizeMm { get; set; } = 2.0;
    public bool UpperCaseLabels { get; set; } = true;
    public int LabelWrapChars { get; set; } = 60;
    public double MembraneWidthMm { get; set; } = 0.35;

    public string LabelFormat { get; set; } = "{thickness}mm {description}";
    public string MembraneLabelFormat { get; set; } = "{description}";
    public string DimensionFormat { get; set; } = "{thickness}mm";
    public string TitleFormat { get; set; } = "{name}";
    public string ScaleFormat { get; set; } = "SCALE 1:{scale}";
    public string TotalFormat { get; set; } = "TOTAL CONSTRUCTION DEPTH = {total}mm";

    public string SubgradeNote { get; set; } = "SUBGRADE COMPACTED TO 98% SMDD AT ±2% OMC";

    public string BarPrefix { get; set; } = "H";
    public string BarFormat { get; set; } = "{prefix}{diameter} @ {spacing} c/c";
    public string ReinforcementLabelFormat { get; set; } = "{bars} {face} + {transverse} - {cover}mm COVER";
    public string TopFaceText { get; set; } = "TOP";
    public string BottomFaceText { get; set; } = "BTM";
    public bool ShowCoverDimension { get; set; } = true;
    public string CoverDimensionFormat { get; set; } = "{cover}";

    public List<HatchRule> HatchRules { get; set; } = new();
    public HatchRule DefaultHatch { get; set; } = new() { Name = "Default", Pattern = "ANSI31", Scale = 0.5 };
    public HatchRule SubgradeHatch { get; set; } = new() { Name = "Subgrade", Pattern = "EARTH", Scale = 0.5, Angle = 45 };

    public LayerStyle Layer(DetailElement element) =>
        Layers.FirstOrDefault(l => l.Element == element) ?? DefaultLayer(element);

    public HatchRule RuleFor(string description) =>
        HatchRules.FirstOrDefault(r => r.Matches(description)) ?? DefaultHatch;

    public HatchRule RuleForPattern(string pattern) =>
        HatchRules.Append(DefaultHatch).Append(SubgradeHatch)
            .FirstOrDefault(r => string.Equals(r.Pattern, pattern, StringComparison.OrdinalIgnoreCase))
        ?? new HatchRule { Name = pattern, Pattern = pattern.ToUpperInvariant(), Scale = 1.0 };

    public IEnumerable<string> AllLayerNames() =>
        Enum.GetValues<DetailElement>().Select(e => Layer(e).Name)
            .Concat(HatchRules.Append(DefaultHatch).Append(SubgradeHatch).Select(r => r.Layer))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase);

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
            if (r.Transparency is < -1 or > 90)
                errors.Add($"{who}: transparency must be 0-90 % (or -1 for by layer).");
            if (!string.IsNullOrWhiteSpace(r.BackgroundColor) && !ColorSpec.TryParse(r.BackgroundColor, out _))
                errors.Add($"{who}: background colour \"{r.BackgroundColor}\" is not valid.");
        }
        foreach (var r in HatchRules.Where(r => !r.KeywordList().Any()))
            errors.Add($"Hatch rule \"{r.Name}\" has no keywords, so it never matches.");

        if (!(TextHeightMm > 0) || !(TitleHeightMm > 0) || !(ScaleHeightMm > 0) || !(DimensionTextHeightMm > 0) || !(DimensionArrowSizeMm > 0))
            errors.Add("Text heights must be greater than zero.");
        if (!DetailLayout.StandardUnitChoices.Contains((DrawingUnits ?? "").Trim().ToUpperInvariant()))
            errors.Add($"Drawing units \"{DrawingUnits}\" must be one of {string.Join(", ", DetailLayout.StandardUnitChoices)}.");
        if (!string.Equals(BreakLineMethod, BreakLineCommand, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(BreakLineMethod, BreakLineBuiltIn, StringComparison.OrdinalIgnoreCase))
            errors.Add($"Break line method must be {BreakLineCommand} or {BreakLineBuiltIn}.");
        if (string.Equals(BreakLineMethod, BreakLineCommand, StringComparison.OrdinalIgnoreCase))
        {
            if (!IsValidSymbolName(BreakLineBlock))
                errors.Add($"Break line block \"{BreakLineBlock}\" is not a valid block name.");
            if (!(BreakLineSizeMm > 0))
                errors.Add("Break line size must be greater than zero.");
            if (BreakLineExtensionMm < 0)
                errors.Add("Break line extension cannot be negative.");
        }
        if (BarFormat is null || !BarFormat.Contains("{diameter}", StringComparison.OrdinalIgnoreCase))
            errors.Add("Bar format must include {diameter}.");
        if (MembraneWidthMm < 0)
            errors.Add("Membrane line width cannot be negative.");
        return errors;
    }

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
        return Regex.Replace(text, @"\s{2,}", " ").Trim();
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
        DetailElement.ExistingHatch => new() { Element = e, Name = "PEN-GREY-H", Color = "8", LineWeightMm = 0.13 },
        DetailElement.ExistingOutline => new() { Element = e, Name = "PEN-GREY-C", Color = "8", LineWeightMm = 0.25 },
        _ => new() { Element = e, Name = "PAV-" + e.ToString().ToUpperInvariant() },
    };

    public static CadStandard CreateDefault() => new CadStandard
    {
        Name = "Default",
        HatchRules = new()
        {
            Rule("Membrane", "geotextile, geogrid, geocomposite, membrane, dpm, separator, tack, bond coat, spray", "NONE", 0),
            Rule("Block paving", "block, paver, sett, flag, brick, cobble", "ANSI37", 0.5),
            Rule("Recycled aggregate", "crushed concrete, recycled concrete, recycled aggregate, recycled sub-base, rca, crushed brick", "GRAVEL", 0.15),
            Rule("Concrete / bound", "concrete, pqc, crcp, jpcp, urcp, cbgm, cbm, hbm, lean mix, leanmix, cement, c8/10, c32/40, c40/50, ctb, ctcr, cement treated, cement stabilised, cement stabilized, mpa, lime stabil, stabilised, stabilized", "AR-CONC", 0.08),
            Rule("Bedding / laying course", "bedding, laying course, sand, mortar, screed, grout", "AR-SAND", 0.1),
            Rule("Soils", "topsoil, subgrade, clay, earth, soil, formation, select fill, fill material", "EARTH", 0.5, 45),
            Rule("Granular", "type 1, type1, type 2, type 3, type 4, mot, sub-base, subbase, sub base, granular, crushed, aggregate, capping, 6f, hardcore, stone, gravel, rubble, scalpings, dgb, dgs, crushed rock, road base, roadbase, sub base, fcr, class 1, class 2, class 3, class 4, quarry, crusher dust, gravel", "GRAVEL", 0.15),
            Rule("Asphalt surface", "sma, hra, surface, wearing, thin surf, porous, pa , chip, hot mix, hotmix, dga, dense graded asphalt, ogfc, open graded, ac5, ac7, ac10, ac14, ac 5, ac 7, ac 10, ac 14", "AR-SAND", 0.15),
            Rule("Asphalt binder", "binder", "AR-SAND", 0.3),
            Rule("Asphalt base", "dbm, hdm, ac 32, ac32, base, macadam, asphalt, bitum, tarmac, emac", "ANSI31", 0.5),
        },
    }.Normalize();

    private static HatchRule Rule(string name, string keywords, string pattern, double scale, double angle = 0) =>
        new() { Name = name, Keywords = keywords, Pattern = pattern, Scale = scale, Angle = angle };
}

public enum ColorKind { ByLayer, ByBlock, Index, Rgb }

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
