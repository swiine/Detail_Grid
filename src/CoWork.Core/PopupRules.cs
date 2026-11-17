namespace CoWork.Core;

/// <summary>
/// When CoWork pops up. Only for things meant for you: a private message, an @mention, or something in a job you're
/// working in (a drawing of that job is open). Everything else - General, other jobs, friends coming online, updates -
/// goes to the command line and the unread counts instead.
/// </summary>
public static class PopupRules
{
    /// <summary>The job number of a job room ("job-241796"), or null.</summary>
    public static string? JobOfRoom(string roomId) => roomId.StartsWith("job-", StringComparison.Ordinal) ? roomId[4..] : null;

    /// <summary>Whether a chat message should pop up.</summary>
    public static bool ForMessage(string roomId, bool mentioned, IEnumerable<string> myJobs) =>
        mentioned || Rooms.IsPrivate(roomId) || roomId.StartsWith("teams:", StringComparison.Ordinal)
        || (JobOfRoom(roomId) is { } job && myJobs.Contains(job));

    /// <summary>The jobs you're working in: those of the drawings you have open.</summary>
    public static HashSet<string> JobsOf(IEnumerable<string> openDrawings) =>
        new(openDrawings.Select(JobAlerts.JobOf).OfType<string>());
}
