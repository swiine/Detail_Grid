using System.Text.Json;

namespace PavementBuildup.Core;

public sealed class UserSettings
{
    public string Name { get; set; } = "";
    public double DefaultScale { get; set; } = 10;
    public int TextSize { get; set; } = 100;
    public bool CountUsage { get; set; } = true;

    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PavementBuildup", "user.json");

    public static string? Override { get; set; }

    private static UserSettings? _current;

    public static UserSettings Current => _current ??= Load();

    public static UserSettings Load()
    {
        try
        {
            var path = Override ?? FilePath;
            var s = File.Exists(path) ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path)) ?? new() : new();
            s.TextSize = Math.Clamp(s.TextSize, 90, 150);
            if (!(s.DefaultScale >= 1)) s.DefaultScale = 10;
            return s;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        try
        {
            var path = Override ?? FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        _current = this;
    }

    public static void Reset() => _current = null;
}
