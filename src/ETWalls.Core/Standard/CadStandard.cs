using System.Text.Json;
using ETWalls.Core.Drawing;

namespace ETWalls.Core.Standard;

public sealed record LayerStyle(string Name, string Color, double LineWeightMm, bool Plot);

/// <summary>
/// The parts of the company CAD standard (TTW.json, shared with the pavement tool) the wall section uses:
/// the layer for each kind of item. Missing or unreadable files fall back to the TTW defaults.
/// </summary>
public sealed class CadStandard
{
    public string Name { get; init; } = "TTW";
    public string? File { get; init; }
    public IReadOnlyDictionary<Layer, LayerStyle> Layers { get; init; } = Defaults;

    static readonly Dictionary<Layer, LayerStyle> Defaults = new()
    {
        [Layer.Outline] = new("Pen-25-c", "6", -1, true),
        [Layer.Hatch] = new("Pen-18-c", "8", -1, true),
        [Layer.Reinforcement] = new("pen-35-c", "2", -1, true),
        [Layer.Text] = new("TTW_stdGlobal_Text_25", "7", -1, true),
        [Layer.Dimension] = new("TTW_stdGlobal_Dim", "7", -1, true),
        [Layer.Leader] = new("TTW_stdGlobal_MLeader", "7", -1, true),
        [Layer.Title] = new("TTW_stdGlobal_Text_50", "1", -1, true),
    };

    public static CadStandard Default => new();

    /// <summary>Reads a TTW.json-style standard. Throws for a file that isn't valid JSON.</summary>
    public static CadStandard Load(string path)
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var layers = new Dictionary<Layer, LayerStyle>(Defaults);
        if (doc.RootElement.TryGetProperty("Layers", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var l in arr.EnumerateArray())
            {
                var element = l.TryGetProperty("Element", out var e) ? e.GetString() : null;
                if (element is null || !Enum.TryParse<Layer>(element, true, out var layer)) continue;
                layers[layer] = new LayerStyle(
                    l.TryGetProperty("Name", out var n) ? n.GetString() ?? "0" : "0",
                    l.TryGetProperty("Color", out var c) ? c.GetString() ?? "7" : "7",
                    l.TryGetProperty("LineWeightMm", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetDouble() : -1,
                    !l.TryGetProperty("Plot", out var p) || p.ValueKind != JsonValueKind.False);
            }
        var name = doc.RootElement.TryGetProperty("Name", out var nm) ? nm.GetString() ?? Path.GetFileNameWithoutExtension(path) : Path.GetFileNameWithoutExtension(path);
        return new CadStandard { Name = name, File = path, Layers = layers };
    }
}
