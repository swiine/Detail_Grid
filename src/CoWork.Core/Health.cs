using System.Diagnostics;

namespace CoWork.Core;

/// <summary>The checks behind CWCHECK that don't need AutoCAD.</summary>
public static class Health
{
    public enum Level { Ok, Warn, Fail }

    public sealed record Result(Level Level, string What, string Detail);

    /// <summary>
    /// Writes, reads and deletes a small file in the shared folder: whether it works, how long it takes, and how far
    /// this PC's clock is from the file server's (the time the server stamps on the file).
    /// </summary>
    public static (Result Folder, Result? Clock) CheckFolder(string folder, Func<DateTime>? utcNow = null)
    {
        utcNow ??= () => DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(folder)) return (new Result(Level.Fail, "Shared folder", "Not set - run CWFOLDER"), null);
        try
        {
            if (!Directory.Exists(folder)) return (new Result(Level.Fail, "Shared folder", $"Can't reach {folder}"), null);
            string dir = Path.Combine(folder, "logs", "check");
            string file = Path.Combine(dir, $"{Environment.MachineName}-{Guid.NewGuid():N}.tmp");
            var sw = Stopwatch.StartNew();
            Directory.CreateDirectory(dir);
            DateTime before = utcNow();
            File.WriteAllText(file, "CoWork check");
            DateTime after = utcNow();
            _ = File.ReadAllText(file);
            DateTime stamped = File.GetLastWriteTimeUtc(file);
            File.Delete(file);
            sw.Stop();
            long ms = sw.ElapsedMilliseconds;
            var speed = new Result(ms > 1500 ? Level.Fail : ms > 400 ? Level.Warn : Level.Ok, "Shared folder",
                $"Read/write OK in {ms} ms" + (ms > 400 ? " - slow; CoWork will lag (the server helps)" : ""));
            // The server's stamp should fall between "before" and "after" on a PC whose clock agrees with it.
            TimeSpan skew = stamped < before ? stamped - before : stamped > after ? stamped - after : TimeSpan.Zero;
            double s = Math.Abs(skew.TotalSeconds);
            var clock = new Result(s > 10 ? Level.Fail : s > 3 ? Level.Warn : Level.Ok, "Clock",
                s < 1 ? "In step with the file server"
                      : $"This PC is {s:0} s {(skew > TimeSpan.Zero ? "behind" : "ahead of")} the file server" + (s > 3 ? " - ask IT to fix the time sync (it can cause false \"host lost\")" : ""));
            return (speed, clock);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (new Result(Level.Fail, "Shared folder", $"Can't write to it: {ex.Message}"), null);
        }
    }

    /// <summary>The report as plain lines ("✔ Shared folder: ...").</summary>
    public static string Format(IEnumerable<Result> results) =>
        string.Join("\n", results.Select(r => $"{(r.Level == Level.Ok ? "✔" : r.Level == Level.Warn ? "!" : "✘")} {r.What}: {r.Detail}"));
}
