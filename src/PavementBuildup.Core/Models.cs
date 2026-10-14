namespace PavementBuildup.Core;

public sealed class PavementLayer
{
    public string Description { get; set; } = "";

    public double ThicknessMm { get; set; }

    public int Lifts { get; set; } = 1;

    [System.Text.Json.Serialization.JsonIgnore]
    public double TotalMm => Math.Max(0, ThicknessMm) * Math.Max(1, Lifts);

    public string Role { get; set; } = "";

    public string HatchPattern { get; set; } = MaterialLibrary.Auto;

    public double HatchScale { get; set; } = 1.0;

    public double? HatchAngle { get; set; }

    public List<Reinforcement> Reinforcement { get; set; } = new();

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

public sealed class DetailSettings
{
    public double WidthMm { get; set; } = 1000;

    public double ScaleDenominator { get; set; } = 10;

    public double HatchScaleMultiplier { get; set; } = 1.0;

    public double SubgradeDepthMm { get; set; } = 150;

    public string UnitsOverride { get; set; } = DetailLayout.UseStandardUnits;

    public bool CreateBlock { get; set; } = true;
    public bool ShowDimensions { get; set; } = true;
    public bool ShowBreakLines { get; set; } = true;
    public bool ShowTitle { get; set; } = true;

    public string StandardPath { get; set; } = "";

    public DetailSettings Clone() => (DetailSettings)MemberwiseClone();
}
