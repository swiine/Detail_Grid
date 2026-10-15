using System.Text.Json;

namespace PavementBuildup;

internal sealed class StandaloneSettings
{
    public string? StandardsFolder { get; set; }
    public string? StandardFile { get; set; }
    public double Scale { get; set; } = PavementBuildup.Core.UserSettings.Current.DefaultScale;
    public string Paper { get; set; } = "A4";

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
        }
    }
}
