namespace CoWork.Core;

/// <summary>A button shown under a message that runs a Civil 3D command when clicked (e.g. "Join game").</summary>
public sealed record MessageButton(string Label, string Command);

/// <summary>One chat message.</summary>
/// <param name="Id">Unique id (also used to drop duplicates).</param>
/// <param name="Room">The room it was posted in ("lobby", "job-241920", ...).</param>
/// <param name="User">Windows login of the sender.</param>
/// <param name="Name">The sender's display name.</param>
/// <param name="Drawing">The drawing it was sent from, if any.</param>
public sealed record ChatMessage(
    string Id, string Room, string User, string Name, DateTimeOffset Time, string Text,
    string? Drawing = null, MessageButton? Button = null);

/// <summary>Someone with CoWork open, the rooms they're in, and what they're working on.</summary>
/// <param name="Drawing">The drawing they have in front of them (file name only), if any.</param>
/// <param name="LiveSession">The CoWork session they're in, if any.</param>
/// <param name="Email">Their work email / sign-in name (for Teams).</param>
/// <param name="TeamsId">Their Microsoft 365 user id, once they've signed in to Teams through CoWork.</param>
/// <param name="Left">They closed Civil 3D (the note stays, so friends can see when they were last on).</param>
/// <param name="OpenDrawings">Every drawing they have open (full network paths), for "who has this open?".</param>
public sealed record OnlinePerson(string User, string Name, string Machine, string[] Rooms, DateTimeOffset Seen,
    string? Drawing = null, string? LiveSession = null, string? Email = null, string? TeamsId = null, bool Left = false,
    string[]? OpenDrawings = null)
{
    /// <summary>Whether they have this drawing open (paths compared without regard to case or slashes).</summary>
    public bool HasOpen(string path) => OpenDrawings?.Any(d => Paths.Same(d, path)) == true;
}

/// <summary>Comparing drawing paths written on different PCs.</summary>
public static class Paths
{
    public static string Norm(string p) => p.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
    public static bool Same(string a, string b) => Norm(a) == Norm(b);
}

/// <summary>
/// A shared view: "look here". Where in which drawing (centre and size of the view) and which objects to point
/// out (by handle). Kept as views\&lt;id&gt;.json in the CoWork folder; the chat message carries a button to go there.
/// </summary>
public sealed record SharedView(string Id, string Drawing, double CenterX, double CenterY, double Width, double Height,
    string[] Handles, string By, DateTimeOffset Time)
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static string NewId() => Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 5).ToLowerInvariant();

    public static string FileOf(string dataFolder, string id) => Path.Combine(dataFolder, "views", id + ".json");

    public void Save(string dataFolder)
    {
        string f = FileOf(dataFolder, Id);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, System.Text.Json.JsonSerializer.Serialize(this, Json));
    }

    public static SharedView? Load(string dataFolder, string id)
    {
        if (id.Length == 0 || id.Any(c => !Uri.IsHexDigit(c))) return null; // ids are hex: no paths sneak in
        try
        {
            string f = FileOf(dataFolder, id);
            return File.Exists(f) ? System.Text.Json.JsonSerializer.Deserialize<SharedView>(File.ReadAllText(f), Json) : null;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { return null; }
    }
}

/// <summary>Someone on your friends list (saved on your PC only).</summary>
/// <param name="User">Their Windows login.</param>
public sealed record Friend(string User, string Name, string? Email = null);

/// <summary>One of your own buttons on the CoWork strip: a label, the command it runs, and an icon name.</summary>
/// <param name="Command">Typed as if at the command line, e.g. "QSAVE" or "-PURGE A * N".</param>
public sealed record MenuCommand(string Label, string Command, string Icon = "cmd");

/// <summary>Rooms everyone has, and the rule for job rooms.</summary>
public static class Rooms
{
    public const string Lobby = "lobby";

    /// <summary>"job-241920" for a drawing under ...\jobs\24\241920\..., or null.</summary>
    public static string? ForDrawing(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(path, @"[\\/]jobs[\\/]\d{2}[\\/](?<job>\d{5,7})(?=[\\/]|$)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? "job-" + m.Groups["job"].Value : null;
    }

    /// <summary>How a room is shown: "General" (id "lobby"), "Job 241920" (private chats are named after the other person by the palette).</summary>
    public static string Title(string room) =>
        room == Lobby ? "General" : room.StartsWith("job-") ? "Job " + room[4..] : room;

    /// <summary>A login reduced to letters and digits (for room names).</summary>
    public static string Safe(string user) => new(user.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

    /// <summary>The private chat room for two people: "dm-cal--hana" (the same whoever opens it).</summary>
    public static string Private(string userA, string userB)
    {
        string[] pair = { Safe(userA), Safe(userB) };
        Array.Sort(pair, StringComparer.Ordinal);
        return $"dm-{pair[0]}--{pair[1]}";
    }

    public static bool IsPrivate(string room) => room.StartsWith("dm-") && room.Contains("--");

    /// <summary>For a private room, the other person's (safe) login.</summary>
    public static string? OtherPerson(string room, string me)
    {
        if (!IsPrivate(room)) return null;
        string[] parts = room[3..].Split("--");
        if (parts.Length != 2) return null;
        string mine = Safe(me);
        return parts[0] == mine ? parts[1] : parts[1] == mine ? parts[0] : null;
    }

    /// <summary>Only simple names are allowed as folder names.</summary>
    public static bool IsValid(string room) =>
        room.Length is > 0 and <= 40 && room.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
}
