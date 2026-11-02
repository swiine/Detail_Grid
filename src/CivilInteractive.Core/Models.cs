namespace CivilInteractive.Core;

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

/// <summary>Someone with CivilInteractive open, the rooms they're in, and what they're working on.</summary>
/// <param name="Drawing">The drawing they have in front of them (file name only), if any.</param>
/// <param name="LiveSession">The drafting session they're in, if any.</param>
/// <param name="Email">Their work email / sign-in name (for Teams).</param>
/// <param name="TeamsId">Their Microsoft 365 user id, once they've signed in to Teams through CivilInteractive.</param>
/// <param name="Left">They closed Civil 3D (the note stays, so friends can see when they were last on).</param>
public sealed record OnlinePerson(string User, string Name, string Machine, string[] Rooms, DateTimeOffset Seen,
    string? Drawing = null, string? LiveSession = null, string? Email = null, string? TeamsId = null, bool Left = false);

/// <summary>Someone on your friends list (saved on your PC only).</summary>
/// <param name="User">Their Windows login.</param>
public sealed record Friend(string User, string Name, string? Email = null);

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

    /// <summary>How a room is shown: "Lobby", "Job 241920" (private chats are named after the other person by the palette).</summary>
    public static string Title(string room) =>
        room == Lobby ? "Lobby" : room.StartsWith("job-") ? "Job " + room[4..] : room;

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
