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

/// <summary>Someone with CivilInteractive open, and the rooms they're in.</summary>
public sealed record OnlinePerson(string User, string Name, string Machine, string[] Rooms, DateTimeOffset Seen);

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

    /// <summary>How a room is shown: "Lobby", "Job 241920".</summary>
    public static string Title(string room) =>
        room == Lobby ? "Lobby" : room.StartsWith("job-") ? "Job " + room[4..] : room;

    /// <summary>Only simple names are allowed as folder names.</summary>
    public static bool IsValid(string room) =>
        room.Length is > 0 and <= 40 && room.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
}
