using System.Text.Json;

namespace PavementBuildup;

/// <summary>What the standalone app remembers: the standards folder, the chosen standard and the scale.</summary>
internal sealed class StandaloneSettings
{
    public string? StandardsFolder { get; set; }
    public string? StandardFile { get; set; }
    public double Scale { get; set; } = 10;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PavementBuildup", "standalone.json");

    public static StandaloneSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<StandaloneSettings>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember the folder isn't worth interrupting the user for.
        }
    }
}
