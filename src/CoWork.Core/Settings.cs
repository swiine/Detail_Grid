using System.Reflection;
using System.Text.Json;

namespace CoWork.Core;

/// <summary>
/// CoWork's settings: the defaults shipped next to the plugin (cowork.json), then each
/// person's own (%APPDATA%\CoWork\settings.json). It never reads or writes any other tool's settings.
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

    /// <summary>Your friends (saved on your PC only).</summary>
    public List<Friend> Friends { get; set; } = new();

    /// <summary>Pop up when a friend comes online.</summary>
    public bool FriendAlerts { get; set; } = true;

    /// <summary>The Teams link, set up by IT (blank = off). Comes from the shipped defaults.</summary>
    public TeamsSettings Teams { get; set; } = new();

    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Environment.UserName : DisplayName!.Trim();

    public static string UserFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoWork", "settings.json");

    /// <summary>cowork.json next to the CoWork DLLs (in the bundle).</summary>
    public static string DefaultsFile => Path.Combine(
        Path.GetDirectoryName(typeof(CiSettings).Assembly.Location) ?? AppContext.BaseDirectory, "cowork.json");

    public static CiSettings Load(string? defaultsFile = null, string? userFile = null)
    {
        CiSettings s = Read(defaultsFile ?? DefaultsFile) ?? new CiSettings();
        if (Read(userFile ?? UserFile) is { } u)
        {
            if (!string.IsNullOrWhiteSpace(u.Folder)) s.Folder = u.Folder;
            if (!string.IsNullOrWhiteSpace(u.DisplayName)) s.DisplayName = u.DisplayName;
            s.Popups = u.Popups;
            s.Friends = u.Friends ?? new();
            s.FriendAlerts = u.FriendAlerts;
        }
        if (!IsAllowedFolder(s.Folder)) s.Folder = null;
        return s;
    }

    /// <summary>Just this person's own settings (to change and save), without the shipped defaults.</summary>
    public static CiSettings LoadUserOnly(string? userFile = null) => Read(userFile ?? UserFile) ?? new CiSettings();

    /// <summary>Saves this person's own settings.</summary>
    public void Save(string? userFile = null)
    {
        string f = userFile ?? UserFile;
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, JsonSerializer.Serialize(this, Json));
    }

    /// <summary>Adds (or renames) a friend; returns false if they were already on the list.</summary>
    public bool AddFriend(Friend f)
    {
        int i = Friends.FindIndex(x => string.Equals(x.User, f.User, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) { Friends[i] = f with { Email = f.Email ?? Friends[i].Email }; return false; }
        Friends.Add(f);
        Friends.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    public bool RemoveFriend(string user) => Friends.RemoveAll(x => string.Equals(x.User, user, StringComparison.OrdinalIgnoreCase)) > 0;

    public bool IsFriend(string user) => Friends.Any(x => string.Equals(x.User, user, StringComparison.OrdinalIgnoreCase));

    /// <summary>Keeps CoWork well away from other chat tools' folders.</summary>
    public static bool IsAllowedFolder(string? folder) =>
        string.IsNullOrWhiteSpace(folder) || !folder.Contains("TTWChat", StringComparison.OrdinalIgnoreCase);

    private static CiSettings? Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<CiSettings>(File.ReadAllText(path), Json) : null; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
}

/// <summary>
/// The Teams link (Microsoft Graph). IT registers an app in Microsoft 365 and puts its ids in cowork.json;
/// until then this is off. See TEAMS-IT-SETUP.md.
/// </summary>
public sealed class TeamsSettings
{
    /// <summary>The company's Microsoft 365 tenant (directory) id.</summary>
    public string? TenantId { get; set; }

    /// <summary>The registered app's client (application) id.</summary>
    public string? ClientId { get; set; }

    public bool IsSetUp => !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId);
}
