using System.Globalization;
using System.Text;

namespace CoWork.Core;

/// <summary>
/// "Time on job" for timesheets: minutes per day per job, counted only while you're actually working in a job's
/// drawing. Off unless you turn it on. Kept only on your own PC (%APPDATA%\CoWork\time\yyyy-MM.csv) - never in the
/// shared folder, so nobody else can see it.
/// </summary>
public sealed class TimeLog
{
    private readonly string _folder;
    private readonly object _lock = new();

    public TimeLog(string folder) => _folder = folder;

    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoWork", "time");

    public sealed record Entry(DateOnly Day, string Job, int Minutes);

    private string FileFor(DateOnly day) => Path.Combine(_folder, day.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".csv");

    /// <summary>Adds minutes to a job on a day.</summary>
    public void Add(DateOnly day, string job, int minutes = 1)
    {
        if (string.IsNullOrWhiteSpace(job) || minutes <= 0) return;
        lock (_lock)
        {
            var month = ReadFile(FileFor(day));
            int i = month.FindIndex(e => e.Day == day && e.Job == job);
            if (i >= 0) month[i] = month[i] with { Minutes = month[i].Minutes + minutes };
            else month.Add(new Entry(day, job, minutes));
            Directory.CreateDirectory(_folder);
            string tmp = FileFor(day) + ".tmp";
            File.WriteAllText(tmp, "date,job,minutes\n" + string.Concat(month.OrderBy(e => e.Day).ThenBy(e => e.Job)
                .Select(e => $"{e.Day:yyyy-MM-dd},{e.Job},{e.Minutes}\n")));
            File.Move(tmp, FileFor(day), overwrite: true);
        }
    }

    /// <summary>Everything between two days (inclusive), oldest first.</summary>
    public List<Entry> Between(DateOnly from, DateOnly to)
    {
        var result = new List<Entry>();
        lock (_lock)
            for (var m = new DateOnly(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
                result.AddRange(ReadFile(FileFor(m)).Where(e => e.Day >= from && e.Day <= to));
        return result.OrderBy(e => e.Day).ThenBy(e => e.Job).ToList();
    }

    /// <summary>A timesheet-ready CSV: date, job, hours (to the nearest quarter hour), minutes.</summary>
    public static string Csv(IEnumerable<Entry> entries)
    {
        var sb = new StringBuilder("Date,Job,Hours,Minutes\n");
        foreach (Entry e in entries)
            sb.Append($"{e.Day:yyyy-MM-dd},{e.Job},{Hours(e.Minutes).ToString("0.00", CultureInfo.InvariantCulture)},{e.Minutes}\n");
        return sb.ToString();
    }

    /// <summary>Minutes as hours rounded to the nearest quarter (but never 0 for real work).</summary>
    public static double Hours(int minutes) => minutes <= 0 ? 0 : Math.Max(0.25, Math.Round(minutes / 15.0, MidpointRounding.AwayFromZero) / 4);

    /// <summary>"241796 3.5 h, 250012 1.25 h".</summary>
    public static string Summary(IEnumerable<Entry> entries) =>
        string.Join(", ", entries.GroupBy(e => e.Job).OrderByDescending(g => g.Sum(e => e.Minutes))
            .Select(g => $"{g.Key} {Hours(g.Sum(e => e.Minutes)).ToString("0.##", CultureInfo.InvariantCulture)} h"));

    private static List<Entry> ReadFile(string file)
    {
        var list = new List<Entry>();
        try
        {
            if (!File.Exists(file)) return list;
            foreach (string line in File.ReadAllLines(file).Skip(1))
            {
                string[] p = line.Split(',');
                if (p.Length >= 3 && DateOnly.TryParseExact(p[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly d)
                    && int.TryParse(p[2], out int min))
                    list.Add(new Entry(d, p[1], min));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return list;
    }
}
