using System.Text.Json;
using ETWalls.Core.Design;

namespace ETWalls.Standalone;

/// <summary>A saved design (.etwall): every input as JSON, so it can be opened again later.</summary>
internal static class DesignFile
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Write(WallInput input) => JsonSerializer.Serialize(new Saved(1, input), Options);

    public static WallInput Read(string json) =>
        JsonSerializer.Deserialize<Saved>(json, Options)?.Design ?? throw new JsonException("The file has no design in it.");

    private sealed record Saved(int Version, WallInput Design);
}
