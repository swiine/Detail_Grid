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

    /// <summary>The company standard's file name, looked for in "_support\_Standards".</summary>
    public const string CompanyFileName = "TTW.json";

    /// <summary>
    /// Folders the app runs from (the plugin's Contents folder, the standalone app's folder). The company
    /// standard is looked for in "_support\_Standards" there and up to four folders above.
    /// </summary>
    public static List<string> SearchFrom { get; } = new();

    /// <summary>The nearest "_support\_Standards\TTW.json", or null if there isn't one.</summary>
    public static string? CompanyDefault
    {
        get
        {
            foreach (var start in SearchFrom)
            {
                try
                {
                    var dir = new DirectoryInfo(start);
                    for (int up = 0; up <= 4 && dir is not null; up++, dir = dir.Parent)
                    {
                        var file = Path.Combine(dir.FullName, "_support", "_Standards", CompanyFileName);
                        if (File.Exists(file)) return file;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { }
            }
            return null;
        }
    }

    /// <summary>
    /// The file a settings object points at. Blank = the default: the company "_support\_Standards\TTW.json",
    /// else the personal standard.
    /// </summary>
    public static string PathFor(DetailSettings settings) =>
        string.IsNullOrWhiteSpace(settings.StandardPath) ? CompanyDefault ?? DefaultPath : settings.StandardPath;

    /// <summary>What to store in the settings for a chosen file: blank when it is the default anyway.</summary>
    public static string ToSetting(string path) =>
        SamePath(path, CompanyDefault) || (CompanyDefault is null && SamePath(path, DefaultPath)) ? "" : path;

    public static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

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
            return Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"CAD standard {path} is not valid: {ex.Message}", ex);
        }
    }

    /// <summary>A standard from its JSON text (throws JsonException / InvalidDataException).</summary>
    public static CadStandard Parse(string json) =>
        (JsonSerializer.Deserialize<CadStandard>(json, Options) ?? throw new InvalidDataException("The file is empty.")).Normalize();

    public static void Save(CadStandard standard, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize(standard));
        File.Move(tmp, path, overwrite: true);
    }

    public static string Serialize(CadStandard standard) => JsonSerializer.Serialize(standard, Options);
}
