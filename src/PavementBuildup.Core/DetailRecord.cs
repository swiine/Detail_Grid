using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PavementBuildup.Core;

public sealed class DetailRecord
{
    public const int CurrentVersion = 1;

    public const string XrecordKey = "PAVEMENT_BUILDUP_DETAIL";

    public int Version { get; set; } = CurrentVersion;
    public Buildup Buildup { get; set; } = new();
    public DetailSettings Settings { get; set; } = new();

    public double UnitsPerMm { get; set; } = 1;

    public double[]? Placement { get; set; }

    public string? AnchorHandle { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public const int ChunkLength = 240;

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static DetailRecord FromJson(string json)
    {
        var record = JsonSerializer.Deserialize<DetailRecord>(json, Options)
                     ?? throw new InvalidDataException("Empty pavement detail data.");
        if (record.Version > CurrentVersion)
            throw new InvalidDataException("This detail was drawn by a newer version of the Pavement Build-up plugin. Update the plugin to edit it.");
        record.Buildup ??= new Buildup();
        record.Buildup.Layers ??= new();
        foreach (var l in record.Buildup.Layers)
            l.Reinforcement ??= new();
        record.Settings ??= new DetailSettings();
        if (!(record.UnitsPerMm > 0))
            record.UnitsPerMm = 1;
        return record;
    }

    public IReadOnlyList<string> ToChunks()
    {
        var json = ToJson();
        var chunks = new List<string>();
        int i = 0;
        while (i < json.Length)
        {
            int len = Math.Min(ChunkLength, json.Length - i);
            if (len < json.Length - i && char.IsHighSurrogate(json[i + len - 1]))
                len--;
            chunks.Add(json.Substring(i, len));
            i += len;
        }
        return chunks;
    }

    public static DetailRecord FromChunks(IEnumerable<string> chunks)
    {
        var sb = new StringBuilder();
        foreach (var c in chunks)
            sb.Append(c);
        return FromJson(sb.ToString());
    }
}
