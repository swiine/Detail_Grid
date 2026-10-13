using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProfileStacker.Core;

/// <summary>
/// Everything the user sets in the dialog. Saved between sessions (minus the station range, which
/// belongs to the profile view picked each time).
/// </summary>
public sealed class StackerSettings
{
    // Sheet template
    public string TemplatePath { get; set; } = "";
    public string TemplateLayout { get; set; } = "";

    /// <summary>Only viewports whose layer contains this text are profile viewports (blank = all).</summary>
    public string ViewportLayerFilter { get; set; } = "";

    /// <summary>Use this viewport scale (paper units per drawing unit) instead of the template's. 0 = template's.</summary>
    public double ScaleOverride { get; set; }

    /// <summary>Gap kept between the viewport edge and the profile view (axes, bands, title), in paper units.</summary>
    public double PaperClearance { get; set; } = 2;

    // Stations (not saved: they come from the picked profile view)
    [JsonIgnore] public double StartStation { get; set; }
    [JsonIgnore] public double EndStation { get; set; }

    /// <summary>Alignment limits; views never run past them.</summary>
    [JsonIgnore] public double MinStation { get; set; } = double.NegativeInfinity;
    [JsonIgnore] public double MaxStation { get; set; } = double.PositiveInfinity;

    /// <summary>View lengths (and the first start) are whole multiples of this. 0 = no rounding.</summary>
    public double StationRounding { get; set; } = 10;

    /// <summary>Start the first view on a round station (StationRounding) at or before the start station.</summary>
    public bool RoundStart { get; set; } = true;

    /// <summary>Station length repeated at the start of each view from the end of the previous one.</summary>
    public double Overlap { get; set; }

    public LengthMode LengthMode { get; set; } = LengthMode.Fill;

    /// <summary>Last view ends at the end station (shorter graph) rather than keeping its full length.</summary>
    public bool TrimLastView { get; set; } = true;

    // Elevations
    /// <summary>Datum elevations are whole multiples of this. 0 = no rounding.</summary>
    public double DatumRounding { get; set; } = 1;

    public double ClearanceBelow { get; set; } = 1;
    public double ClearanceAbove { get; set; } = 1;
    public DatumMode DatumMode { get; set; } = DatumMode.LowPoint;

    // Output
    /// <summary>Tokens: &lt;N&gt; sheet number, &lt;START&gt; &lt;END&gt; first and last station on the sheet.</summary>
    public string LayoutNamePattern { get; set; } = "PROFILE <N> (<START> to <END>)";

    /// <summary>Tokens: &lt;VIEW&gt; long view name, &lt;N&gt; view number, &lt;S&gt; sheet, &lt;START&gt; &lt;END&gt;.</summary>
    public string ViewNamePattern { get; set; } = "<VIEW> - <N>";

    /// <summary>
    /// Shown after the long view's title on every new view ("MR01 - WAREHOUSE ALIGNMENT - CONTINUED").
    /// Blank = the new views keep the long view's style and title.
    /// </summary>
    public string TitleSuffix { get; set; } = "- CONTINUED";

    public bool CreateLayouts { get; set; } = true;
    public bool LockViewports { get; set; } = true;
    public bool EraseSourceView { get; set; }

    /// <summary>Digits after the + in station labels: 3 for metric (1+250), 2 for imperial (12+50).</summary>
    public int StationPlusDigits { get; set; } = 3;
    public int StationDecimals { get; set; }

    /// <summary>Gap between the new views in model space, in drawing units (0 = a tenth of a view height).</summary>
    public double ModelGap { get; set; }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public StackerSettings Clone() => (StackerSettings)MemberwiseClone();

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static StackerSettings FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<StackerSettings>(json, Json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static StackerSettings Load(string path) =>
        File.Exists(path) ? FromJson(File.ReadAllText(path)) : new();

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ToJson());
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TTW", "ProfileStacker", "settings.json");
}
