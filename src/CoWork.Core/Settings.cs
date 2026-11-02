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

    /// <summary>Colour scheme: one of <see cref="Brand.Palettes"/> ("mint" unless you pick another).</summary>
    public string? Theme { get; set; }

    /// <summary>Logo on the status bar and the strip: one of <see cref="Brand.Logos"/>.</summary>
    public string? Logo { get; set; }

    /// <summary>How often the chat checks for new messages, in milliseconds (default 500; at least 250).</summary>
    public int? RefreshMs { get; set; }

    /// <summary>How often a CoWork session swaps changes and crosshairs, in milliseconds (default 200; at least 100).</summary>
    public int? LiveRefreshMs { get; set; }

    public TimeSpan ChatRefresh => TimeSpan.FromMilliseconds(Math.Max(250, RefreshMs ?? 500));
    public TimeSpan LiveRefresh => TimeSpan.FromMilliseconds(Math.Max(100, LiveRefreshMs ?? 200));

    /// <summary>Chat tabs you've closed (their history is kept; they reopen when needed).</summary>
    public List<string>? HiddenRooms { get; set; }

    /// <summary>Your crosshair colour in CoWork sessions ("#RRGGBB" from <see cref="CrosshairColours"/>; null = automatic).</summary>
    public string? CrosshairColour { get; set; }

    /// <summary>Your own buttons on the CoWork strip (null = the starting set).</summary>
    public List<MenuCommand>? Commands { get; set; }

    public static readonly IReadOnlyList<MenuCommand> DefaultCommands = new[]
    {
        new MenuCommand("Save", "QSAVE", "save"),
        new MenuCommand("Plot", "PLOT", "plot"),
        new MenuCommand("Purge all", "-PURGE A * N", "trash"),
        new MenuCommand("Reload xrefs", "-XREF R *", "xref"),
    };

    /// <summary>The buttons to show: yours, or the starting set if you haven't changed them.</summary>
    public IReadOnlyList<MenuCommand> MenuCommands => Commands ?? DefaultCommands;

    public string ThemeName => Brand.PaletteFor(Theme).Id;
    public string LogoName => Brand.LogoFor(Logo);

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
            if (!string.IsNullOrWhiteSpace(u.Theme)) s.Theme = u.Theme;
            if (!string.IsNullOrWhiteSpace(u.Logo)) s.Logo = u.Logo;
            if (u.Commands != null) s.Commands = u.Commands;
            if (!string.IsNullOrWhiteSpace(u.CrosshairColour)) s.CrosshairColour = u.CrosshairColour;
            if (u.HiddenRooms != null) s.HiddenRooms = u.HiddenRooms;
            if (u.RefreshMs != null) s.RefreshMs = u.RefreshMs;
            if (u.LiveRefreshMs != null) s.LiveRefreshMs = u.LiveRefreshMs;
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
