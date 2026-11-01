using System.Reflection;
using System.Text.Json;

namespace CivilInteractive.Core;

/// <summary>
/// CivilInteractive's settings: the defaults shipped next to the plugin (civilinteractive.json), then each
/// person's own (%APPDATA%\CivilInteractive\settings.json). It never reads or writes any other tool's settings.
/// </summary>
public sealed class CiSettings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Where everyone's messages are kept (a shared folder everyone can write to).</summary>
    public string? Folder { get; set; }

    /// <summary>Name shown to others (blank = the Windows login).</summary>
    public string? DisplayName { get; set; }

    /// <summary>Pop up in the status bar when a message arrives while the palette is closed.</summary>
    public bool Popups { get; set; } = true;

    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Environment.UserName : DisplayName!.Trim();

    public static string UserFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CivilInteractive", "settings.json");

    /// <summary>civilinteractive.json next to the CivilInteractive DLLs (in the bundle).</summary>
    public static string DefaultsFile => Path.Combine(
        Path.GetDirectoryName(typeof(CiSettings).Assembly.Location) ?? AppContext.BaseDirectory, "civilinteractive.json");

    public static CiSettings Load(string? defaultsFile = null, string? userFile = null)
    {
        CiSettings s = Read(defaultsFile ?? DefaultsFile) ?? new CiSettings();
        if (Read(userFile ?? UserFile) is { } u)
        {
            if (!string.IsNullOrWhiteSpace(u.Folder)) s.Folder = u.Folder;
            if (!string.IsNullOrWhiteSpace(u.DisplayName)) s.DisplayName = u.DisplayName;
            s.Popups = u.Popups;
        }
        if (!IsAllowedFolder(s.Folder)) s.Folder = null;
        return s;
    }

    /// <summary>Saves this person's own settings.</summary>
    public void Save(string? userFile = null)
    {
        string f = userFile ?? UserFile;
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, JsonSerializer.Serialize(this, Json));
    }

    /// <summary>Keeps CivilInteractive well away from other chat tools' folders.</summary>
    public static bool IsAllowedFolder(string? folder) =>
        string.IsNullOrWhiteSpace(folder) || !folder.Contains("TTWChat", StringComparison.OrdinalIgnoreCase);

    private static CiSettings? Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<CiSettings>(File.ReadAllText(path), Json) : null; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
}
