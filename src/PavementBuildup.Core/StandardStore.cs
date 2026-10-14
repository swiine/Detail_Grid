using System.Text.Json;
using System.Text.Json.Serialization;

namespace PavementBuildup.Core;

public static class StandardStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PavementBuildup", "standard.json");

    public const string CompanyFileName = "TTW.json";

    public static List<string> SearchFrom { get; } = new();

    public static string? CompanyDefault => FindSupportFile(CompanyFileName);

    public static string? FindSupportFile(string fileName)
    {
        foreach (var start in SearchFrom)
        {
            try
            {
                var dir = new DirectoryInfo(start);
                for (int up = 0; up <= 4 && dir is not null; up++, dir = dir.Parent)
                {
                    var file = Path.Combine(dir.FullName, "_support", "_Standards", fileName);
                    if (File.Exists(file)) return file;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { }
        }
        return null;
    }

    public static string PathFor(DetailSettings settings) =>
        string.IsNullOrWhiteSpace(settings.StandardPath) ? CompanyDefault ?? DefaultPath : settings.StandardPath;

    public static string ToSetting(string path) =>
        SamePath(path, CompanyDefault) || (CompanyDefault is null && SamePath(path, DefaultPath)) ? "" : path;

    public static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

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
            var standard = Parse(File.ReadAllText(path));
            standard.SourceFolder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
            return standard;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"CAD standard {path} is not valid: {ex.Message}", ex);
        }
    }

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
