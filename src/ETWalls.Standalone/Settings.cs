using System.Text.Json;
using ETWalls.Core.Design;

namespace ETWalls.Standalone;

/// <summary>What the app remembers between runs: standards folder and file, drawing scale, and the office rates.</summary>
internal sealed class Settings
{
    public string? StandardsFolder { get; set; }
    public string? StandardFile { get; set; }
    /// <summary>Drawing scale for the Detail tab and DXF (25 = 1:25); 0 = pick one that fits.</summary>
    public int Scale { get; set; }
    /// <summary>"Save as my rates": used for every new design.</summary>
    public Rates? Rates { get; set; }
    public string? DesignedBy { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ETWalls", "standalone.json");

    public static Settings Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new() : new();
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
            // Not being able to remember settings isn't worth interrupting the user for.
        }
    }
}
