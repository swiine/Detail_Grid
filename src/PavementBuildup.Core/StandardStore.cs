using System.Text.Json;
using System.Text.Json.Serialization;

namespace PavementBuildup.Core;

/// <summary>Reads and writes <see cref="CadStandard"/> JSON files.</summary>
public static class StandardStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>%APPDATA%\PavementBuildup\standard.json</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PavementBuildup", "standard.json");

    /// <summary>The file a settings object points at (blank = personal default).</summary>
    public static string PathFor(DetailSettings settings) =>
        string.IsNullOrWhiteSpace(settings.StandardPath) ? DefaultPath : settings.StandardPath;

    /// <summary>
    /// Loads a standard. A missing personal standard gives the built-in one; a missing or unreadable
    /// file anywhere else is an error, because silently drawing to the wrong standard is worse.
    /// </summary>
    public static CadStandard Load(string path)
    {
        if (!File.Exists(path))
        {
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(DefaultPath), StringComparison.OrdinalIgnoreCase))
                return CadStandard.CreateDefault();
            throw new FileNotFoundException($"CAD standard not found: {path}", path);
        }

        try
        {
            var standard = JsonSerializer.Deserialize<CadStandard>(File.ReadAllText(path), Options)
                ?? throw new InvalidDataException("The file is empty.");
            return standard.Normalize();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"CAD standard {path} is not valid: {ex.Message}", ex);
        }
    }

    public static void Save(CadStandard standard, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize(standard));
        File.Move(tmp, path, overwrite: true);
    }

    public static string Serialize(CadStandard standard) => JsonSerializer.Serialize(standard, Options);
}
