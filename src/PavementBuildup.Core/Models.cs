namespace PavementBuildup.Core;

/// <summary>One course of the pavement, listed top (surface) to bottom.</summary>
public sealed class PavementLayer
{
    /// <summary>Material / course description, e.g. "SMA 10 SURFACE COURSE".</summary>
    public string Description { get; set; } = "";

    /// <summary>Thickness in millimetres. Zero means a membrane (geotextile, DPM) drawn as a line.</summary>
    public double ThicknessMm { get; set; }

    /// <summary>Hatch pattern name. Empty or "AUTO" picks one from the description; "NONE" leaves it unhatched.</summary>
    public string HatchPattern { get; set; } = MaterialLibrary.Auto;

    /// <summary>Multiplier on the automatic hatch scale for this layer.</summary>
    public double HatchScale { get; set; } = 1.0;

    /// <summary>Hatch angle in degrees. Null uses the material default.</summary>
    public double? HatchAngle { get; set; }

    public PavementLayer Clone() => (PavementLayer)MemberwiseClone();
}

/// <summary>A named pavement build-up (e.g. "TYPE A - CARRIAGEWAY").</summary>
public sealed class Buildup
{
    public string Name { get; set; } = "PAVEMENT BUILD-UP";
    public List<PavementLayer> Layers { get; set; } = new();

    public bool ShowSubgrade { get; set; } = true;
    public string SubgradeText { get; set; } = "SUBGRADE";

    public double TotalThicknessMm => Layers.Sum(l => Math.Max(0, l.ThicknessMm));

    public Buildup Clone() => new()
    {
        Name = Name,
        Layers = Layers.Select(l => l.Clone()).ToList(),
        ShowSubgrade = ShowSubgrade,
        SubgradeText = SubgradeText,
    };
}

/// <summary>Per-drawing choices. Layers, hatches, text and wording come from the <see cref="CadStandard"/>.</summary>
public sealed class DetailSettings
{
    /// <summary>Width of the section strip in real-world millimetres.</summary>
    public double WidthMm { get; set; } = 1000;

    /// <summary>Detail scale denominator, i.e. 10 for 1:10. Sizes text, dimensions and hatching.</summary>
    public double ScaleDenominator { get; set; } = 10;

    /// <summary>Global multiplier applied on top of every layer's hatch scale.</summary>
    public double HatchScaleMultiplier { get; set; } = 1.0;

    /// <summary>Subgrade strip depth in real-world millimetres.</summary>
    public double SubgradeDepthMm { get; set; } = 150;

    /// <summary>Drawing units. "AUTO" reads INSUNITS; otherwise MM, CM or M.</summary>
    public string DrawingUnits { get; set; } = "AUTO";

    public bool CreateBlock { get; set; } = true;
    public bool ShowDimensions { get; set; } = true;
    public bool ShowBreakLines { get; set; } = true;
    public bool ShowTitle { get; set; } = true;

    /// <summary>
    /// CAD standard file (layers, hatches, styles). Blank uses the personal standard in
    /// %APPDATA%\PavementBuildup\standard.json; point it at a shared drive for a company standard.
    /// </summary>
    public string StandardPath { get; set; } = "";

    public DetailSettings Clone() => (DetailSettings)MemberwiseClone();
}
