namespace CoWork.Core;

/// <summary>
/// "Someone's in your job": who else has a drawing open in a job you're working in (the 6-digit job folder, as for
/// safety copies). Different jobs never alert. The same drawing is flagged so it can say "also has it open".
/// </summary>
public static class JobAlerts
{
    public sealed record Alert(OnlinePerson Person, string Drawing, string Job, bool SameDrawing)
    {
        /// <summary>Alert once per person and drawing (again only if they close and reopen it).</summary>
        public string Key => Person.User.ToLowerInvariant() + "|" + Paths.Norm(Drawing);
    }

    /// <summary>The job number of a drawing path, or null if it isn't in a job folder.</summary>
    public static string? JobOf(string path) => Backups.Locate(path).Job;

    public static List<Alert> Find(IEnumerable<string> myDrawings, IEnumerable<OnlinePerson> others, string me)
    {
        var mine = myDrawings.Where(d => d.Length > 0).ToList();
        var myJobs = new HashSet<string>(mine.Select(JobOf).OfType<string>());
        var alerts = new List<Alert>();
        if (myJobs.Count == 0) return alerts;
        foreach (OnlinePerson p in others)
        {
            if (string.Equals(p.User, me, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (string theirs in p.OpenDrawings ?? Array.Empty<string>())
                if (JobOf(theirs) is { } job && myJobs.Contains(job))
                    alerts.Add(new Alert(p, theirs, job, mine.Any(m => Paths.Same(m, theirs))));
        }
        return alerts;
    }
}
