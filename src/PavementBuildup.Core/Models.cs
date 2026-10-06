namespace PavementBuildup.Core;

/// <summary>One course of the pavement, listed top (surface) to bottom.</summary>
public sealed class PavementLayer
{
    /// <summary>Material / course description, e.g. "SMA 10 SURFACE COURSE".</summary>
    public string Description { get; set; } = "";

    /// <summary>Thickness in millimetres (of each layer when <see cref="Lifts"/> &gt; 1). Zero means a membrane drawn as a line.</summary>
    public double ThicknessMm { get; set; }

    /// <summary>Number of equal layers ("2 x 150mm ... ROAD BASE"). Drawn as one course with a line between layers.</summary>
    public int Lifts { get; set; } = 1;

    /// <summary>Full depth of the course: thickness x layers.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double TotalMm => Math.Max(0, ThicknessMm) * Math.Max(1, Lifts);

    /// <summary>Hatch pattern name. Empty or "AUTO" picks one from the description; "NONE" leaves it unhatched.</summary>
    public string HatchPattern { get; set; } = MaterialLibrary.Auto;

    /// <summary>Multiplier on the automatic hatch scale for this layer.</summary>
    public double HatchScale { get; set; } = 1.0;

    /// <summary>Hatch angle in degrees. Null uses the material default.</summary>
    public double? HatchAngle { get; set; }

    /// <summary>Reinforcement mats in this course (at most one top and one bottom). Empty = unreinforced.</summary>
    public List<Reinforcement> Reinforcement { get; set; } = new();

    /// <summary>The reinforcement as short text ("H16@150 c50"), for the course table. Setting it parses the text.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ReinforcementText
    {
        get => ReinforcementParser.Format(Reinforcement);
        set => Reinforcement = ReinforcementParser.Parse(value);
    }

    public PavementLayer Clone()
    {
        var c = (PavementLayer)MemberwiseClone();
        c.Reinforcement = (Reinforcement ?? new()).Select(r => r.Clone()).ToList();
        return c;
    }
}

/// <summary>A named pavement build-up (e.g. "TYPE A - CARRIAGEWAY").</summary>
public sealed class Buildup
{
    public string Name { get; set; } = "PAVEMENT BUILD-UP";
    public List<PavementLayer> Layers { get; set; } = new();

    public bool ShowSubgrade { get; set; } = true;
    public string SubgradeText { get; set; } = "SUBGRADE";

    public double TotalThicknessMm => Layers.Sum(l => l.TotalMm);

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

    /// <summary>
    /// Drawing units for this drawing: "STANDARD" (use the CAD standard's units), "AUTO" (read INSUNITS), MM, CM or M.
    /// Build-up thicknesses are always typed in millimetres; this only sets how big 1 mm is in the drawing.
    /// </summary>
    public string UnitsOverride { get; set; } = DetailLayout.UseStandardUnits;

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
