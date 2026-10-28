using System.Text.Json.Serialization;

namespace ETWalls.Core.Design;

public enum WallType { Cantilever, Block, Gravity, Post }

/// <summary>Where a cantilever wall's footing goes: under the retained soil (heel) or in front only (no heel).</summary>
public enum Footing { Heel, Toe }

/// <summary>Cut: digging out behind the wall (paid excavation and backfill). Fill: building up behind it.</summary>
public enum Site { Cut, Fill }

/// <summary>
/// Everything one wall design needs, in m, kN, kPa, MPa and degrees. Field names follow the preview page
/// (ET Wall Tool preview.html) so the numbers can be compared one for one.
/// </summary>
public sealed class WallInput
{
    // Project
    public string Project { get; set; } = "";
    public string JobNumber { get; set; } = "";
    public string WallName { get; set; } = "";
    public string DesignedBy { get; set; } = "";
    public string CheckedBy { get; set; } = "";

    // Wall
    [JsonConverter(typeof(JsonStringEnumConverter))] public WallType Type { get; set; } = WallType.Cantilever;
    [JsonConverter(typeof(JsonStringEnumConverter))] public Footing Footing { get; set; } = Footing.Heel;
    [JsonConverter(typeof(JsonStringEnumConverter))] public Site Site { get; set; } = Site.Cut;
    /// <summary>Drawing only: mirror the section so the wall faces right.</summary>
    public bool FacesRight { get; set; }
    public double H { get; set; } = 2.4;       // retained height
    public double Slope { get; set; }          // backfill slope, degrees
    public double Length { get; set; } = 25;   // along the wall, for the total cost
    public double D { get; set; } = 0.5;       // underside of footing below front ground
    public double Tb { get; set; } = 0.35;     // base thickness
    public double Toe { get; set; } = 0.6;
    public double Heel { get; set; } = 1.5;
    public double T1 { get; set; } = 0.2;      // stem top
    public double T2 { get; set; } = 0.25;     // stem bottom
    public double Bg { get; set; } = 1.4;      // gravity / block base width
    public double Bt { get; set; } = 0.6;      // gravity / block top width
    public double GammaWall { get; set; } = 22;
    public double S { get; set; } = 2.4;       // post spacing
    public double Dia { get; set; } = 0.6;     // pier diameter
    public double Dp { get; set; } = 2.8;      // pier embedment
    public string Post { get; set; } = "200UC46";

    // Soil and loads
    public double Phi { get; set; } = 30;
    public double Gamma { get; set; } = 19;
    public double PhiF { get; set; } = 28;
    public double GammaF { get; set; } = 19;
    public double CF { get; set; }
    public double Delta { get; set; } = 28;
    public double Qa { get; set; } = 150;
    public double Q { get; set; } = 10;
    public bool Drained { get; set; } = true;
    public double Hw { get; set; } = 1.0;
    public double Kh { get; set; }
    public bool UsePassive { get; set; }

    // Concrete
    public double Fc { get; set; } = 32;
    public double Cover { get; set; } = 50;
    /// <summary>0 = automatic (lightest bar that works), otherwise 12, 16, 20 or 24.</summary>
    public int Bar { get; set; }

    // Sizing and cost
    /// <summary>True: the sizes above were set by hand. False: they are chosen by the cost search.</summary>
    public bool Custom { get; set; }
    /// <summary>Highest use allowed on any check when sizing automatically (1 = 100%).</summary>
    public double Margin { get; set; } = 1;
    public Rates Rates { get; set; } = new();

    /// <summary>Internal: skip the reinforcement design (used while searching sizes).</summary>
    [JsonIgnore] public bool NoReo { get; set; }

    public WallInput Clone()
    {
        var c = (WallInput)MemberwiseClone();
        c.Rates = Rates.Clone();
        return c;
    }

    /// <summary>The footing setting only applies to cantilever walls; a front-only footing has no heel.</summary>
    public double HeelUsed => Type == WallType.Cantilever && Footing == Footing.Toe ? 0 : Heel;

    public double BaseWidth => Type switch
    {
        WallType.Cantilever => Toe + T2 + HeelUsed,
        WallType.Post => Dia,
        _ => Bg,
    };
}

/// <summary>Cost rates (AUD, supplied and installed). The defaults are placeholders: use the office's own.</summary>
public sealed class Rates
{
    public double Concrete { get; set; } = 420;    // $/m3
    public double Steel { get; set; } = 3.8;       // $/kg reinforcement
    public double Formwork { get; set; } = 110;    // $/m2
    public double Excavation { get; set; } = 55;   // $/m3
    public double Backfill { get; set; } = 45;     // $/m3
    public double Drainage { get; set; } = 95;     // $/m3
    public double AgPipe { get; set; } = 30;       // $/m
    public double Blocks { get; set; } = 650;      // $/m3 of block wall
    public double PostSteel { get; set; } = 6.5;   // $/kg
    public double Drilling { get; set; } = 110;    // $/m of 600 mm pier
    public double Sleepers { get; set; } = 120;    // $/m2

    public Rates Clone() => (Rates)MemberwiseClone();
    public static Rates Defaults => new();
}

/// <summary>One check: value against its limit. Util = demand / capacity; 1 or less passes.</summary>
public sealed record Check(string Id, string Name, double Value, double Limit, string Unit, double Util, string Note)
{
    public bool Pass => Util <= 1 + 1e-9;
    /// <summary>Id without the " (earthquake)" suffix.</summary>
    public string BaseId => Id.Replace(" (earthquake)", "");
}
